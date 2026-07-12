import "server-only";
import type { Bid } from "@/types/bid";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * `GET api/bids/{auctionId}` via the Gateway (BiddingService.API.Controllers.BidsController
 * -- anonymous, no bearer token needed, Requirements §3.3). The controller's
 * own contract already returns bids newest-first; `BidDto` never carries
 * `BidderEmail`.
 */
export async function getBidsForAuction(auctionId: string): Promise<Bid[]> {
  const res = await fetch(`${GATEWAY_URL}/api/bids/${auctionId}`, {
    // Bid history changes in near real-time -- never serve a stale cached response.
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Bids request failed with status ${res.status}`);
  }

  return (await res.json()) as Bid[];
}
