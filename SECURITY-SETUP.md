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

Run `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`. Tests use mocked Identity managers, isolated EF in-memory stores, controller HTTP hosts and strict mocked Stripe clients. They never start the real application, SQL initialization, payment provider or messaging service.

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

## Cart pricing and order workflow

Order creation obtains product names and unit prices from Product API and coupon terms from Coupon API. It ignores client-supplied totals, discounts, product snapshots and record identifiers. Order API settings now include the same sample Coupon API address as ShoppingCart API. Override `ServiceUrls__CouponAPI` for your local deployment as needed; the new named client forwards the authenticated bearer token using the existing backend handler. No external service is invoked by the tests.

Unit prices and discounts are rounded to two decimal places with MidpointRounding.AwayFromZero using decimal arithmetic, then stored in the existing double columns. No schema or migration change is required. A coupon is eligible when subtotal is **at least** MinAmount; its discount is capped at subtotal. Invalid coupons reject order creation. A missing/invalid coupon on cart display does not create a discount; unavailable product/coupon HTTP services fail pricing instead of silently producing a successful zero total. Invalid upstream prices or negative coupon terms also fail pricing.

CartUpsert accepts exactly one product with a positive quantity per request, matching the MVC add-to-cart flow; multi-item payloads and integer overflow are rejected. CreateOrder requires a nonempty set of distinct product IDs with positive quantities. Payment sessions use persisted order lines and the persisted discount, not a second client-supplied price list. A fixed-amount Stripe coupon represents the stored discount. Stripe amounts use integer cents and USD. Zero-total orders cannot start this payment flow; free-order fulfillment is not implemented.

Only Pending orders can start checkout. An existing open/unpaid matching session is reused; paid, expired or mismatched sessions are rejected instead of creating another payment. New session, fixed-amount discount and refund requests use stable per-order Stripe idempotency keys. Provider idempotency retention and live refund behavior are not validated by these tests.

| Current status | Allowed operational change |
|---|---|
| Approved | ReadyForPickup or Cancelled |
| ReadyForPickup | Completed or Cancelled |
| Pending, Completed, Cancelled, Refunded | No new operational transition |

Repeating the same operational status is a no-op. Cancellation uses the stored payment intent; failed/exceptional refunds do not change order status. An accepted pending refund marks the order Cancelled; its eventual settlement is not tracked here. Unsupported target strings return 400, disallowed transitions/payment mismatches return 409, and pricing/payment dependency failures return 503 with failure envelopes.

Payment confirmation checks the stored session ID, order reference, paid status, USD total and successful payment intent amount before approving a Pending order. Repeated sequential confirmation of Approved/ReadyForPickup/Completed returns the existing state without another Stripe read or reward message. Cancelled/Refunded orders cannot be reopened by confirmation. MVC Checkout validates service responses before reading results or redirecting, and failed confirmation displays an error instead of the success message.

### Payment and messaging limitations

Approval is saved before reward publication. If publication fails, confirmation returns 503 while the order may already be Approved; a subsequent confirmation does not automatically republish. Reward reconciliation requires operator review. There is no transactional outbox or exactly-once guarantee. Concurrent confirmations/status requests are not serialized and the in-memory suite does not establish SQL concurrency or distributed transaction behavior. A process/database failure after a successful Stripe operation still requires reconciliation; Stripe keys help retries within the provider's retention period but do not provide a cross-system transaction. Passing tests do not establish live Stripe/refund interoperability or production readiness.

The Auth, Order and ShoppingCart applications also publish messages through Mango.MessageBus. Each host reads ServiceBusConnectionString from its own configuration (environment variable: ServiceBusConnectionString). No connection string is embedded in the shared library.
