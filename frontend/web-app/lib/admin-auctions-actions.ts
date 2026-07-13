"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

function notFoundError(auctionId: string): ActionResult<never> {
  return {
    success: false,
    error: { title: "Auction not found", detail: `No auction with id '${auctionId}' was found.`, status: 404 },
  };
}

// ── POST api/admin/auctions/{id}/end ─────────────────────────────────────────
//
// Sets AuctionEnd = UtcNow immediately (Task 8.4's "End now" action); Bidding's background
// finalization job takes it from there.

export async function endAuctionNow(auctionId: string): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("end this auction");

  const res = await fetch(`${GATEWAY_URL}/api/admin/auctions/${auctionId}/end`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 404) return notFoundError(auctionId);
  if (!res.ok) return problemDetailsError(res, "Could not end the auction");

  return { success: true, data: { id: auctionId } };
}

// ── POST api/admin/auctions/{id}/cancel ──────────────────────────────────────

export async function cancelAuction(auctionId: string): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("cancel this auction");

  const res = await fetch(`${GATEWAY_URL}/api/admin/auctions/${auctionId}/cancel`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 404) return notFoundError(auctionId);
  if (!res.ok) return problemDetailsError(res, "Could not cancel the auction");

  return { success: true, data: { id: auctionId } };
}
