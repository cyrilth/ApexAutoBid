import type { Metadata } from "next";
import Link from "next/link";
import { AdminAuctionsTable } from "@/components/admin/AdminAuctionsTable";
import { searchAuctions } from "@/lib/search-service";

export const metadata: Metadata = { title: "Admin auctions | ApexAutoBid" };

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

interface AdminAuctionsPageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

/**
 * Auctions page (Task 8.4): search/list every auction (`GET api/search`, the same endpoint the
 * public listing page uses) with per-row "End now"/"Cancel" moderation actions. "Create
 * auction" reuses the existing create form (`/auctions/create`), which shows a seller-
 * assignment field for admins (`components/AuctionForm.tsx`).
 */
export default async function AdminAuctionsPage({ searchParams }: AdminAuctionsPageProps) {
  const params = await searchParams;
  const searchTerm = first(params.searchTerm)?.trim() ?? "";
  const seller = first(params.seller)?.trim() ?? "";
  const pageRaw = Number(first(params.page));
  const pageNumber = Number.isInteger(pageRaw) && pageRaw > 0 ? pageRaw : 1;

  let result;
  let loadError = false;
  try {
    result = await searchAuctions({
      searchTerm,
      seller,
      winner: "",
      orderBy: "new",
      filterBy: "all",
      pageNumber,
    });
  } catch {
    loadError = true;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900">Auctions</h1>
          <p className="text-sm text-slate-500">Search auctions, moderate live listings, or create one for a seller.</p>
        </div>
        <Link
          href="/auctions/create"
          className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-medium text-white hover:bg-primary-700 focus:outline-none focus:ring-4 focus:ring-primary-400"
        >
          Create auction
        </Link>
      </div>

      {loadError || !result ? (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-8 text-center">
          <p className="text-sm font-medium text-red-700">
            We couldn&apos;t load auctions right now. Please try again shortly.
          </p>
        </div>
      ) : (
        <AdminAuctionsTable
          auctions={result.results}
          totalPages={result.pageCount}
          currentPage={pageNumber}
          searchTerm={searchTerm}
          seller={seller}
        />
      )}
    </div>
  );
}
