import Link from "next/link";
import { CarImage } from "@/components/CarImage";
import { AuctionStatusBadge } from "@/components/AuctionStatusBadge";
import { formatCurrency } from "@/lib/format";
import { formatTimeRemaining } from "@/lib/time";
import type { AuctionItem } from "@/types/auction";

/**
 * Listing grid card (Docs/DesignGuide.md §6/§4): image, make/model/year,
 * status badge, high bid, time remaining. The whole card links to the
 * auction detail page.
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
      {/* Plain markup styled like Flowbite's Card rather than <Card renderImage={...}>:
          Card is a Client Component, and this Server Component can't pass it a render
          function across the RSC boundary (same constraint as the styled-Link buttons
          on the listing/detail pages). */}
      <div className="flex h-full flex-col overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm transition-colors hover:shadow-md">
        <CarImage
          src={item.thumbnailUrl ?? item.imageUrl}
          alt={`${item.year} ${item.make} ${item.model}`}
          className="aspect-[4/3] w-full"
          // Tuned to AuctionResults'/AuctionGridSkeleton's 4-col responsive
          // grid (grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4)
          // inside the max-w-7xl page container.
          sizes="(min-width: 1280px) 300px, (min-width: 1024px) 33vw, (min-width: 640px) 50vw, 100vw"
        />
        <div className="flex h-full flex-col gap-3 p-4">
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
            <p className="text-right text-sm text-slate-500">
              {formatTimeRemaining(item.auctionEnd)}
            </p>
          </div>
        </div>
      </div>
    </Link>
  );
}
