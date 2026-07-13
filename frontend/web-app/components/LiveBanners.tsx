"use client";

import { useEffect, useState } from "react";
import { Banner as FlowbiteBanner } from "flowbite-react";
import { useNotificationHub } from "@/components/NotificationProvider";
import type { Banner, BannerScope } from "@/types/admin";
import type { BannerPublishedPayload } from "@/types/notifications";

interface LiveBannersProps {
  /** Server-fetched active banners at page load (`GET api/banners`, anonymous) -- seeds the
   * list so the very first render already shows anything currently active, matching every
   * other "server-fetched, then kept live" pattern in this app (e.g. `BidStoreProvider`). */
  initialBanners: Banner[];
  /** Which scopes this placement shows -- Docs/Tasks.md Phase 11 Task 8.6: the home page shows
   * "Global"+"HomePage"; an auction detail page shows "Global"+"Auction" (matching `auctionId`). */
  scopes: readonly BannerScope[];
  /** Required when `scopes` includes "Auction" -- the current auction detail page's id. */
  auctionId?: string;
}

function isRelevant(banner: { scope: string; auctionId?: string | null }, scopes: readonly BannerScope[], auctionId?: string): boolean {
  if (!scopes.includes(banner.scope as BannerScope)) return false;
  if (banner.scope === "Auction") return banner.auctionId === auctionId;
  return true;
}

function isCurrentlyActive(banner: { activeFrom: string; activeUntil: string }, now: number): boolean {
  return new Date(banner.activeFrom).getTime() <= now && now <= new Date(banner.activeUntil).getTime();
}

/**
 * Renders currently-active platform banners (Task 8.6) -- the home page (Global + HomePage
 * scopes) and auction detail pages (Global + Auction, matching this page's `auctionId`) both
 * use this same component with different `scopes`/`auctionId`.
 *
 * Live-updates via the shared NotificationHub connection's "BannerPublished" broadcast
 * (`NotificationService.Consumers.BannerPublishedConsumer`) -- a newly published/updated
 * banner that matches this placement's scope appears immediately, no refresh, by upserting
 * into local state (by id, so an edited banner replaces its old copy rather than duplicating).
 * Re-evaluates the active window (`activeFrom`/`activeUntil`) once a minute so a banner whose
 * window has just lapsed disappears without requiring a new event or a page reload.
 */
export function LiveBanners({ initialBanners, scopes, auctionId }: LiveBannersProps) {
  const connection = useNotificationHub();
  const [banners, setBanners] = useState<Banner[]>(() =>
    initialBanners.filter((banner) => isRelevant(banner, scopes, auctionId))
  );
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (!connection) return;

    function handleBannerPublished(payload: BannerPublishedPayload) {
      if (!isRelevant(payload, scopes, auctionId)) return;

      const banner: Banner = {
        id: payload.id,
        message: payload.message,
        scope: payload.scope as BannerScope,
        auctionId: payload.auctionId,
        activeFrom: payload.activeFrom,
        activeUntil: payload.activeUntil,
        createdBy: "",
      };

      setBanners((prev) => {
        const withoutExisting = prev.filter((existing) => existing.id !== banner.id);
        return [...withoutExisting, banner];
      });
      // `now` otherwise only advances on the 60s interval tick, and a banner published AFTER
      // this page mounted has activeFrom > that stale timestamp -- isCurrentlyActive would
      // hide it for up to a minute, defeating the whole "appears without a refresh" behavior.
      setNow(Date.now());
    }

    connection.on("BannerPublished", handleBannerPublished);
    return () => {
      connection.off("BannerPublished", handleBannerPublished);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connection, auctionId]);

  const active = banners.filter((banner) => isCurrentlyActive(banner, now));
  if (active.length === 0) return null;

  return (
    <div className="space-y-2">
      {active.map((banner) => (
        <FlowbiteBanner key={banner.id}>
          <div className="flex w-full items-center justify-between rounded-lg border border-primary-200 bg-primary-50 px-4 py-2 text-sm text-primary-800">
            <p>{banner.message}</p>
          </div>
        </FlowbiteBanner>
      ))}
    </div>
  );
}
