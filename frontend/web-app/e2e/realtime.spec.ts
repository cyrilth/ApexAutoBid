import { test, expect, baseURL } from "./fixtures/test";
import { createAuctionViaUi } from "./fixtures/auction-builder";
import { bidAmountPattern, submitBid } from "./fixtures/bidding";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 7 Task 15.14 -- Real-time: a bid placed by another user appears in the bid history
 * without a refresh, against the LIVE stack. Three separate browser CONTEXTS on the SAME
 * throwaway E2E auction:
 *   - bob (seller) creates the auction, then is done.
 *   - the "viewer" context sits on the auction detail page and is deliberately ANONYMOUS (no
 *     `storageState`) -- this also proves `NotificationHub`'s "BidPlaced" broadcast
 *     (`hooks/useLiveBids.ts`) reaches even an unauthenticated connection, not merely the
 *     bidder's own tab (`components/NotificationProvider.tsx`'s remarks: the hub connects
 *     anonymously when signed out, and "BidPlaced" is a platform-wide broadcast with no
 *     per-connection targeting).
 *   - tom (signed in) places a bid from his own separate context/tab.
 */
test.describe("real-time bid updates", () => {
  test("15.14 -- a bid placed by another signed-in user appears live, without navigation", async ({
    browser,
  }) => {
    const bobContext = await browser.newContext({ storageState: storageStatePath("bob") });
    const bobPage = await bobContext.newPage();
    const tag = `E2E RealTime ${Date.now()}`;
    const auctionId = await createAuctionViaUi(bobPage, {
      make: tag,
      model: "LiveUpdate",
      color: "Silver",
      year: 2024,
      mileage: 25,
      reservePrice: 0,
    });
    await bobContext.close();

    const viewerContext = await browser.newContext();
    const viewerPage = await viewerContext.newPage();
    await viewerPage.goto(`/auctions/${auctionId}`);
    await expect(viewerPage.getByText("No bids yet -- be the first to bid.")).toBeVisible();
    // Anonymous visitors don't get a bid form at all (BidPanel's `!isSignedIn` branch) --
    // confirms this context really is unauthenticated, not merely un-navigated.
    await expect(viewerPage.getByText("Sign in to place a bid on this auction.")).toBeVisible();

    const tomContext = await browser.newContext({ storageState: storageStatePath("tom") });
    const tomPage = await tomContext.newPage();
    await tomPage.goto(`/auctions/${auctionId}`);
    await submitBid(tomPage, 777);
    await expect(tomPage.getByRole("status").filter({ hasText: "Bid accepted" })).toBeVisible();
    await tomContext.close();

    // Back on the viewer's page -- still exactly where it was, no navigation/reload since its
    // very first `goto` above. SignalR's "BidPlaced" broadcast should land within a few
    // seconds; a generous bounded timeout absorbs real network jitter without the test hanging
    // forever if it genuinely never arrives.
    expect(viewerPage.url()).toBe(`${baseURL}/auctions/${auctionId}`);
    const visible = viewerPage.locator(":visible");
    const bidRows = viewerPage.locator("ul li").and(visible).filter({ hasText: bidAmountPattern(777) });
    await expect(bidRows).toHaveCount(1, { timeout: 15_000 });
    await expect(bidRows.first()).toContainText("tom");
    // The empty-state copy is gone now that a real bid arrived live.
    await expect(viewerPage.getByText("No bids yet -- be the first to bid.")).toHaveCount(0);

    await viewerContext.close();
  });
});
