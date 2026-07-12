"use client";

import type { FormEvent } from "react";
import { useRouter } from "next/navigation";
import { Button, Label, Select, TextInput } from "flowbite-react";
import {
  buildAuctionHref,
  FILTER_BY_VALUES,
  ORDER_BY_VALUES,
  type AuctionQuery,
  type FilterBy,
  type OrderBy,
} from "@/lib/auction-search-params";

const FILTER_LABELS: Record<FilterBy, string> = {
  all: "All",
  live: "Live",
  endingSoon: "Ending soon",
  finished: "Finished",
};

const ORDER_LABELS: Record<OrderBy, string> = {
  endingSoon: "Ending soon",
  make: "Make",
  new: "Newest",
};

/**
 * Search/filter/sort toolbar (Task 4.2/4.3). Reads its initial values from
 * the server-parsed `query` prop and drives navigation purely through the URL
 * (`router.push`) -- no client-only state -- so results stay
 * shareable/bookmarkable/back-button friendly. The caller (`app/page.tsx`)
 * keys this component on the current query string so the uncontrolled field
 * values (`defaultValue`) reset correctly on every navigation, including
 * Clear, pagination, and browser back/forward.
 */
export function AuctionToolbar({ query }: { query: AuctionQuery }) {
  const router = useRouter();

  const hasActiveFilters =
    Boolean(query.searchTerm || query.seller || query.winner) ||
    query.filterBy !== "all" ||
    query.orderBy !== "endingSoon";

  function applyForm(form: HTMLFormElement) {
    const data = new FormData(form);
    const next: AuctionQuery = {
      searchTerm: (data.get("searchTerm") as string)?.trim() ?? "",
      seller: (data.get("seller") as string)?.trim() ?? "",
      winner: (data.get("winner") as string)?.trim() ?? "",
      filterBy: (data.get("filterBy") as FilterBy) || "all",
      orderBy: (data.get("orderBy") as OrderBy) || "endingSoon",
      // Any filter/search/sort change starts back at page 1.
      pageNumber: 1,
    };
    router.push(buildAuctionHref(next));
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    applyForm(event.currentTarget);
  }

  return (
    <form
      onSubmit={handleSubmit}
      className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-4"
    >
      <div className="min-w-[200px] flex-1">
        <div className="mb-1">
          <Label htmlFor="searchTerm">Search</Label>
        </div>
        <TextInput
          id="searchTerm"
          name="searchTerm"
          placeholder="Make or model"
          defaultValue={query.searchTerm}
        />
      </div>

      <div className="w-36">
        <div className="mb-1">
          <Label htmlFor="seller">Seller</Label>
        </div>
        <TextInput id="seller" name="seller" placeholder="Username" defaultValue={query.seller} />
      </div>

      <div className="w-36">
        <div className="mb-1">
          <Label htmlFor="winner">Winner</Label>
        </div>
        <TextInput id="winner" name="winner" placeholder="Username" defaultValue={query.winner} />
      </div>

      <div className="w-40">
        <div className="mb-1">
          <Label htmlFor="filterBy">Status</Label>
        </div>
        <Select
          id="filterBy"
          name="filterBy"
          defaultValue={query.filterBy}
          onChange={(event) => event.currentTarget.form?.requestSubmit()}
        >
          {FILTER_BY_VALUES.map((value) => (
            <option key={value} value={value}>
              {FILTER_LABELS[value]}
            </option>
          ))}
        </Select>
      </div>

      <div className="w-40">
        <div className="mb-1">
          <Label htmlFor="orderBy">Sort by</Label>
        </div>
        <Select
          id="orderBy"
          name="orderBy"
          defaultValue={query.orderBy}
          onChange={(event) => event.currentTarget.form?.requestSubmit()}
        >
          {ORDER_BY_VALUES.map((value) => (
            <option key={value} value={value}>
              {ORDER_LABELS[value]}
            </option>
          ))}
        </Select>
      </div>

      <div className="flex gap-2">
        <Button type="submit" color="primary">
          Apply
        </Button>
        {hasActiveFilters && (
          <Button type="button" color="light" onClick={() => router.push("/")}>
            Clear
          </Button>
        )}
      </div>
    </form>
  );
}
