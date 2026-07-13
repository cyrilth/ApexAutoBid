import "server-only";
import { getAccessToken } from "@/lib/auth-token";
import type { AdminBidStats } from "@/types/admin";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/** `GET api/admin/bids/stats` -- dashboard card (Task 8.2). */
export async function getBidStats(): Promise<AdminBidStats> {
  const token = await getAccessToken();

  const res = await fetch(`${GATEWAY_URL}/api/admin/bids/stats`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Admin bid stats request failed with status ${res.status}`);
  }

  return (await res.json()) as AdminBidStats;
}
