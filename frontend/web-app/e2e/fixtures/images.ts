import sharp from "sharp";

/** A solid-color test image, generated on the fly (Task 15.17) -- never touches disk, small enough
 * (a few KB) to stay well under `AuctionImageManager`'s 5 MB client-side cap and the backend's
 * own `Images:MaxSizeMB`. `sharp` is already a real dependency of this app (`next.config.ts`'s
 * image optimizer), reused here rather than adding a second image library just for tests. */
export interface TestImage {
  name: string;
  mimeType: string;
  buffer: Buffer;
}

/**
 * Builds a small, single-color JPEG. Distinct `[r, g, b]` inputs (e.g. pure red vs. pure blue)
 * produce visibly, byte-for-byte distinct files -- not that Batch C's specs inspect pixels, but
 * it keeps two images generated in the same test unambiguously different if ever inspected by a
 * human debugging a failure.
 */
export async function createTestJpeg(name: string, color: [number, number, number]): Promise<TestImage> {
  const buffer = await sharp({
    create: { width: 32, height: 24, channels: 3, background: { r: color[0], g: color[1], b: color[2] } },
  })
    .jpeg()
    .toBuffer();
  return { name, mimeType: "image/jpeg", buffer };
}

/** Same idea as `createTestJpeg`, PNG instead -- `AuctionImageManager`'s allow-list accepts both. */
export async function createTestPng(name: string, color: [number, number, number]): Promise<TestImage> {
  const buffer = await sharp({
    create: { width: 32, height: 24, channels: 3, background: { r: color[0], g: color[1], b: color[2] } },
  })
    .png()
    .toBuffer();
  return { name, mimeType: "image/png", buffer };
}
