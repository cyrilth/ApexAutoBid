import type { Metadata } from "next";
import { getAuctionStats } from "@/lib/admin-auctions-service";
import { getBidStats } from "@/lib/admin-bids-service";
import { getUserStats } from "@/lib/admin-users-service";
import { StatCard, StatCardError } from "@/components/admin/StatCard";
import { numberWithCommas } from "@/lib/format";
import type { AdminAuctionStats, AdminBidStats, AdminUserStats } from "@/types/admin";

export const metadata: Metadata = { title: "Admin dashboard | ApexAutoBid" };

/**
 * Admin dashboard (Task 8.2) -- stats cards from the three per-service stats endpoints, each
 * fetched and rendered independently so one down service degrades to just its own card
 * showing "Unavailable" rather than failing the whole page (Docs/DesignGuide.md §7's 3-up
 * stats grid).
 */
export default async function AdminDashboardPage() {
  const [users, auctions, bids] = await Promise.allSettled([
    getUserStats(),
    getAuctionStats(),
    getBidStats(),
  ]);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-slate-900">Dashboard</h1>
        <p className="text-sm text-slate-500">Platform-wide stats across every service.</p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <UserStatsCard result={users} />
        <AuctionStatsCard result={auctions} />
        <BidStatsCard result={bids} />
      </div>
    </div>
  );
}

function UserStatsCard({ result }: { result: PromiseSettledResult<AdminUserStats> }) {
  if (result.status === "rejected") {
    return <StatCardError label="User stats (Identity Service)" />;
  }
  const { total, confirmed, locked } = result.value;
  return (
    <StatCard
      label="Users"
      value={numberWithCommas(total)}
      detail={
        <>
          {numberWithCommas(confirmed)} confirmed &middot; {numberWithCommas(locked)} locked
        </>
      }
    />
  );
}

function AuctionStatsCard({ result }: { result: PromiseSettledResult<AdminAuctionStats> }) {
  if (result.status === "rejected") {
    return <StatCardError label="Auction stats (Auction Service)" />;
  }
  const { total, byStatus } = result.value;
  return (
    <StatCard
      label="Auctions"
      value={numberWithCommas(total)}
      detail={
        <ul className="space-y-0.5">
          {Object.entries(byStatus).map(([status, count]) => (
            <li key={status} className="flex justify-between gap-4">
              <span>{status}</span>
              <span className="font-medium text-slate-900">{numberWithCommas(count)}</span>
            </li>
          ))}
        </ul>
      }
    />
  );
}

function BidStatsCard({ result }: { result: PromiseSettledResult<AdminBidStats> }) {
  if (result.status === "rejected") {
    return <StatCardError label="Bid stats (Bidding Service)" />;
  }
  return <StatCard label="Bids placed" value={numberWithCommas(result.value.totalBids)} />;
}
