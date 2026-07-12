import type { AuctionDetail } from "@/types/auction";

interface PostSaleContactProps {
  auction: AuctionDetail;
}

/**
 * Post-sale contact callout (Task 5.4, Docs/DesignGuide.md §7 -- rendered in
 * an `accent-cream` callout). Renders only when the backend actually
 * included a contact field in the response -- `AuctionDetailDto` omits
 * `sellerEmail`/`winnerEmail` entirely (not just sets them `null`) for every
 * caller except the counterparty (Requirements §3.1: the seller sees the
 * winner's email, the winner sees the seller's email, no one else sees
 * either). "Field present in the JSON" IS the authorization check here --
 * this component does no additional caller-identity logic of its own, and
 * never receives a raw JWT or any other caller identity to check against.
 */
export function PostSaleContact({ auction }: PostSaleContactProps) {
  if (!auction.sellerEmail && !auction.winnerEmail) {
    return null;
  }

  return (
    <div className="rounded-lg border border-accent-leaf bg-accent-cream p-4">
      <h2 className="text-sm font-semibold text-emerald-900">Sale complete — contact details</h2>

      {auction.winnerEmail && (
        <p className="mt-1 text-sm text-slate-700">
          Winning bidder&apos;s email:{" "}
          <a className="font-medium text-primary-700 hover:underline" href={`mailto:${auction.winnerEmail}`}>
            {auction.winnerEmail}
          </a>
        </p>
      )}

      {auction.sellerEmail && (
        <p className="mt-1 text-sm text-slate-700">
          Seller&apos;s email:{" "}
          <a className="font-medium text-primary-700 hover:underline" href={`mailto:${auction.sellerEmail}`}>
            {auction.sellerEmail}
          </a>
        </p>
      )}
    </div>
  );
}
