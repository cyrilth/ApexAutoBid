/** Mirrors SearchService.Domain.Enums.Status (Requirements §3.1) -- serialized as a string. */
export type AuctionStatus = "Live" | "Finished" | "ReserveNotMet" | "Cancelled";

/**
 * Mirrors SearchService.Application.DTOs.ItemDto field-for-field. ASP.NET
 * Core's default System.Text.Json policy serializes to camelCase, which is
 * what these property names already are.
 */
export interface AuctionItem {
  id: string;
  createdAt: string;
  updatedAt: string;
  auctionEnd: string;
  seller: string;
  winner?: string | null;

  make: string;
  model: string;
  year: number;
  color: string;
  mileage: number;

  imageUrl: string;
  thumbnailUrl?: string | null;

  status: AuctionStatus;

  reservePrice: number;
  soldAmount?: number | null;
  currentHighBid?: number | null;
}

/** Mirrors SearchService.Application.DTOs.SearchResultDto -- the `GET api/search` response shape. */
export interface SearchResult {
  results: AuctionItem[];
  totalCount: number;
  pageCount: number;
}

/**
 * Mirrors AuctionService.Application.DTOs.ImageDto -- a single entry in an
 * auction's full image gallery (Requirements §3.1). `sortOrder = 0` is the
 * primary image; the backend already returns the list pre-sorted ascending.
 */
export interface AuctionImage {
  url: string;
  thumbnailUrl?: string | null;
  sortOrder: number;
}

/**
 * Mirrors AuctionService.Application.DTOs.AuctionDto -- the full auction
 * record served by `GET api/auctions/{id}` (Auction Service, via the
 * Gateway). Distinct from `AuctionItem` above (the flattened search-index
 * projection used by the listing page): this shape carries the complete
 * ordered `images` gallery instead of a single primary image/thumbnail pair.
 */
export interface Auction {
  id: string;
  createdAt: string;
  updatedAt: string;
  auctionEnd: string;
  seller: string;
  winner?: string | null;

  make: string;
  model: string;
  year: number;
  color: string;
  mileage: number;

  reservePrice: number;
  soldAmount?: number | null;
  currentHighBid?: number | null;

  status: AuctionStatus;

  images: AuctionImage[];
}

/**
 * Mirrors AuctionService.Application.DTOs.AuctionDetailDto -- returned only
 * by `GET api/auctions/{id}`. `sellerEmail`/`winnerEmail` are present in the
 * JSON payload only when the backend's post-sale contact-exchange rules
 * (Requirements §3.1) grant the caller that specific field -- the backend
 * uses `JsonIgnoreCondition.WhenWritingNull`, so these keys are *absent*
 * (not merely `null`) for every other caller, including anonymous ones.
 * "Field present" is therefore the entire authorization check on the
 * frontend -- see `components/PostSaleContact.tsx`.
 */
export interface AuctionDetail extends Auction {
  sellerEmail?: string;
  winnerEmail?: string;
}
