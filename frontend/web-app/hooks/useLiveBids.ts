"use client";

import { useEffect, useRef } from "react";
import { useNotificationHub } from "@/components/NotificationProvider";
import { useBidStore } from "@/components/BidStoreProvider";
import type { Bid } from "@/types/bid";

/**
 * Live bid updates for ONE auction detail page (Phase 7 Task 8.2). Subscribes to the shared
 * NotificationHub connection's "BidPlaced" broadcast (`NotificationService.Consumers
 * .BidPlacedConsumer` -- every bid on every auction, platform-wide), filters down to this
 * page's `auctionId`, and feeds matches into the page's `BidStoreProvider` via `addLiveBid` --
 * the same store `BidPanel` (Task 8.1) and `BidHistory` already read/write, so a bid placed by
 * any OTHER connected bidder now appears at the top of the history (and updates `BidPanel`'s
 * minimum-bid guidance) with no page reload, exactly like this bidder's own `placeBid` result
 * already did.
 *
 * `Contracts.BidPlaced`'s fields (Id, AuctionId, Bidder, BidTime, Amount, BidStatus), once
 * camelCased by SignalR's default JSON hub protocol, are already IDENTICAL to `types/bid.ts`'s
 * `Bid` shape field-for-field -- no mapping needed beyond typing the event payload as `Bid`
 * (see types/notifications.ts's remarks on why BidPlaced gets no dedicated payload interface).
 *
 * Call this once, from a Client Component already rendering under both `NotificationProvider`
 * (app/layout.tsx) and this page's own `BidStoreProvider` (app/auctions/[id]/page.tsx) --
 * currently `BidHistory`.
 */
export function useLiveBids(auctionId: string): void {
  const connection = useNotificationHub();
  const addLiveBid = useBidStore((state) => state.addLiveBid);

  // Keeps the registered handler reading the LATEST `addLiveBid`/`auctionId` without needing
  // to re-subscribe (`connection.off`/`connection.on`) every time either changes -- only a
  // `connection` identity change (the provider rebuilding it on sign-in/out) should do that.
  const latestRef = useRef({ addLiveBid, auctionId });
  useEffect(() => {
    latestRef.current = { addLiveBid, auctionId };
  }, [addLiveBid, auctionId]);

  useEffect(() => {
    if (!connection) return;

    function handleBidPlaced(payload: Bid) {
      const { auctionId: currentAuctionId, addLiveBid: currentAddLiveBid } = latestRef.current;
      if (payload.auctionId !== currentAuctionId) return;
      currentAddLiveBid(payload);
    }

    connection.on("BidPlaced", handleBidPlaced);
    return () => {
      connection.off("BidPlaced", handleBidPlaced);
    };
  }, [connection]);
}
