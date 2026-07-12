"use server";

import { getAccessToken } from "@/lib/auth-token";
import type { Bid } from "@/types/bid";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * Same envelope as lib/auction-actions.ts's `ActionResult<T>` -- duplicated
 * here (rather than imported) because that file is itself a "use server"
 * module: every export from one has to be a valid Server Action, and a
 * plain helper type/function isn't one. Keep the shape identical if it ever
 * changes there.
 */
export type ActionResult<T> =
  | { success: true; data: T }
  | { success: false; error: { title: string; detail?: string; status: number } };

/** Mirrors lib/auction-actions.ts's identical helper. */
async function requireBearerToken(): Promise<string | null> {
  const token = await getAccessToken();
  return token ?? null;
}

/** Mirrors lib/auction-actions.ts's identical helper. */
function signInRequiredError(action: string): ActionResult<never> {
  return {
    success: false,
    error: {
      title: "Sign-in required",
      detail: `Please sign in again to ${action}.`,
      status: 401,
    },
  };
}

/** Mirrors lib/auction-actions.ts's identical helper -- see its remarks on the bare-403 case. */
async function problemDetailsError(res: Response, fallbackTitle: string): Promise<ActionResult<never>> {
  let title = fallbackTitle;
  let detail: string | undefined;

  try {
    const body = (await res.json()) as { title?: string; detail?: string };
    if (typeof body.title === "string") title = body.title;
    if (typeof body.detail === "string") detail = body.detail;
  } catch {
    if (res.status === 403) {
      title = "Not allowed";
      detail = "You don't have permission to do that.";
    }
  }

  return { success: false, error: { title, detail, status: res.status } };
}

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
