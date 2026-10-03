import { HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, Injectable, inject, signal } from '@angular/core';
// Type-only import: MSAL (~130 kB) is loaded lazily, and only when Entra sign-in is configured.
import type { AccountInfo, PublicClientApplication } from '@azure/msal-browser';
import { from, switchMap } from 'rxjs';

/** Public sign-in settings served by the API at /config.json (identifiers only, never secrets). */
export interface ClientConfig {
  authMode: 'Entra' | 'Development';
  tenantId: string | null;
  clientId: string | null;
  authority: string | null;
  apiScope: string | null;
}

export interface AuthContext {
  config: ClientConfig;
  msal: PublicClientApplication | null;
}

export const AUTH_CONTEXT = new InjectionToken<AuthContext>('AUTH_CONTEXT', {
  factory: () => ({ config: DEVELOPMENT, msal: null }),
});

const DEVELOPMENT: ClientConfig = {
  authMode: 'Development',
  tenantId: null,
  clientId: null,
  authority: null,
  apiScope: null,
};

export async function loadClientConfig(): Promise<ClientConfig> {
  try {
    const response = await fetch('/config.json', { cache: 'no-store' });
    return response.ok ? ((await response.json()) as ClientConfig) : DEVELOPMENT;
  } catch {
    return DEVELOPMENT;
  }
}

/**
 * Entra sign-in with authorization code + PKCE (redirect flow). Returns null when the browser is being redirected
 * to sign in; the app must not bootstrap in that case.
 */
export async function initializeAuth(config: ClientConfig): Promise<AuthContext | null> {
  if (config.authMode !== 'Entra' || !config.clientId || !config.authority || !config.apiScope) {
    return { config: DEVELOPMENT, msal: null };
  }

  const { PublicClientApplication } = await import('@azure/msal-browser');
  const msal = new PublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: config.authority,
      redirectUri: `${window.location.origin}/`,
      postLogoutRedirectUri: `${window.location.origin}/`,
    },
    cache: { cacheLocation: 'sessionStorage' },
  });
  await msal.initialize();

  const result = await msal.handleRedirectPromise();
  const account = result?.account ?? msal.getActiveAccount() ?? msal.getAllAccounts()[0] ?? null;
  if (!account) {
    await msal.loginRedirect({ scopes: [config.apiScope] });
    return null;
  }

  msal.setActiveAccount(account);
  return { config, msal };
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly context = inject(AUTH_CONTEXT);

  readonly enabled = this.context.config.authMode === 'Entra' && !!this.context.msal;
  readonly account = signal<AccountInfo | null>(this.context.msal?.getActiveAccount() ?? null);

  /** Access token for the API, acquired silently; falls back to a redirect when interaction is required. */
  async accessToken(): Promise<string> {
    const { msal, config } = this.context;
    if (!msal || !config.apiScope) {
      return '';
    }

    const request = { scopes: [config.apiScope], account: msal.getActiveAccount() ?? undefined };
    try {
      return (await msal.acquireTokenSilent(request)).accessToken;
    } catch (error) {
      if (error instanceof Error && error.name === 'InteractionRequiredAuthError') {
        await msal.acquireTokenRedirect(request);
      }

      throw error;
    }
  }

  signOut(): void {
    void this.context.msal?.logoutRedirect();
  }
}

/** Adds the bearer token to same-origin API calls when Entra sign-in is enabled. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  if (!auth.enabled || !req.url.startsWith('/api/')) {
    return next(req);
  }

  return from(auth.accessToken()).pipe(
    switchMap((token) => next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))),
  );
};
