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

## Day 4 notes (cart and checkout)

**What exists:** cart, checkout (cash on pickup), order confirmation, "My orders" (`/Orders`, `/Orders/{id}`).
Migration `AddDay4Changes` adds `Carts`, `CartItems`, `Orders`, `OrderItems` and `OrderStatusHistory`.

**Cart rules** (`CartService`):
- One seller per cart. Adding from a second seller is refused with a message and a
  "Clear cart and add this item" button. Nothing is replaced silently.
- Only public products (Active, seller Approved) and only within stock. Sellers cannot add their own products.
- The cart does NOT reserve stock. The cart page shows current prices and marks lines that became
  unavailable or short; checkout stays disabled until they are fixed.

**Checkout rules** (`CheckoutService`):
- One database transaction. Each line runs a single SQL statement that subtracts stock only if enough is
  left (`QuantityAvailable >= n`), so two buyers can never both take the last unit. Any line that fails
  rolls everything back and names the item that ran short.
- Prices are read after the stock update, then compared with the total the buyer saw. If they differ the
  order is refused and rolled back.
- Order items store a snapshot of the product name and unit price.
- A one-time key on the checkout form plus a unique index on buyer + key means a double click or the Back
  button returns the same order instead of creating another.
- Order number format: `ZB-yyyyMMdd-XXXXXX`. Status starts at Pending, payment at Pending (cash on pickup).
  The first `OrderStatusHistory` row and an `OrderPlaced` audit row are written in the same transaction.
- Taking stock changes the product's `RowVersion`, so a seller saving an edit at that exact moment sees the
  friendly "changed somewhere else" message. This is expected.
- Order status changes (accept, reject, ready, complete, cancel and stock restore) arrive on Day 5.

**Tests:** `dotnet test Zitbaba.sln` (stop the site first). The normal suite uses an in-memory database.
Six extra tests run against real SQL Server (atomic stock under concurrent buyers, rollback, double submit,
unique index, check constraints). They only run when `ZITBABA_TEST_SQL` is set; otherwise they are reported
as skipped, which is what CI shows for now. To run them (Docker SQL Server must be running):

    $cs = (dotnet user-secrets --project Zitbaba.Web list | Select-String "ConnectionStrings:DefaultConnection").ToString()
    $pw = [regex]::Match($cs, 'Password=([^;]+)').Groups[1].Value
    $env:ZITBABA_TEST_SQL = "Server=localhost,14333;Database=Zitbaba_Test;User Id=sa;Password=$pw;TrustServerCertificate=True;MultipleActiveResultSets=true"
    dotnet test Zitbaba.sln

The first run creates the `Zitbaba_Test` database and applies the migrations. Never point this variable at
`Zitbaba_Dev` or a real database. Running these tests in CI is planned for Day 7.

**Service registration test:** every controller, view component and service that needs an app service must
have it registered in `AddZitbabaServices`; `ServiceRegistrationTests` fails with the missing names if not.

## Day 5 notes

**Order lifecycle:** Pending, Accepted, Ready for pickup, Completed, with Rejected and Cancelled by buyer as the other endings. The allowed changes live in one place, `Domain/Rules/OrderRules.cs`.

| Action | Who | From | To |
|---|---|---|---|
| Accept | Seller | Pending | Accepted |
| Reject (reason required) | Seller | Pending | Rejected |
| Mark ready | Seller | Accepted | Ready for pickup |
| Complete (cash received) | Seller | Ready for pickup | Completed, payment Paid |
| Cancel | Buyer | Pending or Accepted | Cancelled by buyer |

**Stock:** rejecting or cancelling puts the stock back in the same transaction as the status change. The status change is a single conditional update, so if a buyer cancels while the seller accepts, exactly one of them wins.

**Privacy:** a seller sees the buyer's name and phone only on that order's page, and only while the order is open. The phone is hidden once the order is closed.

**Ownership:** sellers can only act on their own store's orders and buyers on their own. Anyone else gets a 404 and an `AccessDenied` audit row.

**Suspended sellers:** cannot accept new orders (the whole seller area also requires an approved seller).

**Audit actions:** OrderAccepted, OrderRejected, OrderReadyForPickup, OrderCompleted, OrderCancelled, AccessDenied.
