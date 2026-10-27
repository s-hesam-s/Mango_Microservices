# Local security configuration

This repository contains educational examples. Credential values are intentionally empty in tracked application settings. Supply your own development values through environment variables before starting the relevant application. ASP.NET Core maps a double underscore in an environment variable to a configuration colon.

| Application settings | Configuration key | Environment variable |
|---|---|---|
| `Mango.GatewaySolution/appsettings.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.AuthAPI/appsettings.json` | `ApiSettings:JwtOptions:Secret` | `ApiSettings__JwtOptions__Secret` |
| `Mango.Services.AuthAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.AuthAPI/appsettings.Production.json` | `ApiSettings:JwtOptions:Secret` | `ApiSettings__JwtOptions__Secret` |
| `Mango.Services.CouponAPI/appsettings.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.CouponAPI/appsettings.json` | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `Mango.Services.CouponAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.CouponAPI/appsettings.Production.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.CouponAPI/appsettings.Production.json` | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `Mango.Services.EmailAPI/appsettings.json` | `ServiceBusConnectionString` | `ServiceBusConnectionString` |
| `Mango.Services.EmailAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.EmailAPI/appsettings.Production.json` | `ServiceBusConnectionString` | `ServiceBusConnectionString` |
| `Mango.Services.OrderAPI/appsettings.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.OrderAPI/appsettings.json` | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `Mango.Services.OrderAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.OrderAPI/appsettings.Production.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.OrderAPI/appsettings.Production.json` | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `Mango.Services.ProductAPI/appsettings.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.ProductAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.ProductAPI/appsettings.Production.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.RewardAPI/appsettings.json` | `ServiceBusConnectionString` | `ServiceBusConnectionString` |
| `Mango.Services.RewardAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.RewardAPI/appsettings.Production.json` | `ServiceBusConnectionString` | `ServiceBusConnectionString` |
| `Mango.Services.ShoppingCartAPI/appsettings.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |
| `Mango.Services.ShoppingCartAPI/appsettings.Production.json` | `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `Mango.Services.ShoppingCartAPI/appsettings.Production.json` | `ApiSettings:Secret` | `ApiSettings__Secret` |

For a JWT signing secret, use a locally generated random value of at least 32 bytes. Services that issue or validate the same JWT must use the same local secret, issuer and audience. Do not reuse a published training value. Do not commit local credentials or print them in logs.

PowerShell example for the current shell only:

```powershell
$env:ApiSettings__Secret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

For applications using `ApiSettings:JwtOptions:Secret`, set `ApiSettings__JwtOptions__Secret` instead. Supply payment, storage, messaging and Redis settings only for the features you intend to run, using your own development resources. Empty settings are not fallback credentials.

Some examples apply Entity Framework migrations during application startup. Review startup behavior and select an isolated development database before running them. No migration or database update is performed by this configuration change.

Public registration must create only a customer account. Administrative accounts and role changes require a trusted administrative process; the registration page is not an admin bootstrap mechanism. Do not enable public admin registration to set up a demonstration.

Historical commits may still contain previous training values. If a value was ever connected to a real resource, its owner should revoke or rotate it; clearing the current files does not invalidate an old value or rewrite Git history.

## Regression tests

Run `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`. The tests replace Identity managers with mocks and never start the application or connect to a database, payment provider or messaging service.

Run `pwsh -File scripts/Test-PublicConfiguration.ps1` to check public configuration without displaying values.

## Cart and Order access policy

Both API controllers require bearer authentication. The subject is read from the mapped NameIdentifier claim or raw `sub`; an authenticated identity without a usable subject is rejected.

| Operation | Access |
|---|---|
| Read/update/remove/email a cart | The authenticated cart owner; ADMIN does not impersonate another cart owner |
| List/read orders | Owner reads their own orders; ADMIN can read all orders |
| Create an order | The authenticated owner; an omitted payload UserId is bound to the subject, and a different UserId is rejected |
| Create/validate a Stripe session | The stored order owner, checked before any Stripe call; ADMIN has no payment impersonation override |
| Update order status | ADMIN only; accepted operational targets are ReadyForPickup, Completed and Cancelled |

Customer-supplied cart/header/detail keys do not determine ownership. Cart creation uses server-generated keys, and upsert discards nested cart-header navigation data. Existing cart resources are checked against the stored owner before removal, coupon changes or email publication. Order reads and payment guards use the persisted order owner rather than a payload owner.

Successful responses retain the ResponseDto JSON envelope and existing route names. HTTP errors are 401 for unauthenticated callers, 403 for wrong ownership/role or missing subject, 404 for missing resources and 400 for invalid payloads or unsupported operational statuses. The MVC Cart and Order controllers also require sign-in, and their status actions require ADMIN, matching the existing order-detail UI.

The authorization regression suite hosts controllers in TestServer with separate EF Core in-memory stores. It checks anonymous callers, missing subjects, mapped/raw subject claims, owners, other customers, administrators, forged record keys and nested navigation data. Database state and message publication are asserted, and a strict mocked Stripe client prevents real payment requests if an authorization regression occurs. The real application Program, SQL migration/seed routines, Stripe and Service Bus are not started by these tests. The additional in-memory provider is test-only and matches the existing EF Core 9.0.4 dependency.

This change enforces caller identity and access. Existing pricing and payment workflow semantics remain outside these regressions; live Stripe/refund interoperability is not established by a passing authorization suite.

The Auth, Order and ShoppingCart applications also publish messages through Mango.MessageBus. Each host reads ServiceBusConnectionString from its own configuration (environment variable: ServiceBusConnectionString). No connection string is embedded in the shared library.
