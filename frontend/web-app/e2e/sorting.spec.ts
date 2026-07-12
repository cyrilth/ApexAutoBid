import { test, expect } from "./fixtures/test";
import { fetchSearch } from "./fixtures/api";

/**
 * Phase 7 Task 15.5 -- Sorting by make/new/endingSoon, against the live stack.
 * `components/AuctionToolbar.tsx`'s "Sort by" select maps 1:1 onto `GET api/search`'s
 * `orderBy` param (`lib/auction-search-params.ts`'s `ORDER_BY_VALUES`). Rather than assert a
 * specific hardcoded order (fragile against seed-data changes, and meaningless for "new" when
 * every seed row shares the same `createdAt`), each case fetches the SAME `orderBy` value
 * straight from the live Search Service and asserts the rendered card order is byte-for-byte
 * identical to what the API itself returned -- the actual order change is proven by comparing
 * two DIFFERENT `orderBy` values against each other.
 */

async function assertCardOrderMatchesApi(
  page: import("@playwright/test").Page,
  orderBy: "make" | "new" | "endingSoon",
) {
  const expected = await fetchSearch({ orderBy });
  expect(expected.results.length).toBeGreaterThan(0);

  await page.goto("/");
  await page.getByLabel("Sort by").selectOption(orderBy);
  // `buildAuctionHref` (lib/auction-search-params.ts) omits `orderBy` entirely once it's back
  // to the default value ("endingSoon") to keep URLs tidy -- so selecting the default doesn't
  // add the param back; any other value does.
  if (orderBy === "endingSoon") {
    await expect(page).not.toHaveURL(/orderBy=/);
  } else {
    await expect(page).toHaveURL(new RegExp(`orderBy=${orderBy}`));
  }

  const visible = page.locator(":visible");
  const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);
  await expect(cardLinks).toHaveCount(expected.results.length);

  for (const [index, item] of expected.results.entries()) {
    await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${item.id}`);
  }

  return expected.results.map((item) => item.id);
}

test.describe("sorting (live data)", () => {
  test("orderBy=make sorts cards in the same order the live API returns for make", async ({ page }) => {
    const ids = await assertCardOrderMatchesApi(page, "make");

    // Positive control: the API's own make-sorted order is genuinely non-trivial (more than
    // one distinct make in the seed set), so matching it is a meaningful assertion, not a
    // vacuous single-item comparison.
    const expected = await fetchSearch({ orderBy: "make" });
    const distinctMakes = new Set(expected.results.map((item) => item.make));
    expect(distinctMakes.size).toBeGreaterThan(1);
    expect(ids).toHaveLength(expected.results.length);
  });

  test("orderBy=new sorts cards in the same order the live API returns for new", async ({ page }) => {
    await assertCardOrderMatchesApi(page, "new");
  });

  test("orderBy=endingSoon (default) sorts cards in the same order the live API returns for endingSoon", async ({
    page,
  }) => {
    await assertCardOrderMatchesApi(page, "endingSoon");
  });

  test("switching sort actually changes the rendered order (make vs. endingSoon differ)", async ({
    page,
  }) => {
    const byMake = await fetchSearch({ orderBy: "make" });
    const byEndingSoon = await fetchSearch({ orderBy: "endingSoon" });

    const makeOrderIds = byMake.results.map((item) => item.id);
    const endingSoonOrderIds = byEndingSoon.results.map((item) => item.id);
    // The two orderings are genuinely different in the current seed data -- if a future seed
    // change happened to make them coincide, this positive control would need updating rather
    // than silently passing on a no-op sort.
    expect(makeOrderIds).not.toEqual(endingSoonOrderIds);

    await page.goto("/");
    const visible = page.locator(":visible");
    const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);

    // Default listing (no explicit orderBy) already sorts by endingSoon.
    await expect(cardLinks).toHaveCount(endingSoonOrderIds.length);
    for (const [index, id] of endingSoonOrderIds.entries()) {
      await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${id}`);
    }

    await page.getByLabel("Sort by").selectOption("make");
    await expect(page).toHaveURL(/orderBy=make/);

    for (const [index, id] of makeOrderIds.entries()) {
      await expect(cardLinks.nth(index)).toHaveAttribute("href", `/auctions/${id}`);
    }
  });
});
