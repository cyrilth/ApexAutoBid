import { test, expect } from "./fixtures/test";
import {
  fetchAllAuctions,
  fetchAuctionDetail,
  fetchBids,
  findAuctionByStatus,
  findAuctionWithBids,
} from "./fixtures/api";

/**
 * Phase 7 Task 15.12 -- Auction detail page displays specs, countdown, and bid history, against
 * the live stack. Picks a seeded LIVE auction WITH bid history dynamically via `./fixtures/api.ts`
 * (never a hardcoded id/Guid, per Docs/Tasks.md Phase 7 Task 15's brief) so this spec stays
 * correct even if the seed data is regenerated with different ids.
 */
test.describe("auction detail (live data)", () => {
  // The detail page mounts a live SignalR connection (components/NotificationProvider.tsx,
  // hooks/useLiveBids.ts) on every load, anonymous visitors included -- this doesn't assert
  // "zero console errors" (a flaky, over-broad assertion unrelated to this task's actual specs),
  // but per Docs/Tasks.md Phase 7 Task 15's brief ("report rather than paper over"), any browser
  // console error/pageerror during these tests is surfaced in the test output below instead of
  // being silently swallowed. Reassigned fresh in `beforeEach` -- safe as a plain describe-scoped
  // variable because Playwright always runs a given test's `beforeEach -> test -> afterEach`
  // sequentially, even when multiple tests from this file share a worker.
  let consoleErrors: string[] = [];

  test.beforeEach(async ({ page }) => {
    consoleErrors = [];
    page.on("console", (message) => {
      if (message.type() === "error") consoleErrors.push(message.text());
    });
    page.on("pageerror", (error) => consoleErrors.push(error.message));
  });

  test.afterEach(async ({}, testInfo) => {
    if (consoleErrors.length > 0) {
      // Deliberate: surfaces in the Playwright report/log rather than failing the test outright
      // (see this describe block's remarks above).
      console.warn(
        `[detail.spec.ts] "${testInfo.title}" -- ${consoleErrors.length} browser console error(s) during this test:\n` +
          consoleErrors.map((message) => `  - ${message}`).join("\n"),
      );
    }
  });

  test("shows the vehicle/auction spec grid for a live auction", async ({ page }) => {
    const { auction } = await findAuctionWithBids("Live");
    const detail = await fetchAuctionDetail(auction.id);

    await page.goto(`/auctions/${auction.id}`);

    await expect(
      page.getByRole("heading", { level: 1, name: `${detail.year} ${detail.make} ${detail.model}` }),
    ).toBeVisible();

    // components/DetailedSpecs.tsx renders a `<dl>` of label/value pairs -- assert each
    // `<dt>`/`<dd>` pair directly rather than loose page-wide text, so this doesn't
    // accidentally match the same value appearing elsewhere on the page (e.g. the bid panel's
    // own current-bid figure).
    const specs = page.locator("dl").first();
    await expect(specs.locator("dt", { hasText: "Seller" }).locator("~ dd")).toHaveText(detail.seller);
    await expect(specs.getByText("Make", { exact: true }).locator("~ dd")).toHaveText(detail.make);
    await expect(specs.getByText("Model", { exact: true }).locator("~ dd")).toHaveText(detail.model);
    await expect(specs.getByText("Year", { exact: true }).locator("~ dd")).toHaveText(String(detail.year));
    await expect(specs.getByText("Color", { exact: true }).locator("~ dd")).toHaveText(detail.color);
    await expect(specs.getByText("Mileage", { exact: true }).locator("~ dd")).toHaveText(
      `${detail.mileage.toLocaleString("en-US")} mi`,
    );
    const expectedReserve =
      detail.reservePrice > 0 ? `$${detail.reservePrice.toLocaleString("en-US")}` : "No reserve";
    await expect(specs.getByText("Reserve price", { exact: true }).locator("~ dd")).toHaveText(
      expectedReserve,
    );
  });

  test("shows a live countdown for a live auction", async ({ page }) => {
    const live = await findAuctionByStatus("Live");

    await page.goto(`/auctions/${live.id}`);

    await expect(page.getByText("Time remaining")).toBeVisible();
    // react-countdown's renderer (components/AuctionCountdown.tsx) formats as
    // `[Nd ]HHh MMm SSs` -- assert the digits render rather than a specific value (it ticks
    // every second).
    await expect(page.getByText(/^(\d+d\s)?\d{2}h\s\d{2}m\s\d{2}s$/)).toBeVisible();
  });

  test("shows the bid history for an auction with seeded/live bids, newest first", async ({ page }) => {
    const { auction, bids } = await findAuctionWithBids();

    await page.goto(`/auctions/${auction.id}`);

    await expect(page.getByRole("heading", { level: 2, name: "Bid history" })).toBeVisible();

    const sortedByNewest = [...bids].sort(
      (a, b) => new Date(b.bidTime).getTime() - new Date(a.bidTime).getTime(),
    );

    // components/BidHistory.tsx renders bids as a `<ul>` of `<li>` rows -- scope to that list so
    // this doesn't collide with the bid panel's own "current bid" figure or the RSC payload
    // duplicate (the `:visible` guard smoke.spec.ts documents).
    const visible = page.locator(":visible");
    const bidRows = page.locator("ul li").and(visible).filter({ hasText: /\$[\d,]+/ });
    await expect(bidRows).toHaveCount(sortedByNewest.length);

    for (const [index, bid] of sortedByNewest.entries()) {
      const row = bidRows.nth(index);
      await expect(row).toContainText(bid.bidder);
      await expect(row).toContainText(`$${bid.amount.toLocaleString("en-US")}`);
    }
  });

  test("an auction with no bids shows the empty bid-history state", async ({ page }) => {
    // Not every seeded/live auction has bids -- fetch the full set and find one that doesn't,
    // rather than assuming a specific auction is bid-free.
    const auctions = await fetchAllAuctions();

    let noBidsAuctionId: string | null = null;
    for (const candidate of auctions) {
      const bids = await fetchBids(candidate.id);
      if (bids.length === 0) {
        noBidsAuctionId = candidate.id;
        break;
      }
    }
    test.skip(noBidsAuctionId === null, "Every seeded auction currently has at least one bid.");

    await page.goto(`/auctions/${noBidsAuctionId}`);

    await expect(page.getByRole("heading", { level: 2, name: "Bid history" })).toBeVisible();
    await expect(page.getByText("No bids yet -- be the first to bid.")).toBeVisible();
  });
});
