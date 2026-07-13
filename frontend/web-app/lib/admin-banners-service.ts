import "server-only";
import { getAccessToken } from "@/lib/auth-token";
import type { Banner } from "@/types/admin";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * `GET api/banners?scope=&auctionId=` via the Gateway (AuctionService's public
 * BannersController -- anonymous). Returns only currently-active banners (Task 8.6). Called
 * from the home page and the auction detail page for the initial (pre-SignalR) render.
 */
export async function getActiveBanners(params: { scope?: string; auctionId?: string } = {}): Promise<Banner[]> {
  const qs = new URLSearchParams();
  if (params.scope) qs.set("scope", params.scope);
  if (params.auctionId) qs.set("auctionId", params.auctionId);

  const res = await fetch(`${GATEWAY_URL}/api/banners?${qs.toString()}`, {
    // Banners are admin-published and can start/stop being active at any moment -- never serve
    // a stale cached response.
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Banners request failed with status ${res.status}`);
  }

  return (await res.json()) as Banner[];
}

/** `GET api/admin/banners` -- every banner regardless of its active window (Task 8.5's admin table). */
export async function getAllBanners(): Promise<Banner[]> {
  const token = await getAccessToken();

  const res = await fetch(`${GATEWAY_URL}/api/admin/banners`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Admin banners request failed with status ${res.status}`);
  }

  return (await res.json()) as Banner[];
}
