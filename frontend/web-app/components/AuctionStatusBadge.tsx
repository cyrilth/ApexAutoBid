import { Badge } from "flowbite-react";
import type { AuctionItem } from "@/types/auction";

type StatusBadgeColor = "primary" | "amber" | "slate" | "sold" | "failure";

/**
 * Status badge coloring/labels per Docs/DesignGuide.md §2 (badges always
 * carry a text label -- status is never conveyed by color alone). "Ending
 * soon" is derived (Live with < 6h left), not a distinct backend status;
 * "Sold" is a Finished auction -- this domain's `Finished` status always
 * implies a winner/sale (see Requirements.md §3.1's Status enum and seed
 * data), `ReserveNotMet` is the "ended without selling" case.
 */
function resolveStatus(item: AuctionItem, now: Date): { label: string; color: StatusBadgeColor } {
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
      return hoursRemaining <= 6
        ? { label: "Ending soon", color: "amber" }
        : { label: "Live", color: "primary" };
    }
  }
}

export function AuctionStatusBadge({ item, now = new Date() }: { item: AuctionItem; now?: Date }) {
  const { label, color } = resolveStatus(item, now);
  return <Badge color={color}>{label}</Badge>;
}
