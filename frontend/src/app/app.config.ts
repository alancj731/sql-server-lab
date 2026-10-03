import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { AUTH_CONTEXT, AuthContext, authInterceptor } from './core/auth';

export function buildAppConfig(auth: AuthContext): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      { provide: AUTH_CONTEXT, useValue: auth },
      provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
      provideRouter(routes, withComponentInputBinding()),
    ],
  };
}
