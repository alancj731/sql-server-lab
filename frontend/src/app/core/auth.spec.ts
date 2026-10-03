import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AUTH_CONTEXT, AuthService, authInterceptor, initializeAuth } from './auth';

describe('authInterceptor', () => {
  function setup(enabled: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { enabled, accessToken: () => Promise.resolve('token-123') },
        },
      ],
    });
    return { http: TestBed.inject(HttpClient), ctl: TestBed.inject(HttpTestingController) };
  }

  it('adds a bearer token to API calls when Entra sign-in is enabled', async () => {
    const { http, ctl } = setup(true);
    http.get('/api/v1/labs').subscribe();
    await Promise.resolve();
    await Promise.resolve();
    const req = ctl.expectOne('/api/v1/labs');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-123');
    req.flush([]);
  });

  it('never sends tokens to non-API URLs or in development mode', () => {
    const { http, ctl } = setup(true);
    http.get('/config.json').subscribe();
    expect(ctl.expectOne('/config.json').request.headers.has('Authorization')).toBe(false);
    TestBed.resetTestingModule();

    const dev = setup(false);
    dev.http.get('/api/v1/labs').subscribe();
    expect(dev.ctl.expectOne('/api/v1/labs').request.headers.has('Authorization')).toBe(false);
  });
});

describe('initializeAuth', () => {
  it('uses development mode when the config is incomplete', async () => {
    const ctx = await initializeAuth({
      authMode: 'Entra',
      tenantId: 't',
      clientId: null,
      authority: null,
      apiScope: null,
    });
    expect(ctx?.msal).toBeNull();
    expect(ctx?.config.authMode).toBe('Development');
  });

  it('AuthService is disabled without MSAL', () => {
    TestBed.configureTestingModule({});
    const auth = TestBed.inject(AuthService);
    expect(auth.enabled).toBe(false);
    expect(TestBed.inject(AUTH_CONTEXT).config.authMode).toBe('Development');
  });
});
