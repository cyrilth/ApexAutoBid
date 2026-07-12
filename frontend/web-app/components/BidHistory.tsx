import { formatCurrency } from "@/lib/format";
import { BidStatusBadge } from "@/components/BidStatusBadge";
import { EmptyState } from "@/components/EmptyState";
import type { Bid } from "@/types/bid";

interface BidHistoryProps {
  bids: Bid[];
}

function formatBidTime(bidTime: string): string {
  return new Date(bidTime).toLocaleString("en-US", { dateStyle: "medium", timeStyle: "short" });
}

/**
 * Bid history list (Task 5.3, Docs/DesignGuide.md §7 -- "vertical list,
 * newest first"). `GET api/bids/{auctionId}` already returns bids newest
 * first (BidsController's own contract) -- sorted defensively here too so
 * this component's rendering order doesn't silently depend on the caller
 * never changing that. Server Component: static list, no interactivity.
 */
export function BidHistory({ bids }: BidHistoryProps) {
  if (bids.length === 0) {
    return <EmptyState message="No bids yet -- be the first to bid." />;
  }

  const sorted = [...bids].sort((a, b) => new Date(b.bidTime).getTime() - new Date(a.bidTime).getTime());

  return (
    <ul className="divide-y divide-slate-200 rounded-lg border border-slate-200 bg-white">
      {sorted.map((bid) => (
        <li key={bid.id} className="flex items-center justify-between gap-3 px-4 py-3">
          <div>
            <p className="font-semibold text-slate-900">{bid.bidder}</p>
            <p className="text-sm text-slate-500">{formatBidTime(bid.bidTime)}</p>
          </div>
          <div className="flex items-center gap-2">
            <p className="font-semibold text-slate-900">{formatCurrency(bid.amount)}</p>
            <BidStatusBadge status={bid.bidStatus} />
          </div>
        </li>
      ))}
    </ul>
  );
}
