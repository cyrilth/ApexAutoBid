import "server-only";
import { getAccessToken } from "@/lib/auth-token";
import type { AdminAuctionStats } from "@/types/admin";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/** `GET api/admin/auctions/stats` -- dashboard card (Task 8.2). */
export async function getAuctionStats(): Promise<AdminAuctionStats> {
  const token = await getAccessToken();

  const res = await fetch(`${GATEWAY_URL}/api/admin/auctions/stats`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Admin auction stats request failed with status ${res.status}`);
  }

  return (await res.json()) as AdminAuctionStats;
}
