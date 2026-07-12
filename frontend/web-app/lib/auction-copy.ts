import { formatCurrency } from "@/lib/format";
import type { AuctionDetail, AuctionStatus } from "@/types/auction";

const STATUS_LABELS: Record<AuctionStatus, string> = {
  Live: "Live",
  Finished: "Sold",
  ReserveNotMet: "Reserve not met",
  Cancelled: "Cancelled",
};

/**
 * One-line human summary of an auction's current state -- shared by the
 * detail page's `generateMetadata` (`og:description`/`twitter:description`,
 * Task 5.6 -- "status, current high bid / sold amount, auction end") and its
 * social share text (Task 5.5), so link previews and share-intent text stay
 * in sync instead of duplicating the same phrasing twice.
 */
export function summarizeAuction(auction: AuctionDetail): string {
  const statusLabel = STATUS_LABELS[auction.status];

  const priceLabel =
    auction.status === "Finished" && auction.soldAmount != null
      ? `sold for ${formatCurrency(auction.soldAmount)}`
      : auction.currentHighBid != null
        ? `current bid ${formatCurrency(auction.currentHighBid)}`
        : "no bids yet";

  const endLabel = new Date(auction.auctionEnd).toLocaleString("en-US", {
    dateStyle: "medium",
    timeStyle: "short",
  });

  return `${statusLabel} -- ${priceLabel}. Auction ends ${endLabel}.`;
}
