import { test, expect, baseURL } from "./fixtures/test";
import { fetchSearch } from "./fixtures/api";

/**
 * Phase 7 Task 15.3 -- Pagination, against the live stack.
 *
 * MAINTENANCE NOTE (Task 15 Batch C): this spec originally hardcoded an assertion that
 * `pageCount === 1` -- true when it was written (the seed set alone, ~10 auctions, is under the
 * backend's default `pageSize` of 12 -- `SearchService.Domain.Models.ItemSearchDefaults
 * .DefaultPageSize`), but no longer true now that Batch A/B/C's own auth/CRUD specs have created
 * enough `E2E`-tagged auctions on the live dev database to push the total past one page. Rather
 * than re-hardcode a NEW fixed expectation (which would just as quickly go stale as more specs
 * create more auctions over time), every assertion below computes its expectation from a live
 * `GET api/search` call first (same pattern `listing.spec.ts`/`search.spec.ts` already use) and
 * branches on whatever `pageCount` actually comes back -- this spec passes whether the live
 * dataset spans one page or several, and the multi-page branch below now gets to do genuine
 * next/previous click-through navigation testing, which was previously impossible to exercise
 * honestly against this dataset (`components/AuctionResults.tsx` only renders
 * `<AuctionPagination>` at all when `result.pageCount > 1`).
 */
test.describe("pagination (live data)", () => {
  test("page 1 renders exactly the first page's worth of results, in order", async ({ page }) => {
    const expected = await fetchSearch({ pageNumber: "1" });
    expect(expected.results.length).toBeGreaterThan(0);

    await page.goto("/");

    const visible = page.locator(":visible");
    const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);
    await expect(cardLinks).toHaveCount(expected.results.length);

    for (const [index, item] of expected.results.entries()) {
      await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${item.id}`);
    }
  });

  test("pagination controls render if and only if there's more than one page", async ({ page }) => {
    const expected = await fetchSearch({ pageNumber: "1" });

    await page.goto("/");

    // Flowbite's `<Pagination>` (components/AuctionPagination.tsx) renders as a bare `<nav>`
    // (role "navigation") -- present exactly when `result.pageCount > 1`
    // (`AuctionResults`'s own guard).
    const nav = page.getByRole("navigation");
    if (expected.pageCount > 1) {
      await expect(nav).toHaveCount(1);
    } else {
      await expect(nav).toHaveCount(0);
    }
  });

  test("requesting a pageNumber beyond the actual page count shows the empty state, not a crash", async ({
    page,
  }) => {
    const expected = await fetchSearch({ pageNumber: "1" });
    const beyondLastPage = expected.pageCount + 1;

    await page.goto(`/?pageNumber=${beyondLastPage}`);

    // The page itself must still render its chrome (proves this isn't a hard crash/500) --
    // mirrors smoke.spec.ts's "must not crash regardless of backend availability" contract.
    await expect(page.getByRole("heading", { level: 1, name: "Auctions" })).toBeVisible();

    const visible = page.locator(":visible");
    await expect(page.getByText("No auctions match your filters.").and(visible)).toBeVisible();
    await expect(
      page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible),
    ).toHaveCount(0);
  });

  test("pageNumber=1 explicitly in the URL renders identically to the bare listing page", async ({
    page,
  }) => {
    const expected = await fetchSearch({ pageNumber: "1" });
    expect(expected.results.length).toBeGreaterThan(0);

    await page.goto("/?pageNumber=1");

    const visible = page.locator(":visible");
    await expect(
      page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible),
    ).toHaveCount(expected.results.length);
  });

  test("with a single page of results, every result renders on it with no pagination controls", async ({
    page,
  }) => {
    const expected = await fetchSearch({ pageNumber: "1" });
    test.skip(expected.pageCount > 1, "Live dataset currently spans more than one page.");

    await page.goto("/");

    const visible = page.locator(":visible");
    await expect(
      page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible),
    ).toHaveCount(expected.results.length);
    await expect(page.getByRole("navigation")).toHaveCount(0);
  });

  test("with multiple pages, Next/Previous actually navigate and swap in the next page's results", async ({
    page,
  }) => {
    const page1 = await fetchSearch({ pageNumber: "1" });
    test.skip(page1.pageCount <= 1, "Live dataset currently fits on a single page -- nothing to click through.");

    const page2 = await fetchSearch({ pageNumber: "2" });
    expect(page2.results.length).toBeGreaterThan(0);

    await page.goto("/");

    const visible = page.locator(":visible");
    const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);
    await expect(cardLinks).toHaveCount(page1.results.length);

    const nav = page.getByRole("navigation");
    await expect(nav).toBeVisible();
    const previousButton = nav.getByRole("button", { name: "Previous" });
    const nextButton = nav.getByRole("button", { name: "Next" });

    // Page 1: "Previous" is disabled (AuctionPagination/Flowbite Pagination's own
    // `disabled: currentPage === 1`), "Next" is not.
    await expect(previousButton).toBeDisabled();
    await expect(nextButton).toBeEnabled();

    // Real navigation, not a URL-fabricated `page.goto` -- proves the actual UI control works.
    await nextButton.click();
    await expect(page).toHaveURL(`${baseURL}/?pageNumber=2`);
    await expect(cardLinks).toHaveCount(page2.results.length);
    for (const [index, item] of page2.results.entries()) {
      await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${item.id}`);
    }

    // Page 2: "Previous" is enabled again now.
    await expect(previousButton).toBeEnabled();

    // And back to page 1 via "Previous" -- round-trips to exactly the original result set.
    // `buildAuctionHref` (lib/auction-search-params.ts) omits `pageNumber` entirely for page 1
    // (its "omit default values" rule), so this lands back on the bare "/", not "/?pageNumber=1".
    await previousButton.click();
    await expect(page).toHaveURL(`${baseURL}/`);
    await expect(cardLinks).toHaveCount(page1.results.length);
    for (const [index, item] of page1.results.entries()) {
      await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${item.id}`);
    }
  });
});
