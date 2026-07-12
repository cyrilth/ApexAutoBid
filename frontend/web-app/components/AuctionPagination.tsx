"use client";

import { Pagination } from "flowbite-react";
import { useRouter } from "next/navigation";
import { buildAuctionHref, type AuctionQuery } from "@/lib/auction-search-params";

/** Pagination driven by the URL's `pageNumber` param, preserving the rest of the current query (Task 4.2). */
export function AuctionPagination({ query, totalPages }: { query: AuctionQuery; totalPages: number }) {
  const router = useRouter();

  return (
    <div className="flex justify-center">
      <Pagination
        currentPage={query.pageNumber}
        totalPages={totalPages}
        onPageChange={(page) => router.push(buildAuctionHref(query, { pageNumber: page }))}
        showIcons
      />
    </div>
  );
}
