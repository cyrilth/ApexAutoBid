import Link from "next/link";
import { Card } from "flowbite-react";
import { CarImage } from "@/components/CarImage";
import { AuctionStatusBadge } from "@/components/AuctionStatusBadge";
import { formatCurrency } from "@/lib/format";
import { formatTimeRemaining } from "@/lib/time";
import type { AuctionItem } from "@/types/auction";

/**
 * Listing grid card (Docs/DesignGuide.md §6/§4): image, make/model/year,
 * status badge, high bid, time remaining. The whole card links to the
 * auction detail page (Task 5 -- not built yet, so this 404s until then).
 */
export function AuctionCard({ item }: { item: AuctionItem }) {
  const isSold = item.status === "Finished";
  const highBid = isSold ? item.soldAmount : item.currentHighBid;
  const bidLabel = isSold ? "Sold for" : "Current bid";

  return (
    <Link
      href={`/auctions/${item.id}`}
      className="block h-full rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-400"
    >
      <Card
        className="h-full overflow-hidden p-0 transition-colors hover:shadow-md"
        theme={{ root: { children: "flex h-full flex-col gap-3 p-4" } }}
        renderImage={() => (
          <CarImage
            src={item.thumbnailUrl ?? item.imageUrl}
            alt={`${item.year} ${item.make} ${item.model}`}
            className="aspect-[4/3] w-full"
            // Tuned to AuctionResults'/AuctionGridSkeleton's 4-col responsive
            // grid (grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4)
            // inside the max-w-7xl page container.
            sizes="(min-width: 1280px) 300px, (min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw"
          />
        )}
      >
        <div className="flex items-start justify-between gap-2">
          <h3 className="text-lg font-semibold text-slate-900">
            {item.year} {item.make} {item.model}
          </h3>
          <AuctionStatusBadge item={item} />
        </div>

        <div className="mt-auto flex items-end justify-between gap-2">
          <div>
            <p className="text-sm text-slate-500">{bidLabel}</p>
            <p className="text-lg font-semibold text-slate-900">
              {highBid != null ? formatCurrency(highBid) : "No bids yet"}
            </p>
          </div>
          <p className="text-right text-sm text-slate-500">{formatTimeRemaining(item.auctionEnd)}</p>
        </div>
      </Card>
    </Link>
  );
}
