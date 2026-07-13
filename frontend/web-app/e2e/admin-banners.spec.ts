import { test, expect } from "./fixtures/test";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 11 Task 10.5 -- Live banners against the LIVE stack: an admin publishes a "HomePage"
 * banner from `/admin/banners`; a separate, already-open, anonymous viewer on the home page
 * sees it appear WITHOUT a refresh (`components/LiveBanners.tsx`'s "BannerPublished" SignalR
 * handler, Task 8.6). Two separate browser contexts, same shape as `realtime.spec.ts`'s
 * multi-context live-update spec: the `page` fixture (admin, via `storageState`) publishes;
 * a fresh anonymous context is the viewer.
 */
test.use({ storageState: storageStatePath("admin") });

test.describe("admin banners", () => {
  test("10.5 -- publishing a banner appears on the home page live, without a refresh", async ({
    page,
    browser,
  }) => {
    const message = `E2E live banner ${Date.now()}`;

    const viewerContext = await browser.newContext();
    const viewerPage = await viewerContext.newPage();
    await viewerPage.goto("/");
    await expect(viewerPage.getByText(message)).toHaveCount(0);

    await page.goto("/admin/banners");
    await page.getByRole("button", { name: "New banner" }).click();

    const modal = page.getByRole("dialog");
    await expect(modal.getByRole("heading", { level: 3, name: "New banner" })).toBeVisible();

    await modal.getByLabel("Message").fill(message);
    await modal.getByLabel("Scope").selectOption("HomePage");

    // "Active from" defaults to "now" already (BannerFormModal's create-mode default) --
    // only "Active until" needs an explicit value, set an hour out so the banner is active
    // for the rest of this test.
    const activeUntilInput = modal.getByPlaceholder("Active until date and time");
    const activeUntil = new Date(Date.now() + 60 * 60 * 1000);
    const formattedActiveUntil = activeUntil.toLocaleString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
      hour12: true,
    });
    await activeUntilInput.click();
    await activeUntilInput.fill(formattedActiveUntil);
    await page.keyboard.press("Enter");
    await page.keyboard.press("Escape");

    await modal.getByRole("button", { name: "Publish banner" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Admin's own table (a `router.refresh()` re-fetch) shows it too.
    await expect(page.getByText(message)).toBeVisible();

    // The real assertion: the SEPARATE viewer, sitting on the home page since before this
    // banner existed, sees it appear live -- no navigation/reload on `viewerPage` since its
    // very first `goto` above. SignalR's "BannerPublished" broadcast should land within a few
    // seconds; a generous bounded timeout absorbs real network jitter.
    await expect(viewerPage.getByText(message)).toBeVisible({ timeout: 15_000 });

    await viewerContext.close();
  });
});
