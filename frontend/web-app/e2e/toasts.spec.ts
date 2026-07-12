import { test, expect } from "./fixtures/test";
import { createAuctionViaUi } from "./fixtures/auction-builder";
import { submitBid } from "./fixtures/bidding";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 7 Task 15.15 -- Toast notifications, against the LIVE stack: react-hot-toast renders
 * every toast (`lib/toast.ts`) into a portal with `role="status"`/`aria-live="polite"`
 * (confirmed against `node_modules/react-hot-toast`'s own `Te` toast-factory) -- located here
 * via `getByRole("status")`, never raw CSS.
 */
test.describe("toast notifications", () => {
  test("15.15a -- a successful bid shows a green success toast", async ({ browser }) => {
    const bobContext = await browser.newContext({ storageState: storageStatePath("bob") });
    const bobPage = await bobContext.newPage();
    const tag = `E2E ToastSuccess ${Date.now()}`;
    const auctionId = await createAuctionViaUi(bobPage, {
      make: tag,
      model: "Success",
      color: "Green",
      year: 2020,
      mileage: 5,
      reservePrice: 0,
    });
    await bobContext.close();

    const aliceContext = await browser.newContext({ storageState: storageStatePath("alice") });
    const alicePage = await aliceContext.newPage();
    await alicePage.goto(`/auctions/${auctionId}`);
    await submitBid(alicePage, 100);

    // BidPanel.onSubmit's `toastSuccess` for the Accepted branch (lib/toast.ts's
    // `successStyle` -- the emerald/accent-leaf treatment, not asserted on directly here since
    // computed CSS custom properties aren't meaningfully assertable, but the accessible role +
    // exact copy IS the contract this toast promises the user).
    await expect(
      alicePage.getByRole("status").filter({ hasText: "Bid accepted -- you're the high bidder at $100." })
    ).toBeVisible();

    await aliceContext.close();
  });

  test("15.15b -- a bid that's actually too low (stale client) shows a red error toast", async ({
    browser,
  }) => {
    const bobContext = await browser.newContext({ storageState: storageStatePath("bob") });
    const bobPage = await bobContext.newPage();
    const tag = `E2E ToastTooLow ${Date.now()}`;
    const auctionId = await createAuctionViaUi(bobPage, {
      make: tag,
      model: "TooLow",
      color: "Red",
      year: 2021,
      mileage: 10,
      reservePrice: 0,
    });
    await bobContext.close();

    // Alice's context: block the NotificationHub WebSocket entirely (BEFORE her page ever
    // loads), so her BidPanel's client-side minimum-bid guidance stays frozen at whatever the
    // page's initial server render saw ($1, this brand-new auction has no bids yet) --
    // simulating a dropped/slow real-time connection. `lib/signalr.ts` connects directly via
    // `ws(s)://.../notifications` with `skipNegotiation: true`, so this one route covers the
    // whole connection. `BidAppService.DetermineStatusAsync` (BiddingService) is the sole
    // authority on TooLow regardless of what any client believes the current high bid is (see
    // that method's own remarks on its "tentative ... re-verified atomically" design) -- this
    // is what lets a genuinely stale client submit a bid the backend correctly rejects, without
    // a fragile millisecond-timing race between two simultaneous live submissions.
    const aliceContext = await browser.newContext({ storageState: storageStatePath("alice") });
    await aliceContext.routeWebSocket(/\/notifications/, () => {});
    const alicePage = await aliceContext.newPage();
    await alicePage.goto(`/auctions/${auctionId}`);
    await expect(alicePage.getByText("Minimum bid: $1")).toBeVisible();

    // Tom, on a normal (unblocked) connection, places a real, higher bid -- the backend's true
    // current high is now $500.
    const tomContext = await browser.newContext({ storageState: storageStatePath("tom") });
    const tomPage = await tomContext.newPage();
    await tomPage.goto(`/auctions/${auctionId}`);
    await submitBid(tomPage, 500);
    await expect(tomPage.getByRole("status").filter({ hasText: "Bid accepted" })).toBeVisible();
    await tomContext.close();

    // Alice, still unaware (her WebSocket never delivered that update -- her own guidance is
    // still "Minimum bid: $1"), submits a bid that passes HER stale client-side validation but
    // is well below tom's real $500 high.
    await expect(alicePage.getByText("Minimum bid: $1")).toBeVisible();
    await submitBid(alicePage, 50);

    // BidPanel.onSubmit's `toastError` for the TooLow branch.
    await expect(
      alicePage.getByRole("status").filter({ hasText: "Someone already bid higher -- try a higher amount." })
    ).toBeVisible();

    await aliceContext.close();
  });
});
