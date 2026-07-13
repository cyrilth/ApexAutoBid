import "server-only";
import { getAccessToken } from "@/lib/auth-token";
import type { AdminUserListResult, AdminUserStats } from "@/types/admin";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * `GET api/admin/users?search=&page=&pageSize=` via the Gateway (IdentityService's
 * AdminUsersController -- "AdminOnly" policy, 401 anonymous / 403 non-admin). Read from the
 * Users page's Server Component, so the bearer token comes straight from the session (Task 8.3).
 */
export async function listUsers(params: {
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<AdminUserListResult> {
  const token = await getAccessToken();

  const qs = new URLSearchParams();
  if (params.search) qs.set("search", params.search);
  qs.set("page", String(params.page ?? 1));
  qs.set("pageSize", String(params.pageSize ?? 20));

  const res = await fetch(`${GATEWAY_URL}/api/admin/users?${qs.toString()}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Admin users request failed with status ${res.status}`);
  }

  return (await res.json()) as AdminUserListResult;
}

/** `GET api/admin/users/stats` -- dashboard card (Task 8.2). */
export async function getUserStats(): Promise<AdminUserStats> {
  const token = await getAccessToken();

  const res = await fetch(`${GATEWAY_URL}/api/admin/users/stats`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Admin user stats request failed with status ${res.status}`);
  }

  return (await res.json()) as AdminUserStats;
}
