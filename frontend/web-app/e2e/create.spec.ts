import { test, expect } from "./fixtures/test";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 7 Task 15.9 -- Create auction, against the LIVE stack: a plain already-signed-in seller
 * (bob, via a reused `storageState` -- see `./fixtures/storage-state.ts` and `./global-setup.ts`)
 * fills out the full create form and submits successfully. Complements `register.spec.ts`'s
 * 15.16 spec, which covers the same form but as the LAST step of a fresh-registration/email-
 * verification journey -- this one is the plain, already-verified signed-in path.
 */
test.use({ storageState: storageStatePath("bob") });

test.describe("create auction", () => {
  test("15.9 -- filling out the full form and submitting lands on the new auction's detail page", async ({
    page,
  }) => {
    const tag = `E2E Create ${Date.now()}`;

    await page.goto("/auctions/create");
    await expect(page.getByRole("heading", { level: 1, name: "Create auction" })).toBeVisible();

    await page.getByLabel("Make").fill(tag);
    await page.getByLabel("Model").fill("Coupe");
    await page.getByLabel("Color").fill("Midnight Blue");
    await page.getByLabel("Year").fill("2023");
    await page.getByLabel("Mileage").fill("1200");
    await page.getByLabel("Reserve price").fill("15000");

    const auctionEnd = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000);
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

    // URL-fallback image (see fixtures/auction-builder.ts's identical remarks on the
    // 127.0.0.1 workaround this app's ValidateGalleryAsync currently requires).
    await page.getByLabel("Or add an image by URL").fill("http://127.0.0.1:9000/auction-images/ford-gt.jpg");
    await page.getByRole("button", { name: "Add" }).click();
    await expect(page.getByText("External URL")).toBeVisible();

    await page.getByRole("button", { name: "Create auction" }).click();

    await page.waitForURL(/\/auctions\/[0-9a-f-]{36}$/);
    const auctionId = /\/auctions\/([0-9a-f-]{36})$/.exec(page.url())?.[1];
    expect(auctionId).toBeTruthy();

    // The details actually render -- heading, spec grid, and the seller's own edit/delete
    // controls (proves this landed signed in as the owning seller, not merely "some page").
    await expect(page.getByRole("heading", { level: 1, name: `2023 ${tag} Coupe` })).toBeVisible();
    const specs = page.locator("dl").first();
    await expect(specs.getByText("Seller", { exact: true }).locator("~ dd")).toHaveText("bob");
    await expect(specs.getByText("Make", { exact: true }).locator("~ dd")).toHaveText(tag);
    await expect(specs.getByText("Model", { exact: true }).locator("~ dd")).toHaveText("Coupe");
    await expect(specs.getByText("Color", { exact: true }).locator("~ dd")).toHaveText("Midnight Blue");
    await expect(specs.getByText("Mileage", { exact: true }).locator("~ dd")).toHaveText("1,200 mi");
    await expect(specs.getByText("Reserve price", { exact: true }).locator("~ dd")).toHaveText("$15,000");

    await expect(page.getByRole("link", { name: "Edit" })).toBeVisible();
    await expect(page.getByRole("button", { name: `Delete 2023 ${tag} Coupe` })).toBeVisible();
  });
});
