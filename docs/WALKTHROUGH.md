# Technical walkthrough: a cart becomes a priced order

[Architecture](ARCHITECTURE.md) · [Read-only HTTP examples](examples/read-only.http) · [Setup](../README.md#requirements-and-configuration)

Two review paths are provided below. The test path can run without application hosts. The manual HTTP path requires an already approved, isolated development environment. Commands and response fragments in this document are instructions/examples, not captured live results.

## 1. Reproduce the isolated evidence

From the repository root:

```powershell
dotnet restore tests/Authorization.Tests/Authorization.Tests.csproj
dotnet test tests/Authorization.Tests/Authorization.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CartOrderBusinessTests|FullyQualifiedName~CheckoutFailureTests"
```

These existing tests use unique EF in-memory stores, mocked HTTP/dependency responses and strict mocked Stripe clients. They do not invoke Program, startup migrations, SQL Server, Stripe or Service Bus.

| Review goal | Existing test and controlled outcome |
|---|---|
| A client cannot set its own price | [OrderIgnoresForgedPricesTotalsDiscountsAndRecordKeys](../tests/Authorization.Tests/CartOrderBusinessTests.cs): catalog unit 19.99 × 2, trusted discount 5 → order total 34.98. |
| Coupon boundary | CouponIsEligibleAtExactMinimumInCartAndOrder: unit 20 × 2 meets minimum 40; discount 5 → total 35. |
| Invalid quantities and states | CartRejectsQuantityOverflow and InvalidTransitionsDoNotWriteOrRefund reject the request without the corresponding write/refund. |
| Payment retry | ConfirmingSamePaymentTwiceApprovesAndPublishesOnce approves once and publishes one reward for sequential requests. |
| Dependency failure | OrderDependencyFailuresDoNotPersistAnOrder and CheckoutFailureTests reject unavailable pricing or render a checkout error without an invalid redirect. |

The fixture's 19.99 price is deliberate test data; it is not the checked-in Product API seed price. Run the full suite and [configuration check](../scripts/Test-PublicConfiguration.ps1) using the commands in [Verification](../README.md#verification) for the wider regression scope.

## 2. Prepare a manual development scenario

Before starting hosts, review each startup migration and the exact development database targets. Follow [Build and run](../README.md#build-and-run); no host-start or migration command is executed by this documentation change.

Required for this scenario: Auth, Product, Cart and Order APIs; Coupon API if a coupon is used. Configure JWT settings consistently, host database connections and publisher Service Bus settings. Controllers resolve the publisher dependency even when a chosen endpoint does not publish. Public registration itself publishes a registration message; an existing locally provisioned CUSTOMER account avoids that registration step here.

Order API's `ServiceUrls:CouponAPI` uses the checked-in sample address; override `ServiceUrls__CouponAPI` when your local ports differ. The direct-host examples use Auth 7002, Product 7000, Cart 7003 and Order 7004 with trusted development HTTPS certificates. Gateway and MVC are optional for this direct API review. Do not use certificate-validation bypass switches.

For a deterministic arithmetic example, use an unchanged Product seed `productId=1`, Samosa, unit price 15, a fresh customer's empty cart and no coupon. [Product seed](../Mango.Services.ProductAPI/Data/AppDbContext.cs). If your catalog/cart has changed, use the values actually returned by your local APIs instead of expecting the same numeric total.

## 3. Login and read the catalog

Manual request, using your own development account:

```http
POST https://localhost:7002/api/auth/login
Content-Type: application/json

{"userName":"<local-customer-email>","password":"<local-password>"}
```

Expected success: HTTP 200, `isSuccess=true`, `result.token` and `result.user.id`. Invalid credentials return HTTP 400 with `isSuccess=false`. Do not commit tokens or account values. The `id` from the login response is the subject used in the cart URL below.

```http
GET https://localhost:7000/api/product
Accept: application/json
```

Expected: an envelope with `isSuccess=true` and a product array in `result`. When the seed is unchanged, the array includes Samosa at 15. Check the envelope, not just HTTP 200: this catalog controller also uses a failure envelope for caught exceptions.

## 4. Add one cart item, then read the server total

The following POST **writes the approved development cart**. It is shown for manual review and has not been executed here. A fresh cart prevents repeated additions from accumulating quantities.

```http
POST https://localhost:7003/api/cart/CartUpsert
Authorization: Bearer <local-customer-token>
Content-Type: application/json

{"cartHeader":{},"cartDetails":[{"productId":1,"count":2}]}
```

Expected: HTTP 200 and `isSuccess=true`. The authenticated subject becomes the owner; payload record IDs are not authoritative. CartUpsert accepts exactly one product per request and adds to an existing quantity. It is not an idempotent replacement operation.

```http
GET https://localhost:7003/api/cart/GetCart/<subject-from-login>
Authorization: Bearer <local-customer-token>
```

With the controlled state above, the response includes `result.cartHeader.cartTotal=30`, discount 0 and one line with count 2. Anonymous callers receive 401; another customer's subject receives 403. Missing cart resources return 404.

## 5. Create an order and stop before payment

This request **writes a Pending order** in the approved development Order database. No Stripe call is made by CreateOrder. Use it once per manual scenario; creating orders is not an idempotent operation.

```http
POST https://localhost:7004/api/order/CreateOrder
Authorization: Bearer <local-customer-token>
Content-Type: application/json

{
  "cartHeader":{"name":"Demo Customer","email":"demo@example.invalid","phone":"demo"},
  "cartDetails":[{"productId":1,"count":2}]
}
```

The response should be HTTP 200 with `isSuccess=true` and a generated `result.orderHeaderId`. For the unchanged seed/no-coupon scenario, the following is an **expected fragment**, not a complete or captured response:

```json
{
  "isSuccess": true,
  "result": {
    "orderTotal": 30,
    "discount": 0,
    "status": "Pending",
    "orderDetails": [
      {"productId": 1, "productName": "Samosa", "price": 15, "count": 2}
    ]
  }
}
```

Read it using `GET /api/order/GetOrder/<generated-id>` on port 7004 with the same bearer token. Ownership is checked against the stored order. Do not assume that the generated ID is 1 or 100.

This walkthrough ends here. Do not call CreateStripeSession, ValidateStripeSession or status/refund endpoints as part of this non-payment scenario. Their mocked evidence and operational limits are described in [Architecture](ARCHITECTURE.md) and [payment policy](../SECURITY-SETUP.md#cart-pricing-and-order-workflow).

## Evidence boundaries

Existing CI builds the solution, checks public configuration and runs regressions. These examples were checked against routes, DTOs, seeds and test assertions; the complete multi-host scenario has not been executed on a fresh SQL/Service Bus/Stripe environment. No throughput, production deployment or distributed-transaction guarantee is claimed.
