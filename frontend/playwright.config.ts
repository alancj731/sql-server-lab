import { defineConfig, devices } from '@playwright/test';
import { mkdirSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';

// Isolated stack: its own ports and SQLite file, short simulated delays.
const API_PORT = 5181;
const WEB_PORT = 4301;
// The config is evaluated again in every test worker; only the main process (no run ID yet) resets the data.
const dataDir = resolve(__dirname, '.e2e-data');
if (!process.env['E2E_RUN_ID']) {
  process.env['E2E_RUN_ID'] = Date.now().toString(36);
  rmSync(dataDir, { recursive: true, force: true });
  mkdirSync(dataDir, { recursive: true });
}

const backend = resolve(__dirname, '../backend/src');
const sharedEnv = {
  ControlDb__ConnectionString: `Data Source=${join(dataDir, `e2e-${process.env['E2E_RUN_ID']}.db`)};Default Timeout=30`,
  ControlDb__MigrateOnStartup: 'true',
  LocalSimulation__ProvisionSeconds: '3',
  LocalSimulation__StartSeconds: '2',
  LocalSimulation__DeallocateSeconds: '2',
  LocalSimulation__DeleteSeconds: '2',
  LocalSimulation__SqlReadySeconds: '1',
  LabLimits__MaxActiveLabsPerUser: '20',
  RateLimiting__MutationsPerMinute: '1000',
};

export default defineConfig({
  testDir: './e2e',
  timeout: 90_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI']
    ? [['list'], ['junit', { outputFile: 'test-results/e2e-junit.xml' }]]
    : 'list',
  use: {
    baseURL: `http://localhost:${WEB_PORT}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      // API and worker share one process group so Playwright starts and stops them together.
      command:
        `bash -c 'dotnet run --project ${backend}/SqlServerLab.Worker --no-build & ` +
        `exec dotnet run --project ${backend}/SqlServerLab.Api --no-launch-profile --no-build'`,
      url: `http://localhost:${API_PORT}/health/ready`,
      env: {
        ...sharedEnv,
        ASPNETCORE_ENVIRONMENT: 'Development',
        DOTNET_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: `http://localhost:${API_PORT}`,
        Worker__PollIntervalMs: '250',
        Worker__OperationPollIntervalMs: '250',
      },
      timeout: 180_000,
      reuseExistingServer: false,
    },
    {
      command: `npx ng serve --port ${WEB_PORT}`,
      url: `http://localhost:${WEB_PORT}`,
      env: { API_URL: `http://localhost:${API_PORT}` },
      timeout: 180_000,
      reuseExistingServer: false,
    },
  ],
});
