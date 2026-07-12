import { test, expect } from "./fixtures/test";
import { createAuctionViaUi } from "./fixtures/auction-builder";
import { bidAmountPattern, submitBid } from "./fixtures/bidding";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 7 Task 15.13 -- Place bid, against the LIVE stack. Each test opens its OWN throwaway
 * E2E auction (bob as seller) so parallel workers never fight over the same auction's bid state
 * (Docs/Tasks.md Phase 7 Task 15's "avoid two tests mutating the same auction" guidance) --
 * `browser.newContext({ storageState: ... })` gives each test as many independently-signed-in
 * pages as it needs (here: bob to create, alice to bid), rather than the whole file sharing one
 * `test.use({ storageState })`.
 */
test.describe("place bid", () => {
  test("15.13 -- a valid bid succeeds, shows a success toast, and prepends to the history", async ({
    browser,
  }) => {
    const bobContext = await browser.newContext({ storageState: storageStatePath("bob") });
    const bobPage = await bobContext.newPage();
    const tag = `E2E Bid ${Date.now()}`;
    const auctionId = await createAuctionViaUi(bobPage, {
      make: tag,
      model: "Bidding",
      color: "Blue",
      year: 2022,
      mileage: 100,
      reservePrice: 0,
    });
    await bobContext.close();

    const aliceContext = await browser.newContext({ storageState: storageStatePath("alice") });
    const alicePage = await aliceContext.newPage();
    await alicePage.goto(`/auctions/${auctionId}`);

    // No bids yet on this brand-new auction -- BidPanel's minimum-bid guidance starts at $1.
    await expect(alicePage.getByText("Minimum bid: $1")).toBeVisible();
    await expect(alicePage.getByText("No bids yet -- be the first to bid.")).toBeVisible();

    await submitBid(alicePage, 250);

    // BidPanel.onSubmit's toastSuccess for the Accepted branch (lib/toast.ts renders every
    // toast with role="status"/aria-live="polite" -- see lib/toast.ts's remarks).
    await expect(
      alicePage.getByRole("status").filter({ hasText: "Bid accepted" })
    ).toBeVisible();

    // The bid is prepended to the history via BidStoreProvider's store -- no reload.
    const visible = alicePage.locator(":visible");
    const bidRows = alicePage.locator("ul li").and(visible).filter({ hasText: bidAmountPattern(250) });
    await expect(bidRows).toHaveCount(1);
    await expect(bidRows.first()).toContainText("alice");

    // The panel's own minimum-bid guidance and default next amount update live too.
    await expect(alicePage.getByText("Minimum bid: $251")).toBeVisible();

    await aliceContext.close();
  });

  test("the auction's own seller can't place a bid on it -- the form is hidden, not just disabled", async ({
    browser,
  }) => {
    const bobContext = await browser.newContext({ storageState: storageStatePath("bob") });
    const bobPage = await bobContext.newPage();
    const tag = `E2E SellerNoBid ${Date.now()}`;
    const auctionId = await createAuctionViaUi(bobPage, {
      make: tag,
      model: "OwnAuction",
      color: "White",
      year: 2021,
      mileage: 50,
      reservePrice: 0,
    });

    await bobPage.goto(`/auctions/${auctionId}`);
    await expect(
      bobPage.getByText("You're the seller of this auction", { exact: false })
    ).toBeVisible();
    await expect(bobPage.getByLabel("Your bid")).toHaveCount(0);
    await expect(bobPage.getByRole("button", { name: "Place bid" })).toHaveCount(0);

    await bobContext.close();
  });
});
