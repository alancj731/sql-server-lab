import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { buildAppConfig } from './app/app.config';
import { initializeAuth, loadClientConfig } from './app/core/auth';

async function main(): Promise<void> {
  const auth = await initializeAuth(await loadClientConfig());
  if (!auth) {
    return; // Redirecting to Microsoft sign-in.
  }

  await bootstrapApplication(App, buildAppConfig(auth));
}

main().catch((err) => console.error(err));
