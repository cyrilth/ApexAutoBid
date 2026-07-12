import { AuctionCardSkeleton } from "@/components/AuctionCardSkeleton";

/** Loading state for the auction grid (Docs/DesignGuide.md §6) -- one skeleton page's worth of cards. */
export function AuctionGridSkeleton({ count = 12 }: { count?: number }) {
  return (
    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {Array.from({ length: count }).map((_, index) => (
        <AuctionCardSkeleton key={index} />
      ))}
    </div>
  );
}
