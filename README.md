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

## Day 2 notes

**What exists:** seller applications, admin approve/reject/suspend/reinstate, admin categories, seller dashboard and products (one image each), account menu by seller status, audit log.

**Local database:** the dev database runs in Docker, not LocalDB.
- After a PC restart: `docker start zitbaba-sql`
- Connection string, `Seed:AdminEmail` and `Seed:AdminPassword` live in `dotnet user-secrets` (run secrets commands from the solution folder).
- Seeded admin: `admin@zitbaba.local`.

**Uploads:** product images are saved to `Zitbaba.Web/wwwroot/uploads/products` (git-ignored). This is development storage only. Production needs persistent storage (for example cloud blob storage) because app folders can be wiped on deploy. Images are limited to .jpg, .jpeg, .png, .webp at 5 MB or less, checked by file signature. EXIF metadata is not stripped yet.

**Listing moderation:** `Marketplace:RequireListingModeration` in `appsettings.json` (default `false`). If `true`, new products start as Pending review, but there is no admin approval page yet.

**Running tests:** stop the site first (`Ctrl+C`), otherwise the build fails with a file-lock error. Then `dotnet test Zitbaba.sln`.

**Audit log:** every approval, category change, product change, image change and denied access writes a row to `AuditLogs`.

## Day 3 notes (public catalogue)

Public pages: home (`/`), `/Home/Categories`, `/Home/Search`, `/Product/{id}`, `/Store/{id}`.
`/Wishlist` needs a login.

- **One visibility rule** lives in `CatalogueService`: a product is public only if its status is
  Active and its seller is Approved. Out-of-stock products stay listed and are marked.
  Draft, Pending moderation, Inactive, Removed and Blocked products never appear, and neither do
  products of Pending, Rejected or Suspended sellers. Public pages never show phone numbers,
  room numbers or application notes.
- **Search** matches name or description (every word must match), with category (including
  subcategories), price, condition and in-stock filters, three sort orders, 12 per page.
- **Wishlist** uses the `WishlistItems` table (migration `AddDay3Changes`), unique per user and
  product. Saved products that stop being public are hidden from the list and come back if
  they are reactivated.
- **No view or save counters yet.** `Product.ViewCount` and `Product.SaveCount` are not
  incremented, because changing the product row would change `RowVersion` and make sellers
  see "changed somewhere else" while editing. A separate counter table would fix this.
- After `git pull`, Development applies new migrations when the site starts. Production never
  does; run migrations deliberately.
- Tests: `dotnet test Zitbaba.sln` (78 passing at the end of Day 3). Stop the site first or the
  build hits a file lock.
