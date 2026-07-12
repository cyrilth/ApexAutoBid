import { test, expect, baseURL } from "./fixtures/test";
import { GATEWAY_URL } from "./fixtures/api";
import { createAuctionViaUi } from "./fixtures/auction-builder";
import { throttleMutation } from "./fixtures/mutation-throttle";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 7 Task 15.11 -- Delete auction, against the LIVE stack: bob creates a throwaway E2E
 * auction, deletes it via the detail page's `DeleteAuctionButton` (Flowbite `Modal` confirm/
 * cancel), and confirms it's actually gone. The deterministic check is `GET
 * api/auctions/{id}` returning 404 straight from the Auction Service (via the Gateway) --
 * asserting the auction's absence from `GET api/search` instead would be flaky by construction
 * (Task 15's brief: search-index sync is event-driven/eventually-consistent, same caveat
 * `pagination.spec.ts`/`listing.spec.ts` already document for the OTHER direction, an auction
 * just having been CREATED).
 */
test.use({ storageState: storageStatePath("bob") });

test.describe("delete auction", () => {
  test("15.11 -- deleting an auction, after confirming the modal, removes it", async ({ page }) => {
    const tag = `E2E Delete ${Date.now()}`;
    const auctionId = await createAuctionViaUi(page, {
      make: tag,
      model: "Throwaway",
      color: "Gray",
      year: 2019,
      mileage: 500,
      reservePrice: 0,
    });
    const displayName = `2019 ${tag} Throwaway`;

    await page.goto(`/auctions/${auctionId}`);
    await expect(page.getByRole("heading", { level: 1, name: displayName })).toBeVisible();

    await page.getByRole("button", { name: `Delete ${displayName}` }).click();

    // Flowbite Modal, popup style -- names the specific auction so a seller with several
    // auctions can double-check they picked the right one before confirming. Scoped to the
    // dialog itself -- the page's own <h1> behind it repeats the exact same text.
    const modal = page.getByRole("dialog");
    await expect(modal.getByRole("heading", { level: 3, name: "Delete this auction?" })).toBeVisible();
    await expect(modal.getByText(displayName, { exact: false })).toBeVisible();

    // GatewayService's "strict" rate-limit policy applies to this DELETE -- see
    // ./fixtures/mutation-throttle.ts's remarks.
    await throttleMutation();
    await page.getByRole("button", { name: "Yes, delete it" }).click();

    // DeleteAuctionButton's handleConfirmDelete pushes straight to the listing page.
    await page.waitForURL(`${baseURL}/`);

    // Deterministic (not eventually-consistent) proof the auction is actually gone: the Auction
    // Service itself, the system of record, 404s on it now.
    const res = await fetch(`${GATEWAY_URL}/api/auctions/${auctionId}`);
    expect(res.status).toBe(404);
  });
});
