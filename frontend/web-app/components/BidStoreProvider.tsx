"use client";

import { createContext, useContext, useState, type ReactNode } from "react";
import { useStore } from "zustand";
import { createBidStore, type BidStore } from "@/stores/bid-store";
import type { Bid } from "@/types/bid";

export type BidStoreApi = ReturnType<typeof createBidStore>;

const BidStoreContext = createContext<BidStoreApi | undefined>(undefined);

interface BidStoreProviderProps {
  /**
   * Server-fetched bids for this auction (`getBidsForAuction`, read in
   * `app/auctions/[id]/page.tsx`) -- seeds the store so the very first
   * client render matches the server-rendered HTML exactly (no hydration
   * mismatch, no flash of an empty bid history).
   */
  initialBids: Bid[];
  children: ReactNode;
}

/**
 * One store instance per auction detail page render (Task 9).
 *
 * A module-level `create(...)` store (the plain Zustand quick-start pattern)
 * would be a SINGLE instance shared by the whole Node.js server process --
 * fine for state that's never touched during SSR, but this store's initial
 * data (the bid list for one specific auction, fetched per-request in a
 * Server Component) varies request to request. Sharing one instance across
 * concurrent requests could leak one visitor's auction's bids into another
 * visitor's page. Per current Next.js/Zustand App Router guidance, the fix
 * is to create the store fresh inside a Client Component's render (via
 * `useState`'s lazy initializer, so it's created exactly once and survives
 * re-renders but not remounts -- `useRef`'s "create on first render" idiom
 * does the same thing but this codebase's `react-hooks/refs` lint rule
 * disallows reading `ref.current` during render, so `useState` is the
 * conforming option) and hand it to descendants through React Context
 * rather than importing a shared hook from module scope.
 *
 * `BidPanel` and `BidHistory` both read/write this same instance -- placing
 * a bid (Task 8.1) calls `prependBid` and the history list re-renders
 * immediately, no `router.refresh()`/page reload needed. The Task 8.2/8.3
 * follow-up run hooks in the exact same way: a SignalR "BidPlaced" listener
 * (mounted somewhere under this provider, e.g. inside `BidPanel` or a
 * sibling client component) calls `useBidStore((s) => s.prependBid)` for
 * every incoming event, same as this run's `placeBid` result does.
 */
export function BidStoreProvider({ initialBids, children }: BidStoreProviderProps) {
  const [store] = useState<BidStoreApi>(() => createBidStore(initialBids));

  return <BidStoreContext.Provider value={store}>{children}</BidStoreContext.Provider>;
}

/** Reads/subscribes to the nearest `BidStoreProvider`'s store via a selector. */
export function useBidStore<T>(selector: (store: BidStore) => T): T {
  const context = useContext(BidStoreContext);
  if (!context) {
    throw new Error("useBidStore must be used within a BidStoreProvider");
  }
  return useStore(context, selector);
}
