"use server";

import { getAccessToken } from "@/lib/auth-token";
import type { CreateAuctionPayload, UpdateAuctionPayload } from "@/types/auction-form";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * Typed result envelope for every action below -- never throws a raw fetch
 * error at the client (Docs/Requirements.md §13.2). Callers branch on
 * `success` and render `error.title`/`error.detail` inline near the submit
 * button (the toast system is a later task, see the Task 6 brief).
 */
export type ActionResult<T> =
  | { success: true; data: T }
  | { success: false; error: { title: string; detail?: string; status: number } };

/** Mirrors AuctionService.Application.DTOs.UploadUrlResponse. */
interface UploadUrlResult {
  key: string;
  uploadUrl: string;
  objectUrl: string;
  expiresAt: string;
}

/** Mirrors AuctionService.Application.DTOs.ThumbnailResponse. */
interface ThumbnailResult {
  thumbnailUrl: string;
}

/**
 * Every mutating action needs the signed-in user's bearer token
 * (lib/auth-token.ts is server-only, so this file -- itself server-only via
 * "use server" -- is exactly where it's read). `undefined` means the caller
 * is signed out (or their refresh failed); every action below treats that
 * the same way the backend would treat a missing token: reject before
 * calling the Gateway at all.
 */
async function requireBearerToken(): Promise<string | null> {
  const token = await getAccessToken();
  return token ?? null;
}

function signInRequiredError(action: string): ActionResult<never> {
  return {
    success: false,
    error: {
      title: "Sign-in required",
      detail: `Please sign in again to ${action}.`,
      status: 401,
    },
  };
}

/**
 * Parses a ProblemDetails body (RFC 7807, Docs/Requirements.md §13.1) when
 * present. Some 403s from [Authorize(Policy = "EmailVerified")] are a bare
 * framework `Forbid()` with no body at all (see AuctionsController's
 * remarks) -- `res.json()` throws on those, so this falls back to a generic,
 * status-appropriate title/detail rather than surfacing a parse error.
 */
async function problemDetailsError(res: Response, fallbackTitle: string): Promise<ActionResult<never>> {
  let title = fallbackTitle;
  let detail: string | undefined;

  try {
    const body = (await res.json()) as { title?: string; detail?: string };
    if (typeof body.title === "string") title = body.title;
    if (typeof body.detail === "string") detail = body.detail;
  } catch {
    if (res.status === 403) {
      title = "Not allowed";
      detail = "You don't have permission to do that.";
    }
  }

  return { success: false, error: { title, detail, status: res.status } };
}

// ── 6.3  POST api/auctions/upload-url ───────────────────────────────────────
//
// Requests a presigned PUT URL for one file. The caller (AuctionImageManager)
// then PUTs the file's bytes straight to `uploadUrl` from the browser -- this
// action never sees the file itself (Requirements §3.1's direct-to-storage
// design; see the Task 6 brief's architectural constraint).

export async function requestUploadUrl(
  contentType: string,
  sizeBytes: number
): Promise<ActionResult<UploadUrlResult>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("upload images");

  const res = await fetch(`${GATEWAY_URL}/api/auctions/upload-url`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ contentType, sizeBytes }),
    cache: "no-store",
  });

  if (!res.ok) {
    return problemDetailsError(res, "Could not start the upload");
  }

  return { success: true, data: (await res.json()) as UploadUrlResult };
}

// ── 6.4  POST api/auctions/thumbnail ────────────────────────────────────────

export async function generateThumbnail(key: string): Promise<ActionResult<ThumbnailResult>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("generate a thumbnail");

  const res = await fetch(`${GATEWAY_URL}/api/auctions/thumbnail`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ key }),
    cache: "no-store",
  });

  if (!res.ok) {
    return problemDetailsError(res, "Could not generate a thumbnail");
  }

  return { success: true, data: (await res.json()) as ThumbnailResult };
}

// ── POST api/auctions ────────────────────────────────────────────────────────

export async function createAuction(
  payload: CreateAuctionPayload
): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("create an auction");

  const res = await fetch(`${GATEWAY_URL}/api/auctions`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (res.status === 403) {
    // The "EmailVerified" policy short-circuits to a bare Forbid() before the
    // action body runs -- no ProblemDetails to parse (AuctionsController's
    // CreateAuction remarks), so this is spelled out explicitly here.
    return {
      success: false,
      error: {
        title: "Email verification required",
        detail: "Verify your email address before creating an auction.",
        status: 403,
      },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not create the auction");
  }

  const created = (await res.json()) as { id: string };
  return { success: true, data: { id: created.id } };
}

// ── PUT api/auctions/{id} ────────────────────────────────────────────────────

export async function updateAuction(
  id: string,
  payload: UpdateAuctionPayload
): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("update this auction");

  const res = await fetch(`${GATEWAY_URL}/api/auctions/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(payload),
    cache: "no-store",
  });

  if (res.status === 403) {
    // Forbid() -- ownership check failed (UpdateAuction's remarks); no body.
    return {
      success: false,
      error: {
        title: "Not allowed",
        detail: "You can only edit your own auctions.",
        status: 403,
      },
    };
  }

  if (res.status === 404) {
    return {
      success: false,
      error: {
        title: "Auction not found",
        detail: `No auction with id '${id}' was found.`,
        status: 404,
      },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not update the auction");
  }

  // UpdateAuction returns a bare 200 Ok() with no body -- nothing to parse.
  return { success: true, data: { id } };
}

// ── DELETE api/auctions/{id} ─────────────────────────────────────────────────
//
// Deletes an auction (Task 7). Ownership is enforced by the backend
// (AuctionAppService.DeleteAuctionAsync compares auction.Seller to the caller's
// username -- unlike Update, there is no admin bypass there today, so an admin
// deleting someone else's auction still gets Forbidden even though the detail
// page's `canEdit` gate shows them the button; the backend result is what
// actually decides, this action just surfaces whatever it returns), same as
// updateAuction above.

export async function deleteAuction(id: string): Promise<ActionResult<{ id: string }>> {
  const token = await requireBearerToken();
  if (!token) return signInRequiredError("delete this auction");

  const res = await fetch(`${GATEWAY_URL}/api/auctions/${id}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 403) {
    // Forbid() -- either the "EmailVerified" policy or the ownership check
    // failed (DeleteAuction's remarks); both are a bare 403 with no body.
    return {
      success: false,
      error: {
        title: "Not allowed",
        detail: "You can only delete your own auctions.",
        status: 403,
      },
    };
  }

  if (res.status === 404) {
    return {
      success: false,
      error: {
        title: "Auction not found",
        detail: `No auction with id '${id}' was found.`,
        status: 404,
      },
    };
  }

  if (!res.ok) {
    return problemDetailsError(res, "Could not delete the auction");
  }

  // DeleteAuction returns a bare 200 Ok() with no body -- nothing to parse.
  return { success: true, data: { id } };
}
