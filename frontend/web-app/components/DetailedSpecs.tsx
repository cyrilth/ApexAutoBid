import { formatCurrency, numberWithCommas } from "@/lib/format";
import type { AuctionDetail } from "@/types/auction";

interface DetailedSpecsProps {
  auction: AuctionDetail;
}

/**
 * Vehicle + auction specs (Task 5.1): seller, make, model, year, mileage,
 * reserve price -- plus color, since it's already part of the auction shape
 * and belongs in the same spec grid. Server Component: purely presentational,
 * no interactivity.
 */
export function DetailedSpecs({ auction }: DetailedSpecsProps) {
  const specs: Array<[string, string]> = [
    ["Seller", auction.seller],
    ["Make", auction.make],
    ["Model", auction.model],
    ["Year", String(auction.year)],
    ["Color", auction.color],
    ["Mileage", `${numberWithCommas(auction.mileage)} mi`],
    ["Reserve price", auction.reservePrice > 0 ? formatCurrency(auction.reservePrice) : "No reserve"],
  ];

  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-lg border border-slate-200 bg-white p-4 sm:grid-cols-3">
      {specs.map(([label, value]) => (
        <div key={label}>
          <dt className="text-sm text-slate-500">{label}</dt>
          <dd className="font-semibold text-slate-900">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
