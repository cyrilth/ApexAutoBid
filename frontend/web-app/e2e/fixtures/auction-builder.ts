import type { Page } from "@playwright/test";
import { throttleMutation } from "./mutation-throttle";

/**
 * Fields `components/AuctionForm.tsx`'s create mode always needs. Every Batch C spec that needs
 * a fresh, disposable auction to act on (edit/delete/bid/real-time/toast specs) uses
 * `createAuctionViaUi` below rather than hand-rolling the same form-fill sequence
 * `register.spec.ts` (Batch B) already established -- one seller-signed-in `page`, already
 * navigated nowhere in particular, ends up on that new auction's detail page.
 */
export interface AuctionSeed {
  make: string;
  model: string;
  color: string;
  year: number;
  mileage: number;
  /** 0 = no reserve (`AuctionForm`'s own default/HelperText). */
  reservePrice: number;
  /**
   * Externally hosted fallback image (`AuctionImageManager`'s "Or add an image by URL"
   * fallback) -- defaults to the same 127.0.0.1 workaround `register.spec.ts` documents:
   * `AuctionAppService.ValidateGalleryAsync` treats any URL under the platform's own
   * `Images:PublicBaseUrl`/`Images:Bucket` (`localhost:9000/auction-images/...`) as
   * platform-hosted and requires a bare-GUID object key, which the seeded auctions'
   * human-readable filenames (e.g. "ford-gt.jpg") fail -- `127.0.0.1:9000` resolves to the
   * exact same MinIO instance but isn't string-matched as platform-hosted, so it's correctly
   * treated as an external URL and exempted. See this run's final report for the suggested fix.
   */
  imageUrl?: string;
  /** Days from now the auction ends -- default 7, comfortably "Live" for every Batch C spec. */
  daysUntilEnd?: number;
}

/**
 * Drives `/auctions/create`'s real form (Task 6) to completion for the given `page` (already
 * signed in as the intended seller -- see `./storage-state.ts`) and returns the new auction's
 * id, parsed off the post-submit redirect URL (`AuctionForm.onSubmit`'s `router.push
 * ("/auctions/{id}")`). Mirrors `register.spec.ts`'s 15.16 spec's own inline create-auction
 * steps exactly, factored out here so Batch C's several specs that each need their OWN
 * throwaway auction (test isolation -- Docs/Tasks.md Phase 7 Task 15's "avoid two tests
 * mutating the same auction" guidance, which also keeps the suite correct if `workers`
 * is ever raised above the current rate-limit-imposed 1) don't each duplicate it.
 */
export async function createAuctionViaUi(page: Page, seed: AuctionSeed): Promise<string> {
  await page.goto("/auctions/create");

  await page.getByLabel("Make").fill(seed.make);
  await page.getByLabel("Model").fill(seed.model);
  await page.getByLabel("Color").fill(seed.color);
  await page.getByLabel("Year").fill(String(seed.year));
  await page.getByLabel("Mileage").fill(String(seed.mileage));
  await page.getByLabel("Reserve price").fill(String(seed.reservePrice));

  const auctionEnd = new Date(Date.now() + (seed.daysUntilEnd ?? 7) * 24 * 60 * 60 * 1000);
  const formattedEnd = auctionEnd.toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
    hour12: true,
  });
  // See CLAUDE.md's foundation notes -- the datepicker's <label> association doesn't reach the
  // a11y tree, so this is located by placeholder text rather than `getByLabel`.
  const dateInput = page.getByPlaceholder("Select a date and time");
  await dateInput.click();
  await dateInput.fill(formattedEnd);
  await page.keyboard.press("Enter");
  await page.keyboard.press("Escape");

  await page
    .getByLabel("Or add an image by URL")
    .fill(seed.imageUrl ?? "http://127.0.0.1:9000/auction-images/ford-gt.jpg");
  await page.getByRole("button", { name: "Add" }).click();

  // GatewayService's "strict" rate-limit policy applies to this POST -- see
  // ./mutation-throttle.ts's remarks.
  await throttleMutation();
  await page.getByRole("button", { name: "Create auction" }).click();

  await page.waitForURL(/\/auctions\/[0-9a-f-]{36}$/);
  const match = /\/auctions\/([0-9a-f-]{36})$/.exec(page.url());
  if (!match) {
    throw new Error(`createAuctionViaUi: couldn't parse an auction id out of post-submit URL "${page.url()}"`);
  }
  return match[1];
}
