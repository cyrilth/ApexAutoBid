"use client";

import { useState } from "react";
import type { TouchEvent } from "react";
import { CarImage } from "@/components/CarImage";
import { ChevronIcon } from "@/components/icons/ChevronIcon";
import type { AuctionImage } from "@/types/auction";

interface AuctionGalleryProps {
  images: AuctionImage[];
  alt: string;
}

/** Minimum horizontal drag distance (px) before a touch gesture counts as a swipe, not a tap. */
const SWIPE_THRESHOLD_PX = 40;

/**
 * Auction detail image gallery (Task 5.7, Docs/DesignGuide.md §4/§5). The
 * primary image (`sortOrder = 0`) shows first -- the backend already returns
 * `images` pre-sorted, so this simply renders index 0 initially. Click
 * prev/next arrows and touch swipe both cycle through the rest; a thumbnail
 * strip gives direct access to any image. Client Component: touch handlers
 * and click-driven state are both browser-only. Dependency-free per the
 * task brief -- plain state + pointer/touch handlers, no carousel library.
 */
export function AuctionGallery({ images, alt }: AuctionGalleryProps) {
  const [index, setIndex] = useState(0);
  const [touchStartX, setTouchStartX] = useState<number | null>(null);

  if (images.length === 0) {
    return <CarImage alt={alt} className="aspect-video w-full rounded-lg" />;
  }

  const current = images[index];
  const hasMultiple = images.length > 1;

  function goTo(next: number) {
    setIndex((next + images.length) % images.length);
  }

  function handleTouchStart(event: TouchEvent<HTMLDivElement>) {
    setTouchStartX(event.touches[0].clientX);
  }

  function handleTouchEnd(event: TouchEvent<HTMLDivElement>) {
    if (touchStartX == null) return;
    const deltaX = event.changedTouches[0].clientX - touchStartX;
    setTouchStartX(null);

    if (deltaX > SWIPE_THRESHOLD_PX) goTo(index - 1);
    else if (deltaX < -SWIPE_THRESHOLD_PX) goTo(index + 1);
  }

  return (
    <div className="space-y-3">
      <div
        className="relative aspect-video w-full overflow-hidden rounded-lg"
        onTouchStart={handleTouchStart}
        onTouchEnd={handleTouchEnd}
      >
        <CarImage src={current.url} alt={alt} className="h-full w-full" />

        {hasMultiple && (
          <>
            <button
              type="button"
              aria-label="Previous image"
              onClick={() => goTo(index - 1)}
              className="absolute top-1/2 left-2 -translate-y-1/2 rounded-full bg-white/80 p-2 text-slate-700 shadow hover:bg-white focus:outline-none focus:ring-2 focus:ring-primary-400"
            >
              <ChevronIcon direction="left" className="h-5 w-5" />
            </button>
            <button
              type="button"
              aria-label="Next image"
              onClick={() => goTo(index + 1)}
              className="absolute top-1/2 right-2 -translate-y-1/2 rounded-full bg-white/80 p-2 text-slate-700 shadow hover:bg-white focus:outline-none focus:ring-2 focus:ring-primary-400"
            >
              <ChevronIcon direction="right" className="h-5 w-5" />
            </button>
            <span className="absolute right-2 bottom-2 rounded bg-black/60 px-2 py-0.5 text-xs font-medium text-white">
              {index + 1} / {images.length}
            </span>
          </>
        )}
      </div>

      {hasMultiple && (
        <div className="flex gap-2 overflow-x-auto pb-1">
          {images.map((image, i) => (
            <button
              key={`${image.url}-${i}`}
              type="button"
              aria-label={`Show image ${i + 1} of ${images.length}`}
              aria-current={i === index}
              onClick={() => setIndex(i)}
              className={`h-16 w-20 flex-shrink-0 overflow-hidden rounded-md ring-2 transition-colors ${
                i === index ? "ring-primary-500" : "ring-transparent hover:ring-slate-300"
              }`}
            >
              <CarImage
                src={image.thumbnailUrl ?? image.url}
                alt={`${alt} thumbnail ${i + 1}`}
                className="h-full w-full"
              />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
