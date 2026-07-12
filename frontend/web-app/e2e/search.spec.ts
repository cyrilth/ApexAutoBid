import { test, expect } from "./fixtures/test";
import { fetchSearch } from "./fixtures/api";
import { SEED_MAKES } from "./fixtures/test-data";

/**
 * Phase 7 Task 15.2 -- Search filters auctions by search term, against the live stack.
 * `components/AuctionToolbar.tsx`'s "Search" field maps 1:1 onto `GET api/search`'s
 * `searchTerm` param (`lib/auction-search-params.ts`), so this spec drives the real form and
 * compares against what the live Search Service itself returns for the same term.
 */
test.describe("search (live data)", () => {
  test("searching a seeded make narrows the results to matching auctions only", async ({ page }) => {
    const expected = await fetchSearch({ searchTerm: SEED_MAKES.matching });
    // Sanity check on the fixture/seed data -- if this ever comes back empty the rest of the
    // test would pass vacuously.
    expect(expected.results.length).toBeGreaterThan(0);

    await page.goto("/");
    await page.getByLabel("Search").fill(SEED_MAKES.matching);
    await page.getByRole("button", { name: "Apply" }).click();

    await expect(page).toHaveURL(new RegExp(`searchTerm=${SEED_MAKES.matching}`));

    const visible = page.locator(":visible");
    const cardLinks = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible);
    await expect(cardLinks).toHaveCount(expected.results.length);

    for (const item of expected.results) {
      await expect(
        page
          .locator(`a[href="/auctions/${item.id}"]`)
          .and(visible)
          .getByRole("heading", { level: 3, name: `${item.year} ${item.make} ${item.model}` }),
      ).toBeVisible();
    }

    // A make that IS on a seeded auction but wasn't searched for must not appear.
    const otherMakeAuctions = (await fetchSearch({})).results.filter(
      (item) => !item.make.toLowerCase().includes(SEED_MAKES.matching.toLowerCase()),
    );
    expect(otherMakeAuctions.length).toBeGreaterThan(0);
    for (const item of otherMakeAuctions) {
      await expect(page.locator(`a[href="/auctions/${item.id}"]`).and(visible)).toHaveCount(0);
    }
  });

  test("searching a make with no seeded auctions shows the empty state", async ({ page }) => {
    const expected = await fetchSearch({ searchTerm: SEED_MAKES.nonMatching });
    expect(expected.results).toHaveLength(0);

    await page.goto("/");
    await page.getByLabel("Search").fill(SEED_MAKES.nonMatching);
    await page.getByRole("button", { name: "Apply" }).click();

    await expect(page).toHaveURL(new RegExp(`searchTerm=${SEED_MAKES.nonMatching}`));

    const visible = page.locator(":visible");
    await expect(page.getByText("No auctions match your filters.").and(visible)).toBeVisible();
    await expect(
      page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible),
    ).toHaveCount(0);
  });

  test("Clear resets the search term and restores the full listing", async ({ page }) => {
    const all = await fetchSearch({});
    expect(all.results.length).toBeGreaterThan(0);

    await page.goto("/");
    await page.getByLabel("Search").fill(SEED_MAKES.matching);
    await page.getByRole("button", { name: "Apply" }).click();
    await expect(page).toHaveURL(new RegExp(`searchTerm=${SEED_MAKES.matching}`));

    await page.getByRole("button", { name: "Clear" }).click();

    await expect(page).toHaveURL(/\/$/);
    await expect(page.getByLabel("Search")).toHaveValue("");

    const visible = page.locator(":visible");
    await expect(
      page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])').and(visible),
    ).toHaveCount(all.results.length);
  });
});
