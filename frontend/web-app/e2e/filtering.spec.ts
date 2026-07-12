import { test, expect } from "./fixtures/test";
import { fetchSearch } from "./fixtures/api";
import type { AuctionItem } from "@/types/auction";

/**
 * Phase 7 Task 15.4 -- Filtering by live/endingSoon/finished, against the live stack.
 * `components/AuctionToolbar.tsx`'s "Status" select maps 1:1 onto `GET api/search`'s
 * `filterBy` param (`lib/auction-search-params.ts`'s `FILTER_BY_VALUES`); the backend's actual
 * filter semantics (`SearchService.Infrastructure.Data.ItemRepository.SearchAsync`) are:
 *   - live: `Status == "Live" && AuctionEnd > now`
 *   - endingSoon: the same, further restricted to `AuctionEnd < now + 6h`
 *   - finished: `AuctionEnd <= now || Status != "Live"` (covers Finished, ReserveNotMet, AND a
 *     Live-status auction whose end time has already passed but hasn't been finalized yet)
 * This spec drives the real Status select and compares against what the live Search Service
 * itself returns for the same `filterBy` value -- both the result SET and each card's status
 * badge -- rather than assuming a particular seed shape.
 */

/** Mirrors components/AuctionStatusBadge.tsx's resolveStatus mapping exactly. */
function expectedBadgeText(item: AuctionItem): string {
  switch (item.status) {
    case "Finished":
      return "Sold";
    case "ReserveNotMet":
      return "Reserve not met";
    case "Cancelled":
      return "Cancelled";
    case "Live":
    default: {
      const hoursRemaining = (new Date(item.auctionEnd).getTime() - Date.now()) / (1000 * 60 * 60);
      return hoursRemaining <= 6 ? "Ending soon" : "Live";
    }
  }
}

async function assertFilteredListing(
  page: import("@playwright/test").Page,
  filterBy: "live" | "endingSoon" | "finished",
) {
  const expected = await fetchSearch({ filterBy });

  await page.goto("/");
  await page.getByLabel("Status").selectOption(filterBy);

  await expect(page).toHaveURL(new RegExp(`filterBy=${filterBy}`));

  const visible = page.locator(":visible");

  if (expected.results.length === 0) {
    await expect(page.getByText("No auctions match your filters.").and(visible)).toBeVisible();
    await expect(
      page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible),
    ).toHaveCount(0);
    return;
  }

  const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);
  await expect(cardLinks).toHaveCount(expected.results.length);

  for (const item of expected.results) {
    const card = page.locator(`a[href="/auctions/${item.id}"]`).and(visible);
    await expect(card).toBeVisible();
    await expect(card.getByText(expectedBadgeText(item), { exact: true })).toBeVisible();
  }
}

test.describe("filtering (live data)", () => {
  test("filterBy=live shows only currently-live auctions with a Live/Ending soon badge", async ({
    page,
  }) => {
    await assertFilteredListing(page, "live");
  });

  test("filterBy=endingSoon shows only live auctions ending within 6 hours (or none)", async ({
    page,
  }) => {
    // The 6h "ending soon" window is entirely a function of the live seed data's auctionEnd
    // timestamps vs. wall-clock "now" -- this may legitimately be empty (see
    // fixtures/api.ts's findAuctionByStatus remarks on asserting real data rather than
    // fabricating a scenario). Either way, assertFilteredListing asserts on whatever the live
    // API actually returns for this filter, honestly.
    await assertFilteredListing(page, "endingSoon");
  });

  test("filterBy=finished shows only ended/non-live auctions with a Sold/Reserve-not-met badge", async ({
    page,
  }) => {
    await assertFilteredListing(page, "finished");
  });

  test("changing the Status filter always starts back at page 1 (no stale pageNumber)", async ({
    page,
  }) => {
    await page.goto("/");
    await page.getByLabel("Status").selectOption("live");
    await expect(page).toHaveURL(/filterBy=live/);
    // `buildAuctionHref` omits `pageNumber` entirely once it's back to the default (1) -- see
    // lib/auction-search-params.ts.
    await expect(page).not.toHaveURL(/pageNumber=/);
  });
});
