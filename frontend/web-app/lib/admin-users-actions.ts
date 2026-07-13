"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";
import type {
  CreateUserPayload,
  CreateUserResult,
  LockUserResult,
  ResetPasswordPayload,
  ResetPasswordResult,
  RolesUpdateResult,
} from "@/types/admin";

/** Mirrors lib/auction-actions.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

// ── POST api/admin/users ─────────────────────────────────────────────────────

export async function createUser(payload: CreateUserPayload): Promise<ActionResult<CreateUserResult>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("create a user");

  const res = await fetch(`${GATEWAY_URL}/api/admin/users`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (!res.ok) {
    return problemDetailsError(res, "Could not create the user");
  }

  return { success: true, data: (await res.json()) as CreateUserResult };
}

// ── POST api/admin/users/{id}/reset-password ─────────────────────────────────

export async function resetUserPassword(
  id: string,
  payload: ResetPasswordPayload
): Promise<ActionResult<ResetPasswordResult>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("reset this user's password");

  const res = await fetch(`${GATEWAY_URL}/api/admin/users/${id}/reset-password`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "User not found", detail: `No user with id '${id}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not reset the password");
  }

  return { success: true, data: (await res.json()) as ResetPasswordResult };
}

// ── POST api/admin/users/{id}/resend-confirmation ────────────────────────────

export async function resendUserConfirmation(id: string): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("resend the confirmation email");

  const res = await fetch(`${GATEWAY_URL}/api/admin/users/${id}/resend-confirmation`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "User not found", detail: `No user with id '${id}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not resend the confirmation email");
  }

  // 204 No Content -- nothing to parse.
  return { success: true, data: { id } };
}

// ── PUT api/admin/users/{id}/roles ───────────────────────────────────────────

export async function updateUserRoles(id: string, roles: string[]): Promise<ActionResult<RolesUpdateResult>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("update this user's roles");

  const res = await fetch(`${GATEWAY_URL}/api/admin/users/${id}/roles`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ roles }),
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "User not found", detail: `No user with id '${id}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not update roles");
  }

  return { success: true, data: (await res.json()) as RolesUpdateResult };
}

// ── PUT api/admin/users/{id}/lock ────────────────────────────────────────────

export async function setUserLock(
  id: string,
  locked: boolean,
  lockoutEnd?: string
): Promise<ActionResult<LockUserResult>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("lock/unlock this user");

  const res = await fetch(`${GATEWAY_URL}/api/admin/users/${id}/lock`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ locked, lockoutEnd }),
    cache: "no-store",
  });

  if (res.status === 404) {
    return {
      success: false,
      error: { title: "User not found", detail: `No user with id '${id}' was found.`, status: 404 },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not update the account's lock status");
  }

  return { success: true, data: (await res.json()) as LockUserResult };
}
