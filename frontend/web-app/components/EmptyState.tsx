import Link from "next/link";
import { CarIcon } from "@/components/icons/CarIcon";

interface EmptyStateProps {
  message: string;
  ctaLabel?: string;
  ctaHref?: string;
}

/**
 * Centered icon + one-line message + relevant CTA (Docs/DesignGuide.md §6).
 *
 * The CTA is a plain styled `<Link>` (matching the primary-button classes
 * used the same way in `app/page.tsx`/`app/auctions/[id]/page.tsx`), NOT a
 * Flowbite `Button as={Link}` -- this component is rendered from Server
 * Components (e.g. `components/AuctionResults.tsx`, `app/not-found.tsx`),
 * and `Button` (a Client Component) can't receive the `Link` function
 * component as a prop value across that boundary: React logs "Functions
 * cannot be passed directly to Client Components" and the render fails
 * (confirmed against the dev server's own error output -- this used to
 * silently break `AuctionResults`' empty-filter state and would have taken
 * `app/not-found.tsx`'s whole response down to a 500 instead of a 404).
 */
export function EmptyState({ message, ctaLabel, ctaHref }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-lg border border-slate-200 bg-white py-16 text-center">
      <CarIcon className="h-12 w-12 text-slate-300" />
      <p className="text-sm text-slate-500">{message}</p>
      {ctaLabel && ctaHref && (
        <Link
          href={ctaHref}
          className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-medium text-white hover:bg-primary-700 focus:outline-none focus:ring-4 focus:ring-primary-400"
        >
          {ctaLabel}
        </Link>
      )}
    </div>
  );
}
