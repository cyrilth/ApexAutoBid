import "server-only";
import type { SearchResult } from "@/types/auction";
import type { AuctionQuery } from "@/lib/auction-search-params";

/**
 * Dev default matches backend/GatewayService/Properties/launchSettings.json's
 * "http" profile. Override via GATEWAY_URL for other environments (Docker
 * Compose / Kubernetes service DNS, production hostname).
 */
const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * `GET api/search` via the Gateway (SearchService.API.Controllers.SearchController --
 * anonymous, no bearer token needed). Query params map 1:1 onto
 * SearchService.Application.DTOs.SearchParamsDto's property names (ASP.NET
 * Core's complex-type query binder matches case-insensitively) -- see
 * `AuctionQuery` in lib/auction-search-params.ts for the exact shape.
 *
 * Server-only: called from the listing page's Server Components, never the
 * browser (see Task 4.1's fetch-from-Search-Service requirement).
 */
export async function searchAuctions(query: AuctionQuery): Promise<SearchResult> {
  const params = new URLSearchParams();
  if (query.searchTerm) params.set("searchTerm", query.searchTerm);
  if (query.seller) params.set("seller", query.seller);
  if (query.winner) params.set("winner", query.winner);
  params.set("orderBy", query.orderBy);
  params.set("filterBy", query.filterBy);
  params.set("pageNumber", String(query.pageNumber));

  const res = await fetch(`${GATEWAY_URL}/api/search?${params.toString()}`, {
    // Auction data changes in near real-time (new bids, new listings) -- never
    // serve a stale cached response for the listing page.
    cache: "no-store",
  });

  if (!res.ok) {
    throw new Error(`Search request failed with status ${res.status}`);
  }

  return (await res.json()) as SearchResult;
}
