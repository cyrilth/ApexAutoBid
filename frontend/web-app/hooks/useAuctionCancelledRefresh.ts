"use client";

import { useEffect, useRef } from "react";
import { useRouter } from "next/navigation";
import { useNotificationHub } from "@/components/NotificationProvider";
import { toastInfo } from "@/lib/toast";
import type { AuctionCancelledPayload } from "@/types/notifications";

/**
 * Live "this auction was just cancelled by an admin" handling for ONE auction detail page
 * (Phase 11 Task 8.6), mirroring `hooks/useLiveBids.ts`'s "subscribe to the shared connection,
 * filter to this page's auctionId" shape. Unlike a bid, a cancellation touches several
 * server-rendered parts of this page at once (status badge, countdown, bid panel's
 * live/ended state) -- rather than duplicating all of that into client state, this just
 * toasts and asks Next.js to re-run the page's Server Components (`router.refresh()`), which
 * re-fetches `GET api/auctions/{id}` and re-renders with the now-Cancelled status, same as a
 * manual reload would show but without leaving the page.
 */
export function useAuctionCancelledRefresh(auctionId: string): void {
  const connection = useNotificationHub();
  const router = useRouter();

  const auctionIdRef = useRef(auctionId);
  useEffect(() => {
    auctionIdRef.current = auctionId;
  }, [auctionId]);

  useEffect(() => {
    if (!connection) return;

    function handleAuctionCancelled(payload: AuctionCancelledPayload) {
      if (payload.auctionId !== auctionIdRef.current) return;
      toastInfo("This auction has been cancelled by an admin.");
      router.refresh();
    }

    connection.on("AuctionCancelled", handleAuctionCancelled);
    return () => {
      connection.off("AuctionCancelled", handleAuctionCancelled);
    };
  }, [connection, router]);
}
