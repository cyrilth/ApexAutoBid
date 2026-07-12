import { Badge } from "flowbite-react";
import type { BidStatus } from "@/types/bid";

const BID_STATUS_LABELS: Record<BidStatus, string> = {
  Accepted: "Accepted",
  AcceptedBelowReserve: "Below reserve",
  TooLow: "Too low",
  Finished: "After close",
};

const BID_STATUS_COLORS: Record<BidStatus, "sold" | "amber" | "failure" | "slate"> = {
  // Green, same treatment as a Sold auction badge (Docs/DesignGuide.md §2).
  Accepted: "sold",
  AcceptedBelowReserve: "amber",
  TooLow: "failure",
  // Placed after AuctionEnd -- doesn't count towards the sale (Requirements
  // §3.3); not one of the guide's three explicit bid-result colors, so this
  // reuses the neutral "not counted" slate treatment (same as ReserveNotMet).
  Finished: "slate",
};

/** Bid outcome badge (Task 5.3) -- colors/labels per Docs/DesignGuide.md §2. */
export function BidStatusBadge({ status }: { status: BidStatus }) {
  return <Badge color={BID_STATUS_COLORS[status]}>{BID_STATUS_LABELS[status]}</Badge>;
}
