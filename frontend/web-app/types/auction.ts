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
