# PC Builder

[![CI](https://github.com/phudiennnn/PCSTORE/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/phudiennnn/PCSTORE/actions/workflows/ci.yml)

PC Builder is a React/Vite frontend with an ASP.NET Core API and PostgreSQL database.
The API and frontend are available locally through Docker Compose.

## Requirements

- Docker Desktop with Docker Compose
- Git

For running the projects directly on your machine, install the .NET 10 SDK,
Node.js 22, and PostgreSQL 16.

## Run with Docker Compose

1. Copy `.env.example` to `.env`.
2. Set `POSTGRES_PASSWORD` in `.env` to a password for your local database.
   Do not commit `.env` or use production credentials in a local setup.
3. From the repository root, build and start the services:

   ```powershell
   docker compose up --build -d
   ```

4. Check container and database health:

   ```powershell
   docker compose ps
   ```

   Wait until `db` reports `healthy` and the API and frontend report `running`.
5. Open the frontend at `http://localhost:3000`. The API is available at
   `http://localhost:5170`.

The database data is stored in the `pcstore-postgres-data` Docker volume and
survives container restarts. Backup files and ASP.NET Core Data Protection keys
are stored in separate named volumes. The API image includes PostgreSQL client
tools for backup and restore. These local volumes are not a production backup
or key-management strategy.

Stop the services without deleting stored data:

```powershell
docker compose down
```

To follow service logs:

```powershell
docker compose logs -f api frontend db
```

The API initializes and seeds the database on startup. This local stack uses
PostgreSQL to match the application's Npgsql provider; it does not use SQL Server.

## Run and test .NET projects locally

With the required PostgreSQL connection configured for the API:

```powershell
dotnet restore .\PCSTORE.slnx
dotnet build .\PCSTORE.slnx --configuration Release --no-restore
dotnet test .\PCSTORE.slnx --configuration Release
```

The xUnit project contains unit tests for the PC build compatibility rules.
These tests run without starting the API or connecting to a database.
The application also provides administrator-only audit log search/export,
inventory history, voucher management, and database backup download/restore.
Customers can submit support requests from the AI chat panel; staff can search
and update ticket status in the support workspace.

To run the React frontend directly:

```powershell
Set-Location .\PCStore.Web
npm ci
npm run dev
```

The frontend currently expects the API at `http://localhost:5170`.

## Continuous integration

GitHub Actions runs on pushes to branches and pull requests targeting `main`.
The workflow restores and builds the .NET solution, runs xUnit tests, uploads
the test result artifact, lints the frontend, and builds the API and frontend
Docker images. It does not publish images or deploy the application.

## Current validation notes

- The xUnit suite currently contains 10 tests: 5 compatibility characterization
  cases and 5 support-ticket status normalization cases.
- Local `dotnet list package --vulnerable --include-transitive` reports no
  vulnerable NuGet packages after updating `Microsoft.OpenApi`.
- Frontend production builds may report that a JavaScript bundle exceeds Vite's
  500 kB advisory threshold; this is a warning, not a failed build.
