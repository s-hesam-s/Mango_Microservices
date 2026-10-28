# Read-only fixture preview

![Existing Razor views with sample data](screenshots/fixture-preview.png)

This screenshot was captured from the local preview host, which renders the application's existing Razor home view and shared layout with clearly labeled sample data. It is not a screenshot of a deployed service or a completed payment/booking transaction.

## Visual tour

<picture><source type="image/webp" srcset="screenshots/ui-tour.webp" /><img src="screenshots/ui-tour.gif" width="100%" alt="Captured UI tour with sample data" /></picture>

Sample catalog → Samosa details → catalog → Paneer Tikka details → return. The 9.8-second looping animation is a sequence of captured browser states from the existing Razor views, not continuous video or evidence of a live transaction. Animated WebP preserves photo detail; [GIF](screenshots/ui-tour.gif) is provided as a fallback. The first frame labels the fixture boundary. [Open the static screenshot](screenshots/fixture-preview.png) for a motion-free alternative.

Capture notes: browser viewport 1280 × 720; encoded image 1265 × 712. The Resort modal capture was normalized to the same frame size. No application pixels or sample outcomes were fabricated. The original views, styling and sample model values are retained. To recreate the sequence, start the preview below, follow the shown UI states, and capture the browser viewport without browser chrome. Wait for images and CSS transitions to finish before each capture.

From the repository root, with a .NET 10 SDK:

```powershell
dotnet run --project tools/Preview/Preview.csproj --no-launch-profile
```

Open `http://127.0.0.1:8812`. Stop the process with Ctrl+C.

The separate entry point clears configuration sources, binds to loopback, registers only the preview controller, and supplies models directly. The original application's Program.cs, service adapters, database initialization and cloud consumers are not executed. POST requests return 405. Only the fixture catalog and `/Preview/ProductDetails?productId=1` (IDs 1–3) are available; Back to List returns to the catalog. Unknown product IDs return 404. Other application navigation is unavailable. The fixture detail view is rendered without calling the original authenticated product controller or product service. `/preview-info` returns the preview boundary as JSON. Data-protection keys are ephemeral. Existing layout CDN links remain, so styling is not guaranteed to be fully offline.

This host demonstrates the actual views. Use the isolated regression suite for authorization/business behavior and the README's original startup procedure for a separately configured development deployment.

For a separate capture process while port 8812 is occupied, pass `-- --capture` after the `dotnet run` arguments to bind the same isolated preview to `http://127.0.0.1:8822`. This flag selects only that fixed loopback port; it does not enable original application routes or services.
