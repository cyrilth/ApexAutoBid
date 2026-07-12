/**
 * Request-side shapes for the auction create/edit form (Phase 7 Task 6).
 * These mirror the *input* DTOs the Auction Service actually accepts --
 * distinct from `types/auction.ts`'s `Auction`/`AuctionDetail` (the
 * *response* shapes `GET api/auctions/{id}` returns).
 */

/**
 * Mirrors AuctionService.Application.DTOs.ImageDto -- a single gallery entry
 * as submitted on create/update. `sortOrder = 0` is the primary image; the
 * form always sends the array pre-sorted (index order = sort order), so this
 * never needs to be anything but the entry's position in the array.
 */
export interface AuctionImageInput {
  url: string;
  thumbnailUrl?: string;
  sortOrder: number;
}

/**
 * Mirrors AuctionService.Application.DTOs.CreateAuctionDto -- the full
 * `POST api/auctions` body. `images` requires 1-10 entries (enforced
 * client-side in `components/AuctionImageManager.tsx` and again server-side).
 */
export interface CreateAuctionPayload {
  make: string;
  model: string;
  color: string;
  mileage: number;
  year: number;
  reservePrice: number;
  images: AuctionImageInput[];
  /** ISO 8601 UTC instant -- must satisfy the platform's min/max auction duration. */
  auctionEnd: string;
}

/**
 * Mirrors AuctionService.Application.DTOs.UpdateAuctionDto -- the
 * `PUT api/auctions/{id}` body. Every field is optional (only non-null
 * values are applied); notably there is **no** `auctionEnd` -- regular
 * sellers can't change it once set (only admins can, via a separate
 * Phase 11 endpoint), so the edit form never sends one.
 */
export interface UpdateAuctionPayload {
  make?: string;
  model?: string;
  color?: string;
  mileage?: number;
  year?: number;
  images?: AuctionImageInput[];
}
