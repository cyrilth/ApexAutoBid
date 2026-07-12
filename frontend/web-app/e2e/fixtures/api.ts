import type { AuctionDetail, AuctionItem, AuctionStatus, SearchResult } from "@/types/auction";
import type { Bid } from "@/types/bid";

/**
 * Direct-to-Gateway helpers for Task 15's specs (Batch A). These call the SAME Gateway the
 * Next.js app itself talks to server-side (mirrors `lib/search-service.ts` /
 * `lib/auction-service.ts` / `lib/bid-service.ts`'s identical `GATEWAY_URL` fallback) -- but
 * from the Playwright Node process, not through a page. Specs use these to discover real
 * seeded data (which auctions exist, their statuses, which have bid history) at run time
 * instead of hardcoding auction ids/counts, so a spec stays correct across any fresh seed
 * (Docs/Tasks.md Phase 7 Task 15's "verify against the live stack" instruction) even though
 * today's `DbInitializer`s happen to assign fixed literal Guids.
 */
export const GATEWAY_URL = process.env.GATEWAY_URL ?? "http://localhost:6001";

/**
 * The exact three params `lib/auction-search-params.ts`'s `buildAuctionHref`/`searchAuctions`
 * always send for an unfiltered listing page load (`AuctionQuery`'s own defaults) -- passing
 * these explicitly (rather than omitting them) keeps a fixture-driven expectation calculation
 * byte-for-byte aligned with what the app itself requests, instead of relying on the backend's
 * independently-implemented "omitted param" defaults to happen to agree.
 */
export const DEFAULT_QUERY_PARAMS = {
  orderBy: "endingSoon",
  filterBy: "all",
  pageNumber: "1",
} as const;

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${GATEWAY_URL}${path}`);
  if (!res.ok) {
    throw new Error(`GET ${path} failed with status ${res.status}`);
  }
  return (await res.json()) as T;
}

/** `GET api/search` -- same endpoint/response shape `lib/search-service.ts` fetches server-side. */
export async function fetchSearch(params: Record<string, string> = {}): Promise<SearchResult> {
  const qs = new URLSearchParams(params);
  return getJson<SearchResult>(`/api/search?${qs.toString()}`);
}

/**
 * Every seeded auction in one request. `pageSize=50` is `ItemSearchDefaults.MaxPageSize`
 * (SearchService.Domain.Models.ItemSearchDefaults) -- comfortably above the ~10-item seed set,
 * so this always returns the full set in a single page regardless of how many exist.
 */
export async function fetchAllAuctions(): Promise<AuctionItem[]> {
  const result = await fetchSearch({ ...DEFAULT_QUERY_PARAMS, pageSize: "50" });
  return result.results;
}

/** `GET api/bids/{auctionId}` -- same endpoint `lib/bid-service.ts` fetches server-side. */
export async function fetchBids(auctionId: string): Promise<Bid[]> {
  return getJson<Bid[]>(`/api/bids/${auctionId}`);
}

/** `GET api/auctions/{id}` -- same endpoint `lib/auction-service.ts` fetches server-side. */
export async function fetchAuctionDetail(auctionId: string): Promise<AuctionDetail> {
  return getJson<AuctionDetail>(`/api/auctions/${auctionId}`);
}

/**
 * First seeded auction with the given status, straight from the Search Service's own index --
 * throws (failing the calling spec loudly, not silently skipping) if the current seed data has
 * none, so a spec never "passes" against an empty/mismatched fixture.
 */
export async function findAuctionByStatus(status: AuctionStatus): Promise<AuctionItem> {
  const auctions = await fetchAllAuctions();
  const match = auctions.find((auction) => auction.status === status);
  if (!match) {
    throw new Error(`No seeded auction with status "${status}" found -- has the seed data changed?`);
  }
  return match;
}

/**
 * First seeded auction (optionally restricted to one status) that already has bid history, for
 * specs asserting the detail page's bid list actually renders real seeded bids rather than the
 * empty state.
 */
export async function findAuctionWithBids(
  status?: AuctionStatus,
): Promise<{ auction: AuctionItem; bids: Bid[] }> {
  const auctions = await fetchAllAuctions();
  const candidates = status ? auctions.filter((auction) => auction.status === status) : auctions;

  for (const candidate of candidates) {
    const bids = await fetchBids(candidate.id);
    if (bids.length > 0) {
      return { auction: candidate, bids };
    }
  }

  throw new Error(
    `No seeded auction${status ? ` with status "${status}"` : ""} with bid history found -- has the seed data changed?`,
  );
}
