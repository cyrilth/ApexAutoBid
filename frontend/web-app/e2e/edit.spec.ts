import { test, expect, baseURL } from "./fixtures/test";
import { createAuctionViaUi } from "./fixtures/auction-builder";
import { throttleMutation } from "./fixtures/mutation-throttle";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 7 Task 15.10 -- Edit auction, against the LIVE stack: bob creates a throwaway E2E
 * auction (`./fixtures/auction-builder.ts`), then updates its updatable fields
 * (`components/AuctionForm.tsx`'s edit mode has no `auctionEnd` field -- `UpdateAuctionDto`
 * doesn't carry one, per that component's own remarks -- so this only touches color/mileage)
 * and confirms the detail page reflects the change.
 */
test.use({ storageState: storageStatePath("bob") });

test.describe("edit auction", () => {
  test("15.10 -- updating color and mileage on the edit form updates the detail page", async ({ page }) => {
    const tag = `E2E Edit ${Date.now()}`;
    const auctionId = await createAuctionViaUi(page, {
      make: tag,
      model: "Original",
      color: "Black",
      year: 2020,
      mileage: 1000,
      reservePrice: 0,
    });

    await page.goto(`/auctions/${auctionId}/edit`);
    await expect(page.getByRole("heading", { level: 1, name: "Edit auction" })).toBeVisible();

    // Pre-filled from the auction being edited (AuctionForm's defaultValues).
    await expect(page.getByLabel("Make")).toHaveValue(tag);
    await expect(page.getByLabel("Color")).toHaveValue("Black");

    await page.getByLabel("Color").fill("Emerald Green");
    await page.getByLabel("Mileage").fill("2500");
    // GatewayService's "strict" rate-limit policy applies to this PUT -- see
    // ./fixtures/mutation-throttle.ts's remarks.
    await throttleMutation();
    await page.getByRole("button", { name: "Save changes" }).click();

    await page.waitForURL(`${baseURL}/auctions/${auctionId}`);

    const specs = page.locator("dl").first();
    await expect(specs.getByText("Color", { exact: true }).locator("~ dd")).toHaveText("Emerald Green");
    await expect(specs.getByText("Mileage", { exact: true }).locator("~ dd")).toHaveText("2,500 mi");
    // Fields NOT touched by this edit stay as originally created.
    await expect(specs.getByText("Make", { exact: true }).locator("~ dd")).toHaveText(tag);
    await expect(specs.getByText("Model", { exact: true }).locator("~ dd")).toHaveText("Original");
  });
});
