import { Badge } from "flowbite-react";
import type { AuctionStatus } from "@/types/auction";

type StatusBadgeColor = "primary" | "amber" | "slate" | "sold" | "failure";

/**
 * The only fields `resolveStatus` actually needs. Deliberately narrower than
 * `AuctionItem` (the listing/search projection) so this badge also accepts
 * `AuctionDetail` (the detail page's full auction shape, Task 5.1) -- both
 * interfaces satisfy this structurally, no adapter/mapping needed at either
 * call site.
 */
interface StatusBadgeItem {
  status: AuctionStatus;
  auctionEnd: string;
}

/**
 * Status badge coloring/labels per Docs/DesignGuide.md §2 (badges always
 * carry a text label -- status is never conveyed by color alone). "Ending
 * soon" is derived (Live with < 6h left), not a distinct backend status;
 * "Sold" is a Finished auction -- this domain's `Finished` status always
 * implies a winner/sale (see Requirements.md §3.1's Status enum and seed
 * data), `ReserveNotMet` is the "ended without selling" case.
 *
 * "Ended" is also derived: BiddingService finalizes auctions in a background
 * job, so an auction whose `auctionEnd` has passed can still carry
 * `status: "Live"` until the AuctionFinished event lands (the Search service
 * even files such rows under its `finished` filter). Without this branch a
 * negative `hoursRemaining` would satisfy `<= 6` and mislabel an already-over
 * auction "Ending soon".
 */
function resolveStatus(item: StatusBadgeItem, now: Date): { label: string; color: StatusBadgeColor } {
  switch (item.status) {
    case "Cancelled":
      return { label: "Cancelled", color: "failure" };
    case "Finished":
      return { label: "Sold", color: "sold" };
    case "ReserveNotMet":
      return { label: "Reserve not met", color: "slate" };
    case "Live":
    default: {
      const hoursRemaining = (new Date(item.auctionEnd).getTime() - now.getTime()) / (1000 * 60 * 60);
      if (hoursRemaining <= 0) return { label: "Ended", color: "slate" };
      // Strictly-under-6h, matching AuctionCountdown's amber threshold exactly
      // (its `days * 24 + hours < 6` floor arithmetic is amber iff remaining < 6h).
      return hoursRemaining < 6
        ? { label: "Ending soon", color: "amber" }
        : { label: "Live", color: "primary" };
    }
  }
}

export function AuctionStatusBadge({ item, now = new Date() }: { item: StatusBadgeItem; now?: Date }) {
  const { label, color } = resolveStatus(item, now);
  return <Badge color={color}>{label}</Badge>;
}
