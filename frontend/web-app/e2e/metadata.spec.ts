import { test, expect } from "./fixtures/test";
import { fetchAuctionDetail, findAuctionByStatus } from "./fixtures/api";

/**
 * Phase 7 Task 15.18 -- Link preview metadata (og:title, og:description, og:image,
 * twitter:card), against the live stack. `app/auctions/[id]/page.tsx`'s `generateMetadata`
 * (Task 5.6) builds these from the same `getAuctionById` data the page itself renders --
 * fetched here dynamically via `./fixtures/api.ts` so the expectations track whichever auction
 * is picked, never a hardcoded id.
 */
test.describe("auction detail metadata (live data)", () => {
  test("renders Open Graph and Twitter Card meta tags for a live auction", async ({ page }) => {
    const live = await findAuctionByStatus("Live");
    const detail = await fetchAuctionDetail(live.id);

    await page.goto(`/auctions/${live.id}`);

    const expectedTitle = `${detail.year} ${detail.make} ${detail.model}`;

    // <title> itself (Metadata.title, not OG-specific, but the same generateMetadata call).
    await expect(page).toHaveTitle(expectedTitle);

    const ogTitle = page.locator('meta[property="og:title"]');
    await expect(ogTitle).toHaveAttribute("content", expectedTitle);

    const ogDescription = page.locator('meta[property="og:description"]');
    await expect(ogDescription).toHaveCount(1);
    const ogDescriptionContent = await ogDescription.getAttribute("content");
    expect(ogDescriptionContent).toBeTruthy();
    // summarizeAuction (lib/auction-copy.ts) always leads with the auction's live/sold/reserve
    // status -- confirms this isn't just present but genuinely auction-specific copy.
    expect(ogDescriptionContent).toMatch(/live|sold|reserve/i);

    const ogImage = page.locator('meta[property="og:image"]');
    await expect(ogImage).toHaveCount(1);
    const ogImageContent = await ogImage.getAttribute("content");
    expect(ogImageContent).toBeTruthy();
    // Primary image (images[0], sortOrder 0) -- thumbnail preferred, full image as fallback
    // (generateMetadata's own comment/logic).
    const primaryImage = detail.images[0];
    const expectedImageUrl = primaryImage?.thumbnailUrl ?? primaryImage?.url;
    expect(ogImageContent).toBe(expectedImageUrl);

    const ogUrl = page.locator('meta[property="og:url"]');
    await expect(ogUrl).toHaveAttribute("content", new RegExp(`/auctions/${live.id}$`));

    const twitterCard = page.locator('meta[name="twitter:card"]');
    await expect(twitterCard).toHaveAttribute("content", "summary_large_image");

    const twitterTitle = page.locator('meta[name="twitter:title"]');
    await expect(twitterTitle).toHaveAttribute("content", expectedTitle);

    const twitterDescription = page.locator('meta[name="twitter:description"]');
    await expect(twitterDescription).toHaveCount(1);

    const twitterImage = page.locator('meta[name="twitter:image"]');
    await expect(twitterImage).toHaveAttribute("content", expectedImageUrl ?? "");
  });

  test("renders correct metadata for a finished (sold) auction too", async ({ page }) => {
    const finished = await findAuctionByStatus("Finished");
    const detail = await fetchAuctionDetail(finished.id);

    await page.goto(`/auctions/${finished.id}`);

    const expectedTitle = `${detail.year} ${detail.make} ${detail.model}`;
    await expect(page.locator('meta[property="og:title"]')).toHaveAttribute("content", expectedTitle);

    const ogDescriptionContent = await page.locator('meta[property="og:description"]').getAttribute("content");
    expect(ogDescriptionContent).toBeTruthy();
    // A Finished (sold) auction's summary should read as sold, not live -- proves the metadata
    // is genuinely status-aware rather than a static template.
    expect(ogDescriptionContent).toMatch(/sold/i);
  });
});
