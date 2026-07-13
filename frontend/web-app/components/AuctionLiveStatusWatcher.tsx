"use client";

import { useAuctionCancelledRefresh } from "@/hooks/useAuctionCancelledRefresh";

/**
 * Mounts `useAuctionCancelledRefresh` on the auction detail page (Task 8.6) -- renders nothing,
 * it exists purely to run that hook's effect from within the page's Client Component tree (a
 * Server Component can't call hooks directly).
 */
export function AuctionLiveStatusWatcher({ auctionId }: { auctionId: string }) {
  useAuctionCancelledRefresh(auctionId);
  return null;
}
