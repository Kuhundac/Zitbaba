# Zitbaba

Multi-vendor university marketplace for Copperbelt University (currency: ZMW).
Day 1 skeleton: accounts, roles, seller/catalogue schema, themed layout, health check, CI.

**Stack:** .NET 10 (LTS), ASP.NET Core MVC, EF Core + SQL Server, ASP.NET Core Identity.

## Prerequisites

- .NET 10 SDK (see `global.json`)
- SQL Server. On Windows, LocalDB (installed with Visual Studio) works out of the box.
  On Linux/macOS use Docker (see below) and change the connection string.

## Local setup

```bash
dotnet tool restore                       # installs dotnet-ef (pinned in .config/dotnet-tools.json)
dotnet restore

# Seed admin (never commit these). Stored outside the repo in user-secrets:
cd Zitbaba.Web
dotnet user-secrets set "Seed:AdminEmail" "admin@zitbaba.local"
dotnet user-secrets set "Seed:AdminPassword" "ChangeMe123"
cd ..
```

The Development connection string is in `Zitbaba.Web/appsettings.Development.json` (LocalDB).
SQL Server in Docker alternative:

```bash
docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Your_strong_Passw0rd' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
dotnet user-secrets --project Zitbaba.Web set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=Zitbaba_Dev;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True"
```

## Migrations

The initial migration is created once and committed:

```bash
dotnet ef migrations add InitialCreate --project Zitbaba.Web --output-dir Data/Migrations
```

Apply to a database:

```bash
dotnet ef database update --project Zitbaba.Web
```

In **Development** the app also applies migrations and runs the seeder on startup.
In **Production** the app never migrates itself. It runs the seeder only if no migrations are pending.

## Run and test

```bash
dotnet run --project Zitbaba.Web --launch-profile https
dotnet test
```

- Site: https://localhost:7180
- Health: https://localhost:7180/health (returns `Healthy` when the database is reachable)

Password-reset links are written to the console log (no email provider yet).

## Roles

Buyer, Seller, Admin, Support, DeliveryAgent. Self-registration grants **Buyer only**.

## Deployment (skeleton)

1. Set environment variables on the host:
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `ConnectionStrings__DefaultConnection=<production SQL Server connection string>`
   - `Seed__AdminEmail`, `Seed__AdminPassword` (first deploy only; remove the password afterwards)
   - Behind a reverse proxy / Azure App Service: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
2. Apply migrations as a controlled step (not on app start):
   `dotnet ef database update --project Zitbaba.Web --connection "<production connection string>"`
   or generate a script: `dotnet ef migrations script --idempotent --project Zitbaba.Web -o migrate.sql`
3. Publish: `dotnet publish Zitbaba.Web -c Release -o ./publish`
4. Restart the app so the seeder creates roles, categories and the admin.
5. Check `/health`, then log in as the admin.

CI (`.github/workflows/ci.yml`) restores, builds and tests on every push and pull request.
