import "server-only";
import { getAccessToken } from "@/lib/auth-token";
import type { DurationSettings } from "@/types/admin";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/** `GET api/admin/settings/duration` -- the Settings page (Task 8.7). */
export async function getDurationSettings(): Promise<DurationSettings> {
  const token = await getAccessToken();

  const res = await fetch(`${GATEWAY_URL}/api/admin/settings/duration`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Admin duration settings request failed with status ${res.status}`);
  }

  return (await res.json()) as DurationSettings;
}
