"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";
import type { Banner, BannerPayload } from "@/types/admin";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

// ── POST api/admin/banners ───────────────────────────────────────────────────

export async function createBanner(payload: BannerPayload): Promise<ActionResult<Banner>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("create a banner");

  const res = await fetch(`${GATEWAY_URL}/api/admin/banners`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (!res.ok) {
    return problemDetailsError(res, "Could not create the banner");
  }

  return { success: true, data: (await res.json()) as Banner };
}

// ── PUT api/admin/banners/{id} ───────────────────────────────────────────────
//
// Full replace (UpdateBannerDto's own remarks) -- returns a bare 200 Ok() with no body.

export async function updateBanner(id: string, payload: BannerPayload): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("update this banner");

  const res = await fetch(`${GATEWAY_URL}/api/admin/banners/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "Banner not found", detail: `No banner with id '${id}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not update the banner");
  }

  return { success: true, data: { id } };
}

// ── DELETE api/admin/banners/{id} ────────────────────────────────────────────

export async function deleteBanner(id: string): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("delete this banner");

  const res = await fetch(`${GATEWAY_URL}/api/admin/banners/${id}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "Banner not found", detail: `No banner with id '${id}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not delete the banner");
  }

  return { success: true, data: { id } };
}
