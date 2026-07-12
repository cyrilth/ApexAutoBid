/**
 * Object-storage host that serves auction images -- MinIO in dev
 * (`docker/docker-compose.infra.yml`), any S3-compatible host in production.
 * Mirrors the Auction Service's `Images:PublicBaseUrl`
 * (`backend/AuctionService/AuctionService.API/appsettings.Development.json`),
 * which is what `MinioImageStorage.BuildObjectUrl` stamps onto every
 * `AuctionImage.Url`/`ThumbnailUrl` the backend returns, and what
 * `DbInitializer` uses for seed data.
 *
 * `NEXT_PUBLIC_`-prefixed so this same value is available both in
 * `next.config.ts` (Node, build time) and in `CarImage` (browser, Task 12) --
 * one source of truth instead of hardcoding the host twice. Changing it in
 * production means setting `NEXT_PUBLIC_IMAGE_STORAGE_URL` and rebuilding:
 * `images.remotePatterns` is baked in at build time, it can't change at
 * runtime (see `.env.example`).
 */
export const IMAGE_STORAGE_URL = process.env.NEXT_PUBLIC_IMAGE_STORAGE_URL ?? "http://localhost:9000";

/** Bucket path prefix under the storage host (`MinioImageStorage.BuildObjectUrl`, `Images:Bucket`). */
const AUCTION_IMAGES_PREFIX = `${IMAGE_STORAGE_URL}/auction-images/`;

/**
 * Whether `src` is safe to hand to `next/image`'s built-in optimizer: true
 * only for our own object-storage host. `next/image` throws a runtime
 * "hostname not configured" error for any remote host not listed in
 * `images.remotePatterns` -- fine for platform-hosted images (always this
 * host), but not for the auction form's "plain URL" fallback (Task 6.3),
 * which accepts *any* http(s) host, or a local `blob:`/`data:` preview
 * during upload. Callers should render those `unoptimized` instead of
 * relying on the default loader (see `components/CarImage.tsx`).
 */
export function isOptimizableImageSrc(src: string): boolean {
  return src.startsWith(AUCTION_IMAGES_PREFIX);
}
