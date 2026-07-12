import { CarIcon } from "@/components/icons/CarIcon";

interface CarImageProps {
  src?: string | null;
  alt: string;
  className?: string;
}

/**
 * Shared image slot for anywhere an auction image renders (Docs/DesignGuide.md
 * §5) -- a `bg-slate-100` placeholder with a car icon when there's no image,
 * never a broken-image glyph.
 *
 * Deliberately a plain `<img>`, not `next/image`, until Phase 7 Task 12
 * whitelists the MinIO/object-storage host via `remotePatterns`: `next/image`
 * throws a runtime "hostname not configured" error for any host not in that
 * list, which would break real (non-Vercel, non-seed) auction images today.
 * Swap this to `next/image` once that task lands.
 */
export function CarImage({ src, alt, className = "" }: CarImageProps) {
  if (!src) {
    return (
      <div className={`flex items-center justify-center bg-slate-100 ${className}`}>
        <CarIcon className="h-10 w-10 text-slate-400" />
      </div>
    );
  }

  return (
    // eslint-disable-next-line @next/next/no-img-element -- see doc comment above
    <img src={src} alt={alt} loading="lazy" className={`object-cover ${className}`} />
  );
}
