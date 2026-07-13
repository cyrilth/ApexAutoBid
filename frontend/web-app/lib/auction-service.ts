import "server-only";
import { getAccessToken } from "@/lib/auth-token";
import type { AuctionDetail } from "@/types/auction";
import type { DurationLimits } from "@/types/admin";

/**
 * Dev default matches backend/GatewayService/Properties/launchSettings.json's
 * "http" profile. Override via GATEWAY_URL for other environments (Docker
 * Compose / Kubernetes service DNS, production hostname). Mirrors
 * lib/search-service.ts's identical constant.
 */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * `GET api/auctions/{id}` via the Gateway (AuctionsController.GetAuctionById
 * -- anonymous overall, but reads the caller's identity WHEN present to
 * decide post-sale contact-field exposure, Requirements §3.1). Attaches the
 * signed-in user's bearer token when a session exists so the backend can
 * populate `sellerEmail`/`winnerEmail` for the seller/winner; omits it
 * entirely for anonymous visitors -- the backend's own redaction logic
 * (`AuctionDetailDto`) is the sole authority on who actually gets those
 * fields either way.
 *
 * Called from both the detail page and its `generateMetadata` (Task 5.6) --
 * same URL and same (session-derived) options each render, so Next's
 * automatic fetch memoization collapses them into a single network request
 * per page load.
 *
 * Returns `null` on a 404 (unknown auction id) so callers can `notFound()`;
 * any other non-OK status throws.
 */
export async function getAuctionById(id: string): Promise<AuctionDetail | null> {
  const token = await getAccessToken();

  const res = await fetch(`${GATEWAY_URL}/api/auctions/${id}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    // Bids/status/post-sale contact fields all change in near real-time and
    // are caller-specific (post-sale emails) -- never a shared cached response.
    cache: "no-store",
  });

  if (res.status === 404) {
    return null;
  }

  if (!res.ok) {
    throw new Error(`Auction request failed with status ${res.status}`);
  }

  return (await res.json()) as AuctionDetail;
}

/**
 * `GET api/auctions/duration-limits` via the Gateway (anonymous, Phase 11 Task 3.8) -- the
 * platform's currently-effective auction duration bounds, used to constrain the create form's
 * `AuctionEnd` datepicker (`components/AuctionForm.tsx`).
 */
export async function getDurationLimits(): Promise<DurationLimits> {
  const res = await fetch(`${GATEWAY_URL}/api/auctions/duration-limits`, {
    // Admin-configurable (Task 8.7) -- never serve a stale cached response.
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Duration limits request failed with status ${res.status}`);
  }

  return (await res.json()) as DurationLimits;
}
