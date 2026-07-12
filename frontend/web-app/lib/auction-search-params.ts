/**
 * URL <-> backend query-param mapping for the auction listing page
 * (`GET api/search` via the Gateway -- SearchService.Application.DTOs.SearchParamsDto,
 * Docs/Requirements.md §3.2). The URL is the single source of truth for
 * listing state (shareable/bookmarkable, back-button friendly) -- nothing
 * here is duplicated into client-only state.
 */

/** Raw string values SearchAppService accepts for `orderBy` (case-insensitive on the backend). */
export const ORDER_BY_VALUES = ["endingSoon", "make", "new"] as const;
export type OrderBy = (typeof ORDER_BY_VALUES)[number];

/** Raw string values SearchAppService accepts for `filterBy` (case-insensitive on the backend). */
export const FILTER_BY_VALUES = ["all", "live", "endingSoon", "finished"] as const;
export type FilterBy = (typeof FILTER_BY_VALUES)[number];

export interface AuctionQuery {
  searchTerm: string;
  seller: string;
  winner: string;
  orderBy: OrderBy;
  filterBy: FilterBy;
  /** 1-based, matches SearchParamsDto.PageNumber. */
  pageNumber: number;
}

type RawSearchParams = Record<string, string | string[] | undefined>;

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

/**
 * Parses (and validates/defaults) the Page's raw `searchParams` prop into an
 * `AuctionQuery`. Unknown/invalid values fall back to the same defaults
 * `SearchAppService` applies when a param is omitted, so the UI's "no filter
 * selected" state always matches what the backend would do anyway.
 */
export function parseAuctionSearchParams(raw: RawSearchParams): AuctionQuery {
  const orderByRaw = first(raw.orderBy);
  const orderBy = (ORDER_BY_VALUES as readonly string[]).includes(orderByRaw ?? "")
    ? (orderByRaw as OrderBy)
    : "endingSoon";

  const filterByRaw = first(raw.filterBy);
  const filterBy = (FILTER_BY_VALUES as readonly string[]).includes(filterByRaw ?? "")
    ? (filterByRaw as FilterBy)
    : "all";

  const pageNumberRaw = Number(first(raw.pageNumber));
  const pageNumber = Number.isInteger(pageNumberRaw) && pageNumberRaw > 0 ? pageNumberRaw : 1;

  return {
    searchTerm: first(raw.searchTerm)?.trim() ?? "",
    seller: first(raw.seller)?.trim() ?? "",
    winner: first(raw.winner)?.trim() ?? "",
    orderBy,
    filterBy,
    pageNumber,
  };
}

/**
 * Builds the listing page's "/" href for a given query (merging in any
 * overrides), omitting default values so URLs stay tidy/shareable.
 */
export function buildAuctionHref(query: AuctionQuery, overrides: Partial<AuctionQuery> = {}): string {
  const merged = { ...query, ...overrides };
  const params = new URLSearchParams();

  if (merged.searchTerm) params.set("searchTerm", merged.searchTerm);
  if (merged.seller) params.set("seller", merged.seller);
  if (merged.winner) params.set("winner", merged.winner);
  if (merged.orderBy !== "endingSoon") params.set("orderBy", merged.orderBy);
  if (merged.filterBy !== "all") params.set("filterBy", merged.filterBy);
  if (merged.pageNumber > 1) params.set("pageNumber", String(merged.pageNumber));

  const qs = params.toString();
  return qs ? `/?${qs}` : "/";
}
