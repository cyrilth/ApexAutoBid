import { test, expect } from "./fixtures/test";
import { DEFAULT_QUERY_PARAMS, fetchSearch } from "./fixtures/api";

/**
 * Phase 7 Task 15.1 -- Home page loads auction listings, against the LIVE stack. Unlike
 * `smoke.spec.ts` (backend-agnostic chrome only), this spec asserts on real seeded data: every
 * auction the live Search Service returns for the listing page's default query renders as its
 * own card, in the same order, right down to its `/auctions/{id}` link.
 */
test.describe("home page listing (live data)", () => {
  test("renders one card per seeded auction, matching the live Search Service response", async ({
    page,
  }) => {
    const expected = await fetchSearch(DEFAULT_QUERY_PARAMS);
    // Sanity check on the fixture itself -- an empty seed would make every assertion below
    // vacuously true and silently mask a broken listing page.
    expect(expected.results.length).toBeGreaterThan(0);

    await page.goto("/");

    await expect(page.getByRole("heading", { level: 1, name: "Auctions" })).toBeVisible();

    // `:visible` guard: Next's streamed RSC payload duplicates each card's ENTIRE anchor
    // markup verbatim into a hidden data island elsewhere in the DOM (not merely duplicate
    // text, as `smoke.spec.ts`'s comment describes for its simpler case) -- without this
    // filter, `a[href="..."]` resolves two real `<a>` elements per auction and trips
    // Playwright's strict mode.
    const visible = page.locator(":visible");

    // Excludes the "Create auction" nav link, the only other `/auctions/*` href on this page
    // (see smoke.spec.ts's identical locator/rationale).
    const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);
    await expect(cardLinks).toHaveCount(expected.results.length);

    for (const [index, item] of expected.results.entries()) {
      // Scoped to this specific auction's own (visible) card.
      const card = page.locator(`a[href="/auctions/${item.id}"]`).and(visible);
      await expect(card).toBeVisible();
      await expect(card.getByRole("heading", { level: 3 })).toHaveText(
        `${item.year} ${item.make} ${item.model}`,
      );

      // Same order as the API returned (default sort = endingSoon, ascending auctionEnd).
      await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${item.id}`);
    }
  });

  test("each card shows its status badge and bid/price summary", async ({ page }) => {
    const expected = await fetchSearch(DEFAULT_QUERY_PARAMS);
    expect(expected.results.length).toBeGreaterThan(0);

    await page.goto("/");

    const visible = page.locator(":visible");

    for (const item of expected.results) {
      // See the previous test's comment on why `:visible` is required here.
      const card = page.locator(`a[href="/auctions/${item.id}"]`).and(visible);

      const isSold = item.status === "Finished";
      const expectedAmount = isSold ? item.soldAmount : item.currentHighBid;
      const expectedPriceText = expectedAmount != null ? `$${expectedAmount.toLocaleString("en-US")}` : "No bids yet";
      await expect(card.getByText(expectedPriceText, { exact: true })).toBeVisible();

      // Status badge text mirrors components/AuctionStatusBadge.tsx's resolveStatus mapping.
      const expectedBadgeText =
        item.status === "Finished"
          ? "Sold"
          : item.status === "ReserveNotMet"
            ? "Reserve not met"
            : item.status === "Cancelled"
              ? "Cancelled"
              : // Live: "Ending soon" inside 6h of auctionEnd, "Live" otherwise -- same threshold
                // components/AuctionStatusBadge.tsx's resolveStatus applies.
                (new Date(item.auctionEnd).getTime() - Date.now()) / (1000 * 60 * 60) <= 6
                ? "Ending soon"
                : "Live";
      await expect(card.getByText(expectedBadgeText, { exact: true })).toBeVisible();
    }
  });
});
