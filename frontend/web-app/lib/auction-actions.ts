"use server";

import {
  type ActionResult,
  problemDetailsError,
  requireBearerToken,
  signInRequiredError,
} from "@/lib/action-result";
import type { CreateAuctionPayload, UpdateAuctionPayload } from "@/types/auction-form";

/** Mirrors lib/auction-service.ts's identical constant/fallback. */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

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
