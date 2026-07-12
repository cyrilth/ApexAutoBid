import { test, expect } from "./fixtures/test";

/**
 * Phase 7 Task 14 scaffold-verification spec. Deliberately backend-agnostic:
 * it only asserts on chrome that Server Components render unconditionally
 * (headings, the toolbar's filter/sort controls, the `/session` auth
 * harness) -- never on actual auction data, since this spec must also pass
 * with the Gateway/backend services stopped (the results region then shows
 * `AuctionResults`' graceful error message instead of crashing -- see
 * components/AuctionResults.tsx). Task 15's specs bring the backend up and
 * assert on real data/flows on top of this same page structure.
 */

test.describe("smoke", () => {
  test("home page loads with the auction listing chrome", async ({ page }) => {
    await page.goto("/");

    await expect(page.getByRole("heading", { level: 1, name: "Auctions" })).toBeVisible();

    // Toolbar controls (Task 4.2/4.3 -- components/AuctionToolbar.tsx), each
    // tied to its Flowbite <Label> so `getByLabel` resolves them by their
    // visible text rather than a brittle CSS selector.
    await expect(page.getByLabel("Search")).toBeVisible();
    await expect(page.getByLabel("Seller")).toBeVisible();
    await expect(page.getByLabel("Winner")).toBeVisible();
    await expect(page.getByLabel("Status")).toBeVisible();
    await expect(page.getByLabel("Sort by")).toBeVisible();
    await expect(page.getByRole("button", { name: "Apply" })).toBeVisible();

    // Results region: the page must not crash regardless of backend
    // availability. With the backend down (this spec's normal environment)
    // AuctionResults renders its graceful error message; with the backend
    // up it renders the auction card grid (or the "no auctions" empty
    // state on a freshly-seeded, empty dataset) instead. Any one of the
    // three proves the Suspense boundary resolved cleanly.
    //
    // `.and(page.locator(":visible"))` on the text locators guards against
    // Next's streamed RSC payload -- embedded in an invisible <script> tag
    // and containing the same copy verbatim -- otherwise satisfying
    // `getByText` too and tripping strict-mode ("resolved to 2 elements").
    const visible = page.locator(":visible");
    const errorMessage = page
      .getByText("We couldn't load auctions right now", { exact: false })
      .and(visible);
    const emptyState = page.getByText("No auctions match your filters.").and(visible);
    // Auction cards link to `/auctions/{id}`; excludes the "Create auction"
    // nav link, the only other `/auctions/*` href on this page.
    const auctionCards = page.locator('a[href^="/auctions/"]:not([href="/auctions/create"])');

    await expect(errorMessage.or(emptyState).or(auctionCards.first())).toBeVisible();
  });

  test("session page loads", async ({ page }) => {
    await page.goto("/session");

    await expect(page.getByRole("heading", { level: 1, name: "ApexAutoBid" })).toBeVisible();
    // Unauthenticated by default (no stored session in a fresh browser
    // context) -- Task 15.6 signs in via `signInViaIdentityServer` and
    // re-asserts this page in its signed-in state instead.
    await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
  });
});
