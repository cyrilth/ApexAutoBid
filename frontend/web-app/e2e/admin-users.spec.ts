import { test, expect } from "./fixtures/test";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Phase 11 Task 10.2 -- Admin user management against the LIVE stack: create a fresh,
 * pre-confirmed user via the Create user modal, find them in the (searched) table, then reset
 * their password to a temporary one via the Reset password modal's "set new password"
 * (non-email) path -- deterministic, unlike the "send reset link" path, which would need a
 * Mailpit round-trip (`./fixtures/mailpit.ts`) this spec doesn't otherwise need.
 */
test.use({ storageState: storageStatePath("admin") });

test.describe("admin users", () => {
  test("10.2 -- create a user, then reset their password to a temporary one", async ({ page }) => {
    const username = `e2e_admin_${Date.now()}`;
    const email = `${username}@apexautobid.local`;

    await page.goto("/admin/users");
    await expect(page.getByRole("heading", { level: 1, name: "Users" })).toBeVisible();

    await page.getByRole("button", { name: "Create user" }).click();
    const createModal = page.getByRole("dialog");
    await expect(createModal.getByRole("heading", { level: 3, name: "Create user" })).toBeVisible();

    await createModal.getByLabel("Username").fill(username);
    await createModal.getByLabel("Email").fill(email);
    await createModal.getByLabel("Password").fill("Pass123$");
    // Pre-confirmed -- skips email verification, so the new user's status is deterministic
    // ("Confirmed") without waiting on a confirmation email.
    await createModal.getByRole("switch", { name: "Pre-confirmed" }).click();

    await createModal.getByRole("button", { name: "Create user" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);

    // Search narrows the table down to just the new user.
    await page.getByPlaceholder("Search by username or email").fill(username);
    await page.getByRole("button", { name: "Search" }).click();

    const row = page.getByRole("row").filter({ hasText: username });
    await expect(row).toBeVisible();
    await expect(row.getByText("Confirmed", { exact: true })).toBeVisible();

    // Reset password: set a new temporary password directly (not emailed) -- shown once.
    await row.getByRole("button", { name: "Reset password" }).click();
    const resetModal = page.getByRole("dialog");
    await expect(resetModal.getByRole("heading", { level: 3, name: `Reset password -- ${username}` })).toBeVisible();

    await resetModal.getByRole("switch", { name: "Send a reset link by email" }).click();
    await resetModal.getByLabel("New temporary password").fill("NewTempPass456$");
    await resetModal.getByRole("button", { name: "Set new password" }).click();

    await expect(resetModal.getByText("shown once -- copy it now", { exact: false })).toBeVisible();
    await expect(resetModal.getByText("NewTempPass456$", { exact: true })).toBeVisible();

    await resetModal.getByRole("button", { name: "Done" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });
});
