import { test, expect, baseURL } from "./fixtures/test";
import { uniqueE2EUser } from "./fixtures/test-data";
import { fetchConfirmationLink } from "./fixtures/mailpit";

/**
 * Phase 7 Task 15.16 -- Email verification, against the LIVE stack: register a brand-new user,
 * fetch the real confirmation email via the Mailpit API, confirm it, then create an auction
 * successfully.
 *
 * Registration auto-signs the new user in (Register/Index.cshtml.cs's OnPostAsync) with
 * `email_verified: false` baked into that first access token. Duende's "webapp" client does NOT
 * set `UpdateAccessTokenClaimsOnRefresh` (Config.cs), so a plain token refresh reuses the
 * claims captured at the ORIGINAL sign-in -- `email_verified` would stay `false` forever without
 * a fresh Authorization Code round-trip. This spec signs out and back in after confirming, which
 * re-runs ProfileService.GetProfileDataAsync (reads `EmailConfirmed` live) and issues a new
 * token with `email_verified: true` -- confirmed necessary and correct by running this spec
 * without that step first (the create-auction submission below 403s with the "EmailVerified"
 * policy's bare Forbid() otherwise).
 */
test.describe("email verification", () => {
  test("15.16 -- register, confirm via the real email, then create an auction", async ({ page }) => {
    const user = uniqueE2EUser();

    // ── Register ──────────────────────────────────────────────────────────
    // Reached via the same nav path a real visitor without an account would use: the "Sign in"
    // harness surfaces IdentityServer's login page, which links to "Create one" -- preserving
    // the pending OIDC ReturnUrl so registering also completes the Authorization Code flow back
    // into this app, exactly like signInViaIdentityServer's login path does.
    await page.goto("/session");
    await page.getByRole("button", { name: "Sign in" }).click();
    await page.waitForURL(/\/Account\/Login/);
    await page.getByRole("link", { name: "Create one" }).click();
    await page.waitForURL(/\/Account\/Register/);

    await page.getByLabel("Username", { exact: true }).fill(user.username);
    await page.getByLabel("Email", { exact: true }).fill(user.email);
    await page.getByLabel("Password", { exact: true }).fill(user.password);
    await page.getByLabel("Confirm password", { exact: true }).fill(user.password);

    // Cloudflare Turnstile's official always-pass TEST site key (backend/IdentityService/
    // appsettings.Development.json's Turnstile:SiteKey) still runs the real widget script and
    // round-trips to Cloudflare for a token -- wait for that hidden field to populate rather
    // than submitting immediately, or Register/Index.cshtml.cs's early
    // `string.IsNullOrWhiteSpace(TurnstileResponse)` check rejects the submission.
    await page.waitForFunction(() => {
      const input = document.querySelector('input[name="cf-turnstile-response"]') as HTMLInputElement | null;
      return !!input && input.value.length > 0;
    });

    await page.getByRole("button", { name: "Register", exact: true }).click();

    // Registration signs the new user straight in and completes the pending OIDC flow, landing
    // back on this app's /session harness (same as signInViaIdentityServer's login path).
    await page.waitForURL(`${baseURL}/session`);
    await expect(page.getByText(`Signed in as ${user.username}`)).toBeVisible();

    // Unverified accounts CAN browse and sign in (Requirements.md §3.4) -- the create-auction
    // page's amber "verify your email" banner is this policy made visible.
    await page.goto("/auctions/create");
    await expect(page.getByText("Verify your email address before creating an auction", { exact: false })).toBeVisible();

    // ── Fetch the confirmation link from Mailpit and confirm ────────────────
    const confirmationLink = await fetchConfirmationLink(user.email);
    await page.goto(confirmationLink);
    await expect(page.getByText("Your email address has been confirmed.")).toBeVisible();

    // ── Re-authenticate so the fresh token actually carries email_verified=true ──────────────
    await page.goto("/session");
    await page.getByRole("button", { name: "Sign out" }).click();
    await page.waitForURL(/Account\/Logout\/LoggedOut/);
    await page.getByRole("link", { name: "here" }).click();
    await page.waitForURL(`${baseURL}/`);

    await page.goto("/session");
    await page.getByRole("button", { name: "Sign in" }).click();
    await page.waitForURL(/\/Account\/Login/);
    await page.getByLabel("Username", { exact: true }).fill(user.username);
    await page.getByLabel("Password", { exact: true }).fill(user.password);
    await page.getByRole("button", { name: "Login", exact: true }).click();
    await page.waitForURL(`${baseURL}/session`);
    await expect(page.getByText(`Signed in as ${user.username}`)).toBeVisible();

    // ── Create an auction successfully ───────────────────────────────────
    await page.goto("/auctions/create");
    // The verify-email banner is gone now that this session's token reflects the confirmed
    // account.
    await expect(page.getByText("Verify your email address before creating an auction", { exact: false })).toHaveCount(0);

    // "E2E"-prefixed make/model tags this spec's data as test-created, per Docs/Tasks.md Phase 7
    // Task 15's guidance -- this run mutates the live dev database.
    await page.getByLabel("Make").fill("E2E Verified");
    await page.getByLabel("Model").fill("E2E Email Flow");
    await page.getByLabel("Color").fill("Silver");
    await page.getByLabel("Year").fill("2021");
    await page.getByLabel("Mileage").fill("500");
    await page.getByLabel("Reserve price").fill("0");

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

    // A plain externally hosted URL (AuctionImageManager's "Or add an image by URL" fallback) --
    // deliberately `127.0.0.1`, NOT the seeded MinIO `localhost:9000/auction-images/...` URL the
    // Task 15 brief suggested: AuctionAppService.ValidateGalleryAsync treats ANY URL under
    // `Images:PublicBaseUrl`/`Images:Bucket` (`http://localhost:9000/auction-images/`) as
    // platform-hosted and requires its path segment to be a bare GUID object key -- the
    // seeded auctions' human-readable filenames (e.g. "ford-gt.jpg") fail that check and 400
    // with "Invalid image gallery" even though the object itself exists and is well within the
    // size limit. `127.0.0.1:9000` resolves to the exact same MinIO instance (still fully
    // local, no external network dependency) but doesn't match the exact `localhost` prefix
    // string, so it's correctly treated as an external URL and exempted from that check. See
    // this task's final report for the suggested fix.
    await page.getByLabel("Or add an image by URL").fill("http://127.0.0.1:9000/auction-images/ford-gt.jpg");
    await page.getByRole("button", { name: "Add" }).click();

    await page.getByRole("button", { name: "Create auction" }).click();

    await page.waitForURL(/\/auctions\/[0-9a-f-]+$/);
    await expect(page.getByRole("heading", { level: 1, name: "2021 E2E Verified E2E Email Flow" })).toBeVisible();
  });
});
