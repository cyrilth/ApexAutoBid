"use client";

import Image from "next/image";
import { useState } from "react";
import { CarIcon } from "@/components/icons/CarIcon";
import { isOptimizableImageSrc } from "@/lib/image-config";

interface CarImageProps {
  src?: string | null;
  alt: string;
  className?: string;
  /**
   * Forwarded to `next/image`'s `sizes` prop. Every `CarImage` renders with
   * `fill`, and the next/image docs call `sizes` required whenever `fill` is
   * used -- without it the browser assumes the image is as wide as the
   * viewport and downloads unnecessarily large files. Callers should tune
   * this to how the image actually renders (see AuctionCard/AuctionGallery);
   * the default only covers the narrowest common case (a small fixed-size
   * thumbnail).
   */
  sizes?: string;
}

const DEFAULT_SIZES = "96px";

/**
 * Shared image slot for anywhere an auction image renders (Docs/DesignGuide.md
 * §5) -- a `bg-slate-100` placeholder with a car icon when there's no image,
 * or when the image fails to load, never a broken-image glyph. Place one
 * shared `<CarImage>` component in `components/` and use it everywhere an
 * auction image renders.
 *
 * Uses `next/image` (Phase 7 Task 12). `next/image` throws a runtime
 * "hostname not configured" error for any remote host not listed in
 * `images.remotePatterns` (next.config.ts) -- fine for platform-hosted
 * images, which always come from the configured MinIO/object-storage host,
 * but the auction form's "plain URL" fallback (Task 6.3) accepts *any*
 * http(s) host, and local upload previews are `blob:` URLs. Neither of those
 * can be whitelisted in advance, so `isOptimizableImageSrc` tells them apart
 * from platform-hosted images and those render `unoptimized` -- the src is
 * served as-is, skipping the host check entirely -- so an arbitrary
 * user-supplied URL never crashes the page.
 */
export function CarImage({ src, alt, className = "", sizes = DEFAULT_SIZES }: CarImageProps) {
  // Stores WHICH src failed rather than a boolean, so `failed` is derived
  // per-src: when src changes -- e.g. AuctionGallery reuses the same CarImage
  // instance as the user cycles through images -- the failure no longer
  // applies and the new image renders, with no state reset (render-time or
  // Effect) needed at all.
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  const failed = src != null && src === failedSrc;

  if (!src || failed) {
    return (
      <div className={`flex items-center justify-center bg-slate-100 ${className}`}>
        <CarIcon className="h-10 w-10 text-slate-400" />
      </div>
    );
  }

  return (
    <div className={`relative overflow-hidden ${className}`}>
      <Image
        src={src}
        alt={alt}
        fill
        sizes={sizes}
        unoptimized={!isOptimizableImageSrc(src)}
        className="object-cover"
        onError={() => setFailedSrc(src)}
      />
    </div>
  );
}
