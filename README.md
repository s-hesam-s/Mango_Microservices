# Mango Microservices

An educational .NET microservices application for an e-commerce workflow, combining authentication, products, coupons, carts, orders and asynchronous notifications.

## Implementation highlights

- Separately hosted ASP.NET Core services and an MVC client.
- Identity/JWT accounts, server-controlled CUSTOMER registration and administrator-only role management.
- SQL Server persistence, Ocelot gateway routing and a shared Azure Service Bus publisher.
- Shopping-cart, checkout and Stripe integration code with email/reward consumers.

## Service map

HTTPS addresses below come from the checked-in `https` launch profiles.

| Project | Responsibility | Local HTTPS port |
|---|---|---:|
| [Mango.Services.AuthAPI](Mango.Services.AuthAPI) | Accounts and tokens | 7002 |
| [Mango.Services.ProductAPI](Mango.Services.ProductAPI) | Product catalog | 7000 |
| [Mango.Services.CouponAPI](Mango.Services.CouponAPI) | Coupons | 7001 |
| [Mango.Services.ShoppingCartAPI](Mango.Services.ShoppingCartAPI) | Cart operations | 7003 |
| [Mango.Services.OrderAPI](Mango.Services.OrderAPI) | Orders and checkout | 7004 |
| [Mango.Services.EmailAPI](Mango.Services.EmailAPI) | Notification consumer | 7146 |
| [Mango.Services.RewardAPI](Mango.Services.RewardAPI) | Reward consumer | 7109 |
| [Mango.GatewaySolution](Mango.GatewaySolution) | Ocelot gateway | 7777 |
| [Mango.Web](Mango.Web) | MVC storefront/client | 7047 |
| [Mango.MessageBus](Mango.MessageBus) | Shared publisher library; not a separate host | — |

## Requirements and configuration

Use an SDK capable of targeting .NET 8, isolated SQL Server development databases and your own Azure Service Bus development namespace for messaging workflows. Stripe-dependent payment operations require separate development credentials. Package restore requires NuGet access.

Read [SECURITY-SETUP.md](SECURITY-SETUP.md) before configuring credentials. Tracked secret values are intentionally empty. Use environment variables for your own development resources; do not restore old values from Git history.

Configure each host separately:

- `ConnectionStrings__DefaultConnection` selects that host's isolated development database.
- `ApiSettings__JwtOptions__Secret` configures the issuer in AuthAPI; services that read `ApiSettings:Secret` use `ApiSettings__Secret`. Keep the local signing secret, issuer and audience consistent across issuer and validators.
- `ServiceBusConnectionString` configures publishers/consumers. The shared library no longer embeds a connection string.
- Review `TopicAndQueueNames` in the relevant settings and provision your own matching messaging entities before exercising these flows.
- Review `ServiceUrls` in clients/services and [ocelot.json](Mango.GatewaySolution/ocelot.json) in the gateway. Host URLs and ports must agree with the instances you run; production settings describe a separate environment.

**Most API hosts apply migrations on startup.** Review their Program.cs files and approve the database targets first. Email/reward consumers also need configured messaging resources. Running every host is not an offline, dependency-free demo.

## Build and run

```powershell
git clone https://github.com/s-hesam-s/Mango_Microservices.git
cd Mango_Microservices
```

```powershell
dotnet restore Mango.sln
dotnet build Mango.sln --no-restore
```

After configuration and database review, start Auth, Product and Coupon, then Cart and Order. Start Email/Reward when the corresponding messaging resources are ready. Each command runs in a separate terminal from the repository root:

```powershell
dotnet run --project Mango.Services.AuthAPI/Mango.Services.AuthAPI.csproj --launch-profile https
dotnet run --project Mango.Services.ProductAPI/Mango.Services.ProductAPI.csproj --launch-profile https
dotnet run --project Mango.Services.CouponAPI/Mango.Services.CouponAPI.csproj --launch-profile https
dotnet run --project Mango.Services.ShoppingCartAPI/Mango.Services.ShoppingCartAPI.csproj --launch-profile https
dotnet run --project Mango.Services.OrderAPI/Mango.Services.OrderAPI.csproj --launch-profile https
dotnet run --project Mango.Services.EmailAPI/Mango.Services.EmailAPI.csproj --launch-profile https
dotnet run --project Mango.Services.RewardAPI/Mango.Services.RewardAPI.csproj --launch-profile https
```

Start the gateway if you are testing gateway routes, then start the MVC client:

```powershell
dotnet run --project Mango.GatewaySolution/Mango.GatewaySolution.csproj --launch-profile https
dotnet run --project Mango.Web/Mango.Web.csproj --launch-profile https
```

Open `https://localhost:7047` for the storefront. Controllers with configured Swagger expose their API description under `/swagger`. Public registration creates CUSTOMER; assigning another role requires an already authenticated ADMIN bearer token.

## Verification

```powershell
pwsh -File scripts/Test-PublicConfiguration.ps1
dotnet test tests/Authorization.Tests/Authorization.Tests.csproj
```

The authorization tests use mocked dependencies, with in-memory HTTP checks where applicable. They do not start the application or apply database migrations. Build and test results do not establish that a full deployment or external integration is configured.

The regression suite includes in-memory HTTP authorization checks and the MVC registration flow. It does not validate a live payment or Service Bus deployment.

Cart operations use the authenticated subject. Order reads allow the owner or an administrator; checkout/payment operations remain owner-only, and operational status changes require ADMIN. See the [Cart and Order access policy](SECURITY-SETUP.md#cart-and-order-access-policy) for identity binding, HTTP errors and test boundaries.

## Project scope and reuse

This is an educational repository. The implementation notes describe the code in this repository; they do not claim a deployed commercial service or measured production results. Third-party libraries and assets retain their own terms. No project-wide license is currently declared.

## Continuous integration

GitHub Actions restores and builds `Mango.sln` in Release mode on Windows using the existing .NET 8 target. It also runs `scripts/Test-PublicConfiguration.ps1` without printing configured values. The existing authorization regressions run with mocked dependencies and in-memory HTTP hosts where applicable. The application hosts, database migrations/seeds, cloud integrations and payments are not started by CI.

The [CI workflow](.github/workflows/ci.yml) runs for pull requests and pushes to `master`, and can be started manually. It uses read-only repository permissions and does not deploy the project.
