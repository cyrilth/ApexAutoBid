import { test, expect, baseURL } from "./fixtures/test";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 11 Task 10.1 -- Admin route protection against the LIVE stack: an admin (the seeded
 * `admin` user, signed in once by `../global-setup.ts` alongside bob/alice/tom) reaches
 * `/admin`; a signed-in but non-admin user (bob) is redirected away
 * (`app/admin/layout.tsx`'s server-side role gate, Task 8.1).
 */
test.describe("admin access", () => {
  test("10.1 -- an admin sees the Admin nav link and can reach the dashboard", async ({ browser }) => {
    const context = await browser.newContext({ storageState: storageStatePath("admin") });
    const page = await context.newPage();

    await page.goto("/");
    const adminLink = page.getByRole("link", { name: "Admin", exact: true });
    await expect(adminLink).toBeVisible();
    await adminLink.click();

    await expect(page).toHaveURL(`${baseURL}/admin`);
    await expect(page.getByRole("heading", { level: 1, name: "Dashboard" })).toBeVisible();

    // Sidebar nav (Docs/DesignGuide.md §4) -- every section is reachable.
    await expect(page.getByRole("link", { name: "Users" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Auctions" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Banners" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Settings" })).toBeVisible();

    await context.close();
  });

  test("10.1 -- a signed-in non-admin is redirected away from /admin", async ({ browser }) => {
    const context = await browser.newContext({ storageState: storageStatePath("bob") });
    const page = await context.newPage();

    // The "Admin" nav link never renders for a non-admin in the first place.
    await page.goto("/");
    await expect(page.getByRole("link", { name: "Admin", exact: true })).toHaveCount(0);

    // Direct navigation is also blocked server-side, not merely hidden from the nav.
    await page.goto("/admin");
    await expect(page).toHaveURL(`${baseURL}/`);
    await expect(page.getByRole("heading", { level: 1, name: "Dashboard" })).toHaveCount(0);

    await context.close();
  });
});
