import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { auth } from "@/auth";
import { AuctionCountdown } from "@/components/AuctionCountdown";
import { AuctionGallery } from "@/components/AuctionGallery";
import { AuctionStatusBadge } from "@/components/AuctionStatusBadge";
import { BidHistory } from "@/components/BidHistory";
import { DeleteAuctionButton } from "@/components/DeleteAuctionButton";
import { DetailedSpecs } from "@/components/DetailedSpecs";
import { PostSaleContact } from "@/components/PostSaleContact";
import { ShareButtons } from "@/components/ShareButtons";
import { summarizeAuction } from "@/lib/auction-copy";
import { getAuctionById } from "@/lib/auction-service";
import { getBidsForAuction } from "@/lib/bid-service";
import { formatCurrency } from "@/lib/format";
import { hasAdminRole } from "@/lib/roles";
import { getSiteUrl } from "@/lib/site-url";
import type { Bid } from "@/types/bid";

interface AuctionDetailPageProps {
  params: Promise<{ id: string }>;
}

// ── generateMetadata (Task 5.6) ──────────────────────────────────────────
//
// Reuses lib/auction-service.ts's `getAuctionById` -- same URL/options as the
// page component below calls it with, so Next's automatic fetch memoization
// (Docs: "fetch requests are automatically memoized ... across
// generateMetadata ... Layouts, Pages") collapses the two into a single
// network request per page load rather than fetching the auction twice.

export async function generateMetadata({ params }: AuctionDetailPageProps): Promise<Metadata> {
  const { id } = await params;
  const auction = await getAuctionById(id);

  if (!auction) {
    return { title: "Auction not found" };
  }

  const title = `${auction.year} ${auction.make} ${auction.model}`;
  const description = summarizeAuction(auction);
  // Primary image is images[0] (SortOrder = 0, backend pre-sorted) -- its
  // thumbnail is preferred, falling back to the full-size image (Requirements §4).
  const primaryImage = auction.images[0];
  const imageUrl = primaryImage?.thumbnailUrl ?? primaryImage?.url;
  const canonicalUrl = `${getSiteUrl()}/auctions/${auction.id}`;

  return {
    title,
    description,
    openGraph: {
      title,
      description,
      url: canonicalUrl,
      images: imageUrl ? [{ url: imageUrl }] : undefined,
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: imageUrl ? [imageUrl] : undefined,
    },
  };
}

export default async function AuctionDetailPage({ params }: AuctionDetailPageProps) {
  const { id } = await params;
  const auction = await getAuctionById(id);

  if (!auction) {
    notFound();
  }

  // Bid history is a secondary panel on this page -- if the Bidding Service
  // is unreachable, degrade to an inline message rather than failing the
  // whole page (mirrors AuctionResults's identical try/catch for the search
  // fetch on the listing page).
  let bids: Bid[] = [];
  let bidsFailed = false;
  try {
    bids = await getBidsForAuction(id);
  } catch {
    bidsFailed = true;
  }

  const isSold = auction.status === "Finished";
  const highBid = isSold ? auction.soldAmount : auction.currentHighBid;
  const bidLabel = isSold ? "Sold for" : "Current bid";

  // Edit link (Task 6) / Delete button (Task 7) -- shown only to the
  // auction's own seller or an admin; the PUT/DELETE api/auctions/{id}
  // endpoints enforce the real ownership check regardless, this just avoids
  // dangling the controls in front of everyone else.
  const session = await auth();
  const canEdit =
    Boolean(session?.user?.username) &&
    (session!.user.username === auction.seller || hasAdminRole(session!.user.role));

  const title = `${auction.year} ${auction.make} ${auction.model}`;
  const shareUrl = `${getSiteUrl()}/auctions/${auction.id}`;
  const shareText = summarizeAuction(auction);

  return (
    <div className="mx-auto w-full max-w-7xl space-y-6 px-4 py-8 sm:px-6">
      <Link
        href="/"
        className="text-sm font-medium text-primary-700 hover:underline focus:outline-none focus:ring-2 focus:ring-primary-400"
      >
        &larr; Back to auctions
      </Link>

      {/* Two columns on lg (image + specs left, bid panel + history right), stacked on mobile (Docs/DesignGuide.md §4). */}
      <div className="grid grid-cols-1 gap-8 lg:grid-cols-2">
        <div className="space-y-6">
          <AuctionGallery images={auction.images} alt={title} />
          <DetailedSpecs auction={auction} />
          <ShareButtons url={shareUrl} title={title} text={shareText} />
        </div>

        <div className="space-y-6">
          <div className="space-y-4 rounded-lg border border-slate-200 bg-white p-4">
            <div className="flex items-start justify-between gap-2">
              <h1 className="text-2xl font-bold text-slate-900">{title}</h1>
              <div className="flex items-center gap-2">
                <AuctionStatusBadge item={auction} />
                {/* Styled Link, not Flowbite `Button as={Link}` -- Button is a
                    Client Component and can't receive a function component
                    as a prop from this Server Component (RSC serialization). */}
                {canEdit && (
                  <Link
                    href={`/auctions/${auction.id}/edit`}
                    className="rounded-lg border border-slate-300 bg-white px-2 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100 focus:outline-none focus:ring-4 focus:ring-primary-400"
                  >
                    Edit
                  </Link>
                )}
                {/* DeleteAuctionButton is a small Client Component (needs the
                    Flowbite Modal + confirm/cancel interactivity) -- everything
                    else on this page stays a Server Component. */}
                {canEdit && <DeleteAuctionButton auctionId={auction.id} displayName={title} />}
              </div>
            </div>

            <div>
              <p className="text-sm text-slate-500">{bidLabel}</p>
              <p className="text-3xl font-bold text-slate-900">
                {highBid != null ? formatCurrency(highBid) : "No bids yet"}
              </p>
            </div>

            <AuctionCountdown auctionEnd={auction.auctionEnd} isLive={auction.status === "Live"} />
          </div>

          <PostSaleContact auction={auction} />

          <div>
            <h2 className="mb-3 text-xl font-semibold text-slate-900">Bid history</h2>
            {bidsFailed ? (
              <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-6 text-center">
                <p className="text-sm font-medium text-red-700">
                  We couldn&apos;t load the bid history right now. Please try again shortly.
                </p>
              </div>
            ) : (
              <BidHistory bids={bids} />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
