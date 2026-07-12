"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";
import type { Bid } from "@/types/bid";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

// ── POST api/bids ────────────────────────────────────────────────────────
//
// Places a bid (Task 8.1, Requirements §3.3). Every outcome the backend can
// produce for a well-formed request -- Accepted / AcceptedBelowReserve /
// TooLow / Finished -- comes back as a 200 + BidDto, NOT an ActionResult
// failure (BidsController.PlaceBid's remarks: "none of these are HTTP
// errors"). Callers branch on `data.bidStatus` for those; this only becomes
// `success: false` for a genuine HTTP failure: the auction not existing
// (404), the seller bidding on their own auction (400), or the
// "EmailVerified" policy's bare Forbid() (403, no body).

export async function placeBid(auctionId: string, amount: number): Promise<ActionResult<Bid>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("place a bid");

  const res = await fetch(`${GATEWAY_URL}/api/bids`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ auctionId, amount }),
    cache: "no-store",
  });

  if (res.status === 403) {
    // The "EmailVerified" policy short-circuits to a bare Forbid() before the
    // action body runs (BidsController.PlaceBid's remarks) -- no
    // ProblemDetails to parse, same convention as auction-actions.ts's
    // createAuction for its identically-guarded endpoint.
    return {
      success: false,
      error: {
        title: "Email verification required",
        detail: "Verify your email address before placing a bid.",
        status: 403,
      },
    };
  }

  if (!res.ok) {
    // 404 (auction not found) / 400 (seller bidding on own auction) are
    // genuine ProblemDetails bodies from BidsController.PlaceBid.
    return problemDetailsError(res, "Could not place the bid");
  }

  return { success: true, data: (await res.json()) as Bid };
}
