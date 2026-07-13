import { createStore } from "zustand/vanilla";
import type { Bid } from "@/types/bid";

/**
 * Client-side bid state for a single auction detail page (Phase 7 Task 9).
 * Seeded from the server-fetched `GET api/bids/{auctionId}` list
 * (`lib/bid-service.ts`) so the first paint never flashes empty, then kept
 * live in the browser by two producers:
 *
 * 1. Task 8.1: `placeBid`'s own result -- the bidder's own
 *    Accepted/AcceptedBelowReserve/TooLow/Finished outcome -- via `prependBid`.
 * 2. Task 8.2/8.3: `hooks/useLiveBids.ts`'s SignalR `"BidPlaced"` handler,
 *    pushing every OTHER connected bidder's new bids into the same store --
 *    via `addLiveBid`, so `BidHistory` (and `BidPanel`'s live "current high
 *    bid" read) update without a page reload regardless of who placed the
 *    bid.
 *
 * Both actions insert with the same idempotent-by-id logic; they're kept
 * separate rather than unified because only `addLiveBid` also records
 * `latestLiveBidId` -- see its own remarks.
 */
export interface BidState {
  bids: Bid[];
  /**
   * Id of the most recently arrived bid via SignalR (Task 8.2) --
   * `BidHistory` reads this to apply the accent-leaf background pulse
   * (Docs/DesignGuide.md §8) to exactly that one row, then clears it via
   * `clearLatestLiveBid` once the animation finishes. Bids added through
   * `prependBid` (this bidder's own `placeBid` result) never set this --
   * that outcome is already communicated by `BidPanel`'s own toast, so it
   * doesn't need the extra live-update visual too.
   */
  latestLiveBidId: string | null;
}

export interface BidActions {
  /** Replaces the whole list -- e.g. re-hydrating after a fresh server fetch. */
  setBids: (bids: Bid[]) => void;
  /**
   * Inserts one new bid at the front, matching `GET api/bids/{auctionId}`'s
   * own newest-first ordering (`BidsController`'s contract, mirrored by
   * `BidHistory`'s existing defensive sort). Idempotent by `id` -- guards
   * against the same bid arriving twice (e.g. the bidder's own tab getting
   * both `placeBid`'s direct result AND that same bid echoed back over
   * SignalR via `addLiveBid` below). Used by `BidPanel` for the bidder's
   * own placed bid -- never sets `latestLiveBidId` (see its remarks).
   */
  prependBid: (bid: Bid) => void;
  /**
   * Inserts a bid pushed by SignalR's "BidPlaced" broadcast (Task 8.2).
   * Reuses `prependBid`'s idempotency-by-id, plus records the
   * newly-inserted bid's id as the one `BidHistory` should pulse. A no-op
   * (no pulse, no duplicate) if the bid already exists -- e.g. this
   * bidder's own bid echoing back through the broadcast after `prependBid`
   * already inserted it directly from `placeBid`'s result.
   */
  addLiveBid: (bid: Bid) => void;
  /** Clears the pulse target once its animation has played (`BidHistory`'s `onAnimationEnd`). */
  clearLatestLiveBid: () => void;
  /**
   * Drops one bid from local state after an admin's `DELETE api/admin/bids/{id}` succeeds
   * (Phase 11 Task 8.4) -- lets `BidHistory` reflect the removal instantly without a full page
   * reload, mirroring how `prependBid`/`addLiveBid` already keep this store in sync with the
   * backend without one.
   */
  removeBid: (bidId: string) => void;
}

export type BidStore = BidState & BidActions;

export const createBidStore = (initialBids: Bid[] = []) => {
  return createStore<BidStore>()((set) => ({
    bids: initialBids,
    latestLiveBidId: null,
    setBids: (bids) => set({ bids }),
    prependBid: (bid) =>
      set((state) => {
        if (state.bids.some((existing) => existing.id === bid.id)) {
          return state;
        }
        return { bids: [bid, ...state.bids] };
      }),
    addLiveBid: (bid) =>
      set((state) => {
        if (state.bids.some((existing) => existing.id === bid.id)) {
          return state;
        }
        return { bids: [bid, ...state.bids], latestLiveBidId: bid.id };
      }),
    clearLatestLiveBid: () => set({ latestLiveBidId: null }),
    removeBid: (bidId) => set((state) => ({ bids: state.bids.filter((bid) => bid.id !== bidId) })),
  }));
};
