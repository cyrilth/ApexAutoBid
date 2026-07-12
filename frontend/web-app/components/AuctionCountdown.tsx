"use client";

import Countdown, { type CountdownRenderProps } from "react-countdown";

interface AuctionCountdownProps {
  auctionEnd: string;
  /**
   * Only `Live` auctions get a ticking clock -- a `Finished`/`ReserveNotMet`/
   * `Cancelled` auction's `auctionEnd` is already in the past (or moot), so
   * this renders nothing for those; the status badge + sold amount already
   * cover them.
   */
  isLive: boolean;
}

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

function renderer({ days, hours, minutes, seconds, completed }: CountdownRenderProps) {
  if (completed) {
    return <p className="text-lg font-semibold text-slate-500">Auction ended</p>;
  }

  // "Ending soon" mirrors AuctionStatusBadge's identical < 6h threshold (Docs/DesignGuide.md §2).
  const isEndingSoon = days * 24 + hours < 6;

  return (
    <p
      className={`text-2xl font-bold tabular-nums ${isEndingSoon ? "text-amber-600" : "text-primary-700"}`}
    >
      {days > 0 && `${days}d `}
      {pad(hours)}h {pad(minutes)}m {pad(seconds)}s
    </p>
  );
}

/**
 * Auction end countdown (Task 5.2, Docs/DesignGuide.md §7 -- "countdown
 * prominent near the bid panel"). Client Component: `react-countdown` ticks
 * every second via its own internal timer, which can't run during SSR.
 *
 * On completion this only flips to a static "Auction ended" label -- it does
 * NOT refetch or re-derive the auction's real status. Full live status
 * transitions (the backend actually finalizing the auction) arrive via
 * SignalR in a later task (Phase 7 Task 8+); this is deliberately just the
 * clock.
 */
export function AuctionCountdown({ auctionEnd, isLive }: AuctionCountdownProps) {
  if (!isLive) {
    return null;
  }

  return (
    <div>
      <p className="text-sm text-slate-500">Time remaining</p>
      <Countdown date={new Date(auctionEnd)} renderer={renderer} />
    </div>
  );
}
