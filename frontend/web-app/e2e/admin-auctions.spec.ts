import type { Page } from "@playwright/test";
import { test, expect } from "./fixtures/test";
import { fetchAuctionDetail } from "./fixtures/api";
import { throttleMutation } from "./fixtures/mutation-throttle";
import { storageStatePath } from "./fixtures/storage-state";
import { SEEDED_USERS } from "./fixtures/test-data";

/**
 * Phase 11 Task 10.3/10.4 -- Admin auction moderation against the LIVE stack: create an
 * auction assigned to another seller (Task 3.1/8.4's admin-only seller-assignment fields on
 * the existing create form), then end/cancel auctions from `/admin/auctions`.
 */
test.use({ storageState: storageStatePath("admin") });

interface AdminAuctionSeed {
  make: string;
  model: string;
  seller?: { username: string; email: string };
}

/**
 * Drives `/auctions/create` as the signed-in admin, optionally filling the admin-only "create
 * for another seller" fields (`components/AuctionForm.tsx`). Mirrors
 * `./fixtures/auction-builder.ts`'s `createAuctionViaUi` shape but kept local to this spec file
 * rather than extending that shared, non-admin-aware fixture (used by many other specs).
 */
async function createAuctionAsAdmin(page: Page, seed: AdminAuctionSeed): Promise<string> {
  await page.goto("/auctions/create");
  await expect(page.getByRole("heading", { level: 1, name: "Create auction" })).toBeVisible();

  await page.getByLabel("Make").fill(seed.make);
  await page.getByLabel("Model").fill(seed.model);
  await page.getByLabel("Color").fill("Gray");
  await page.getByLabel("Year").fill("2022");
  await page.getByLabel("Mileage").fill("100");
  await page.getByLabel("Reserve price").fill("0");

  if (seed.seller) {
    await page.getByLabel("Seller username").fill(seed.seller.username);
    await page.getByLabel("Seller email").fill(seed.seller.email);
  }

  const auctionEnd = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000);
  const formattedEnd = auctionEnd.toLocaleString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
    hour12: true,
  });
  const dateInput = page.getByPlaceholder("Select a date and time");
  await dateInput.click();
  await dateInput.fill(formattedEnd);
  await page.keyboard.press("Enter");
  await page.keyboard.press("Escape");

  await page.getByLabel("Or add an image by URL").fill("http://127.0.0.1:9000/auction-images/ford-gt.jpg");
  await page.getByRole("button", { name: "Add" }).click();

  // POST /api/auctions falls under the Gateway's strict mutating-route rate limit (unlike the
  // admin end/cancel actions used elsewhere in this spec, which are on the "general" policy --
  // see backend/GatewayService/appsettings.json's admin-route comments).
  await throttleMutation();
  await page.getByRole("button", { name: "Create auction" }).click();

  await page.waitForURL(/\/auctions\/[0-9a-f-]{36}$/);
  const match = /\/auctions\/([0-9a-f-]{36})$/.exec(page.url());
  if (!match) {
    throw new Error(`createAuctionAsAdmin: couldn't parse an auction id out of post-submit URL "${page.url()}"`);
  }
  return match[1];
}

/**
 * Finds a just-created auction's row on `/admin/auctions` -- the table reads `GET api/search`
 * (SearchService's own index), which is populated asynchronously off the `AuctionCreated` event
 * (eventually consistent, same caveat `delete.spec.ts`'s remarks document for the same
 * direction), so a freshly created auction may not appear on the very first render. Reloads
 * (re-running the page's Server Component fetch) until the row shows up.
 */
async function findAuctionRow(page: Page, tag: string) {
  const row = page.getByRole("row").filter({ hasText: tag });
  await expect(async () => {
    if (!(await row.isVisible().catch(() => false))) {
      await page.reload();
    }
    await expect(row).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 20_000, intervals: [1_000] });
  return row;
}

/** Polls `GET api/auctions/{id}` until `predicate` passes, failing loudly if it never does. */
async function waitForAuction(
  auctionId: string,
  predicate: (auction: Awaited<ReturnType<typeof fetchAuctionDetail>>) => boolean,
  timeoutMs = 15_000,
): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  let last: Awaited<ReturnType<typeof fetchAuctionDetail>> | undefined;

  while (Date.now() < deadline) {
    last = await fetchAuctionDetail(auctionId);
    if (predicate(last)) return;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error(`waitForAuction: condition never met for auction ${auctionId}; last seen: ${JSON.stringify(last)}`);
}

test.describe("admin auctions", () => {
  test("10.3 -- an admin can create an auction assigned to another seller", async ({ page }) => {
    const tag = `E2E Admin Assign ${Date.now()}`;
    const auctionId = await createAuctionAsAdmin(page, {
      make: tag,
      model: "ForAlice",
      seller: { username: SEEDED_USERS.alice.username, email: SEEDED_USERS.alice.email },
    });

    await expect(page.getByRole("heading", { level: 1, name: `2022 ${tag} ForAlice` })).toBeVisible();
    const specs = page.locator("dl").first();
    await expect(specs.getByText("Seller", { exact: true }).locator("~ dd")).toHaveText(
      SEEDED_USERS.alice.username,
    );

    const auction = await fetchAuctionDetail(auctionId);
    expect(auction.seller).toBe(SEEDED_USERS.alice.username);
  });

  test("10.4 -- an admin can end a live auction now", async ({ page }) => {
    const tag = `E2E Admin End ${Date.now()}`;
    const auctionId = await createAuctionAsAdmin(page, { make: tag, model: "EndNow" });

    await page.goto(`/admin/auctions?searchTerm=${encodeURIComponent(tag)}`);
    const row = await findAuctionRow(page, tag);
    await row.getByRole("button", { name: "End now" }).click();

    const modal = page.getByRole("dialog");
    await expect(modal.getByRole("heading", { level: 3, name: "End this auction now?" })).toBeVisible();
    await modal.getByRole("button", { name: "Yes, confirm" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Deterministic proof, straight from the Auction Service (not the eventually-consistent
    // search index the admin table itself reads): AuctionEnd is now in the past.
    await waitForAuction(auctionId, (auction) => new Date(auction.auctionEnd).getTime() <= Date.now());
  });

  test("10.4 -- an admin can cancel a live auction", async ({ page }) => {
    const tag = `E2E Admin Cancel ${Date.now()}`;
    const auctionId = await createAuctionAsAdmin(page, { make: tag, model: "CancelMe" });

    await page.goto(`/admin/auctions?searchTerm=${encodeURIComponent(tag)}`);
    const row = await findAuctionRow(page, tag);
    await row.getByRole("button", { name: "Cancel" }).click();

    const modal = page.getByRole("dialog");
    await expect(modal.getByRole("heading", { level: 3, name: "Cancel this auction?" })).toBeVisible();
    await modal.getByRole("button", { name: "Yes, confirm" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Cancel sets Status = Cancelled synchronously (AdminAuctionAppService.CancelAuctionAsync),
    // so this needs no polling window the way "End now"'s finalization does -- but a short
    // retry still absorbs the same request latency every other assertion in this suite allows for.
    await waitForAuction(auctionId, (auction) => auction.status === "Cancelled");
  });
});
