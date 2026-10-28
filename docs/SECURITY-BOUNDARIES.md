# Catalog and browser security boundaries

This page supplements the existing order/cart authorization and pricing notes. The application remains an educational microservices sample.

## Implemented controls

- Product and coupon management pages require ADMIN, in addition to the existing API role checks. The public catalog remains available through Home/Product API read routes.
- MVC registers automatic antiforgery validation. Logout, cart removal and payment verification are POST operations using form tokens. The confirmation GET reads the stored order; it does not invoke payment validation or publish rewards.
- Browser bearer-token storage is HttpOnly, Secure, SameSite=Lax and scoped to `/`. Product descriptions use normal Razor encoding on catalog, detail and cart pages.
- Product API creation/update explicitly bind multipart form input. Product names and positive prices are validated by the API model pipeline. Creation assigns the product ID on the server.
- Posted ImageLocalPath/ImageUrl values do not replace stored storage metadata. Image uploads use generated filenames, matching PNG/JPEG/WebP extensions/signatures, a 2 MiB file limit and a 3 MiB multipart limit. API-host image URLs are retained for the separate MVC host.
- Deletion only targets a direct file in the managed ProductImages folder. Previous images are cleaned up after a successful save; a cleanup failure may leave an orphan file. Failed creation removes only its new image. Signature checks are not a decoder or malware scanner. Static responses include nosniff.
- Product failure responses do not expose exception messages.

## Evidence and remaining boundaries

Tests retain the existing cart/order/registration regressions and add in-process HTTP checks for product role authorization, multipart binding and model rejection, MVC antiforgery, cookie flags, image policies, stored-path preservation and read-only confirmation. TestServer does not open an external listening service. Product/order persistence in tests uses the existing EF InMemory fixture provider, not a SQL server or durable database.

The previous payment limitations remain: reward publication after stored approval has no durable outbox/reconciliation record, SQL concurrency/exactly-once processing is not proven, accepted pending refunds are not tracked to final settlement, and free-order fulfillment is not implemented. Webhook fulfillment and live Stripe/message-bus integration are not supplied by this browser/catalog change. Do not claim production payment guarantees from passing isolated tests.

No original host, real database migration, live payment, email or message broker was run. Runtime and dependency advisory remediation remain a separate change group.

Order-table contact fields use the DataTables text renderer; customer-provided strings are not rendered as trusted HTML.

Use HTTPS for local MVC sign-in: Secure token cookies are intentionally not sent over plain HTTP.
