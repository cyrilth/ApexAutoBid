import { test, expect, baseURL } from "./fixtures/test";
import { imageStorageOrigin } from "./fixtures/dev-domains";
import { createTestJpeg, createTestPng } from "./fixtures/images";
import { throttleMutation } from "./fixtures/mutation-throttle";
import { storageStatePath } from "./fixtures/storage-state";

/**
 * Decodes `next/image`'s optimizer proxy URL (`/_next/image?url=<encoded>&w=...&q=...` --
 * `CarImage`'s `<Image>` renders this whenever `isOptimizableImageSrc` allows optimization,
 * which every platform-hosted (uploaded) image here does -- back to the ORIGINAL storage URL it
 * wraps. The `w`/`q` bucket next/image picks can vary by viewport/DPR; the underlying `url`
 * param -- which is what actually identifies WHICH image this is -- does not.
 */
function decodeNextImageUrl(src: string): string {
  const url = new URL(src, baseURL);
  const inner = url.searchParams.get("url");
  if (!inner) {
    throw new Error(`decodeNextImageUrl: no "url" query param on "${src}" -- is this really a next/image src?`);
  }
  return inner;
}

/**
 * Phase 7 Task 15.17 -- Image upload, against the LIVE stack: create an auction with multiple
 * REAL file uploads (`setInputFiles` with in-memory buffers -- `./fixtures/images.ts` generates
 * two small, distinct JPEG/PNG images with `sharp`, never touching disk) through
 * `AuctionImageManager`'s direct-to-storage presigned-PUT flow (Task 6.3), then confirms the
 * images actually landed in storage (MinIO, via a real GUID object key -- `requestUploadUrl`'s
 * `objectUrl`) and that the detail page's gallery renders them with the PRIMARY image (the
 * first one added, never reordered in this spec) first.
 *
 * Unlike `register.spec.ts`/`fixtures/auction-builder.ts`'s "add an image by URL" fallback
 * (needed there only to dodge `ValidateGalleryAsync`'s platform-hosted-URL GUID check -- see
 * those files' remarks), a genuine upload's `objectUrl` already carries a real GUID key
 * (`AuctionAppService.ValidateGalleryAsync`'s `Guid.TryParse` branch), so it passes that check
 * outright -- no workaround needed for this spec.
 */
test.use({ storageState: storageStatePath("bob") });

test.describe("image upload", () => {
  test("15.17 -- multiple uploaded images land in storage and render in the gallery, primary first", async ({
    page,
  }) => {
    const red = await createTestJpeg("e2e-red.jpg", [220, 20, 20]);
    const blue = await createTestPng("e2e-blue.png", [20, 20, 220]);
    const tag = `E2E Upload ${Date.now()}`;

    await page.goto("/auctions/create");
    await page.getByLabel("Make").fill(tag);
    await page.getByLabel("Model").fill("Gallery");
    await page.getByLabel("Color").fill("Multi");
    await page.getByLabel("Year").fill("2022");
    await page.getByLabel("Mileage").fill("10");
    await page.getByLabel("Reserve price").fill("0");

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

    // Two real files through the actual file input -- `AuctionImageManager.handleFilesSelected`
    // kicks off a real presigned-PUT upload to MinIO for each, concurrently. Each file's
    // `requestUploadUrl` call is a `POST /api/auctions/upload-url` -- GatewayService's "strict"
    // rate-limit policy covers it too (see ./fixtures/mutation-throttle.ts's remarks) -- reserve
    // both slots up front since the two uploads fire together right after `setInputFiles`.
    await throttleMutation();
    await throttleMutation();
    const fileInput = page.locator("#auction-images");
    await fileInput.setInputFiles([
      { name: red.name, mimeType: red.mimeType, buffer: red.buffer },
      { name: blue.name, mimeType: blue.mimeType, buffer: blue.buffer },
    ]);

    // Per-file upload success: "Uploading..." only shows while `status === "uploading"`; the
    // "Generate thumbnail" button only renders once `status === "ready"` AND the image has a
    // `key` (i.e. it's a genuine platform upload, not the URL fallback) -- both uploads
    // reaching that state is the manager's own success signal.
    await expect(page.getByText("Uploading…")).toHaveCount(0, { timeout: 15_000 });
    await expect(page.getByRole("button", { name: "Generate thumbnail" })).toHaveCount(2);
    await expect(page.getByText("2 of 10 images", { exact: false })).toBeVisible();

    // The manager never reorders what it's given (no drag/move-button interaction here), so
    // gallery order is exactly upload order: red (added first) is primary.
    const managerRows = page.locator("ul li").filter({ has: page.getByRole("button", { name: "Remove image" }) });
    await expect(managerRows).toHaveCount(2);
    await expect(managerRows.nth(0).getByText("Primary", { exact: true })).toBeVisible();
    await expect(managerRows.nth(1).getByText("Primary", { exact: true })).toHaveCount(0);

    const redManagerSrc = await managerRows.nth(0).locator('img[alt^="Gallery image"]').getAttribute("src");
    const blueManagerSrc = await managerRows.nth(1).locator('img[alt^="Gallery image"]').getAttribute("src");
    expect(redManagerSrc).toBeTruthy();
    expect(blueManagerSrc).toBeTruthy();
    const redObjectUrl = decodeNextImageUrl(redManagerSrc!);
    const blueObjectUrl = decodeNextImageUrl(blueManagerSrc!);

    // Both are real, distinct, storage-hosted objects (Requirements §3.1 -- the backend's
    // `Images:PublicBaseUrl`/`Images:Bucket`), proving the uploads actually landed in storage
    // (not just a local blob: preview). The expected origin follows the target stack
    // (`imageStorageOrigin`): MinIO directly in the dev loop, the Nginx storage domain in the
    // docker-compose stack.
    const objectUrlPattern = new RegExp(
      `^${imageStorageOrigin.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}/auction-images/[0-9a-f-]{36}$`,
    );
    expect(redObjectUrl).toMatch(objectUrlPattern);
    expect(blueObjectUrl).toMatch(objectUrlPattern);
    expect(redObjectUrl).not.toBe(blueObjectUrl);

    await throttleMutation();
    await page.getByRole("button", { name: "Create auction" }).click();
    await page.waitForURL(/\/auctions\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("heading", { level: 1, name: `2022 ${tag} Gallery` })).toBeVisible();

    // ── Detail page gallery: primary image first, in the thumbnail strip AND the main frame ──
    const mainImage = page.locator('button[aria-label="Next image"]').locator("..").locator("img").first();
    const mainSrc = await mainImage.getAttribute("src");
    expect(mainSrc).toBeTruthy();
    expect(decodeNextImageUrl(mainSrc!)).toBe(redObjectUrl);

    const thumbButtons = page.getByRole("button", { name: /Show image \d+ of 2/ });
    await expect(thumbButtons).toHaveCount(2);

    const firstThumbSrc = await thumbButtons.nth(0).locator("img").getAttribute("src");
    const secondThumbSrc = await thumbButtons.nth(1).locator("img").getAttribute("src");
    expect(firstThumbSrc).toBeTruthy();
    expect(secondThumbSrc).toBeTruthy();
    expect(decodeNextImageUrl(firstThumbSrc!)).toBe(redObjectUrl);
    expect(decodeNextImageUrl(secondThumbSrc!)).toBe(blueObjectUrl);

    // The active thumbnail (the one currently shown in the main frame) is the first/primary one.
    await expect(thumbButtons.nth(0)).toHaveAttribute("aria-current", "true");
  });
});
