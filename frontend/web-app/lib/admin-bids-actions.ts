"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

// ── DELETE api/admin/bids/{id} ───────────────────────────────────────────────
//
// Removes the bid; BiddingService recalculates the auction's current high bid from its
// remaining accepted bids and publishes BidRemoved so Auction/Search refresh (Task 8.4).

export async function removeBid(bidId: string): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("remove this bid");

  const res = await fetch(`${GATEWAY_URL}/api/admin/bids/${bidId}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "Bid not found", detail: `No bid with id '${bidId}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not remove the bid");
  }

  return { success: true, data: { id: bidId } };
}
