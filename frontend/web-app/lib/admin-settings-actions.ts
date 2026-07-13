"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";
import type { DurationSettings, DurationSettingsPayload } from "@/types/admin";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

// ── PUT api/admin/settings/duration ──────────────────────────────────────────
//
// 400 comes back as a proper `ValidationProblem(ModelState)` (a full ValidationProblemDetails,
// title "One or more validation errors occurred.") rather than a plain ProblemDetails --
// `problemDetailsError` still handles it fine since it only reads `title`/`detail`.

export async function updateDurationSettings(
  payload: DurationSettingsPayload
): Promise<ActionResult<DurationSettings>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("update the platform's auction duration settings");

  const res = await fetch(`${GATEWAY_URL}/api/admin/settings/duration`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (!res.ok) {
    return problemDetailsError(res, "Could not update the duration settings");
  }

  return { success: true, data: (await res.json()) as DurationSettings };
}
