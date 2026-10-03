# Locked versions

Recorded at Milestone 0–2 (2026-09-30), updated for Milestone 3 (2026-10-01). Do not upgrade during the build unless required for security or
compatibility; update this file with any change. Backend versions are pinned centrally in
`backend/Directory.Packages.props` with `packages.lock.json` per project; frontend versions are locked by
`frontend/package-lock.json`.

## Toolchain

| Tool | Version |
| --- | --- |
| .NET SDK | 10.0.110 (target `net10.0`, LTS) |
| Node.js | 22.23.2 |
| npm | 10.9.8 |
| Angular CLI | 22.2.0 |
| dotnet-ef (global tool) | 10.0.12 |
| Azure CLI / Bicep CLI | 2.90.0 / 0.47.16 |
| Container base images | mcr.microsoft.com/dotnet/aspnet:10.0, sdk:10.0, node:22-alpine |

## Backend packages

| Package | Version |
| --- | --- |
| Microsoft.EntityFrameworkCore (+ Sqlite, SqlServer, Design) | 10.0.12 |
| Microsoft.AspNetCore.OpenApi | 10.0.12 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 |
| Microsoft.Extensions.ApiDescription.Server | 10.0.12 |
| Microsoft.Extensions.Hosting / Options / Logging.Abstractions | 10.0.12 |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | 10.0.12 |
| OpenTelemetry.Extensions.Hosting, Exporter.OpenTelemetryProtocol | 1.19.1 |
| OpenTelemetry.Instrumentation.AspNetCore, Instrumentation.Http | 1.19.0 |
| Azure.Identity | 1.21.0 |
| Azure.ResourceManager / .Resources / .Resources.Deployments | 1.14.0 / 1.12.0 / 1.0.0 |
| Azure.ResourceManager.Compute / .Network | 1.17.0 / 1.17.0 |
| Azure.Security.KeyVault.Secrets | 4.11.1 |
| Microsoft.Data.SqlClient | 6.1.6 (matches EF Core's dependency) |
| Azure.Monitor.OpenTelemetry.AspNetCore / .Exporter | 1.6.0 / 1.9.0 |
| Testcontainers.MsSql | 4.15.0 |
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.4 |
| Microsoft.NET.Test.Sdk / coverlet.collector | 17.14.1 / 6.0.4 |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 |

## Frontend packages

| Package | Version |
| --- | --- |
| @angular/core, common, router, forms, compiler | 22.2.1 |
| @angular/material, @angular/cdk | 22.2.1 |
| rxjs | 7.8.2 |
| typescript | 6.0.3 |
| @microsoft/signalr | 10.0.11 |
| vitest / jsdom | 5.0.3 / 30.1.1 |
| @playwright/test | 1.63.0 |
| @azure/msal-browser | 5.23.0 (loaded lazily, only in Entra mode) |
| openapi-typescript | 7.13.0 (peer TypeScript range overridden to the project's TypeScript 6 in `package.json` `overrides`) |
| eslint / angular-eslint / typescript-eslint | 10.11.0 / 22.5.0 / 8.69.0 |
| prettier | 3.9.9 |
