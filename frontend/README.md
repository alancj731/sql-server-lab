# SQL Server Lab — Angular SPA

See the [root README](../README.md) for setup. Design rules live in [DESIGN.md](./DESIGN.md).

| Command | Purpose |
| --- | --- |
| `npm start` | Dev server on :4200, proxying `/api` and `/hubs` to the API on :5080 (`API_URL` overrides) |
| `npm run build` | Production build |
| `npm test` | Unit tests (Vitest via `ng test`) |
| `npm run lint` / `npm run format:check` | ESLint (angular-eslint) / Prettier |
| `npm run generate:api` | Regenerate `src/app/api/openapi.json` and `schema.d.ts` from the API |
| `npm run e2e` | Playwright against an isolated API + worker + dev server |

API types are generated, never hand-written: `src/app/api/models.ts` only re-exports from `schema.d.ts`.
