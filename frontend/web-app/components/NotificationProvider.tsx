"use client";

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { toastInfo, toastSuccess, toastWarning } from "@/lib/toast";
import { formatCurrency } from "@/lib/format";
import type { AuctionCreatedPayload, AuctionFinishedPayload } from "@/types/notifications";

const NotificationHubContext = createContext<HubConnection | null>(null);

interface NotificationProviderProps {
  /** Whether the current visitor has a next-auth session (`auth()`, read server-side in app/layout.tsx). */
  isSignedIn: boolean;
  /** `session.user.username`, if signed in -- used only to skip the "AuctionCreated" toast for the auction's own creator (see below). */
  username?: string;
  children: ReactNode;
}

/**
 * Owns the app's single SignalR connection to NotificationHub (Phase 7 Task 8.2/8.3) and the
 * GLOBAL event handlers that apply on every page (Docs/DesignGuide.md §8's "noticeable without
 * being noisy" ethos): "AuctionWon"/"AuctionSellerResult" (targeted -- only reach this
 * connection when it's authenticated as the relevant username, see the backend's
 * `UsernameUserIdProvider`) and "AuctionCreated" (broadcast, deliberately low-noise).
 *
 * "BidPlaced" and "AuctionFinished" have NO handler here, on purpose:
 *   - "BidPlaced" only matters to whichever single auction detail page is currently open --
 *     `hooks/useLiveBids.ts` reads this same connection (via `useNotificationHub`) and filters
 *     to just that page's auction id, rather than this provider fanning every bid on the whole
 *     platform out to a toast no one asked for.
 *   - "AuctionFinished" (the broadcast variant) is already covered, for the two people who
 *     actually need to know, by the targeted "AuctionWon"/"AuctionSellerResult" follow-ups
 *     below, plus whatever the auction detail page itself shows (status badge, sold price) --
 *     a third toast on top of those would be pure noise (task brief: "avoid toast spam").
 *
 * One HubConnection for the whole app, not one per page: created here (mounted once in
 * app/layout.tsx, above every route) and handed down via Context so any Client Component
 * further down the tree -- today just `useLiveBids` -- registers its OWN page-scoped handlers
 * on this exact same connection instead of opening a second one.
 *
 * Two things force the connection to be built ASYNCHRONOUSLY, inside `useEffect`, rather than
 * eagerly during render (e.g. a lazy `useState` initializer):
 *
 *   1. `HubConnection` is a browser-only object -- `@microsoft/signalr`'s environment-detection
 *      code throws when evaluated during Next.js's server-side render pass (Client Components
 *      still render once on the server for the initial HTML). `useEffect` bodies, unlike render
 *      bodies, are GUARANTEED never to run during SSR, only after the browser commits/hydrates
 *      -- exactly the guarantee needed here. `import("@/lib/signalr")` is a dynamic import for
 *      the same reason: it defers even loading (not just calling) the `@microsoft/signalr`
 *      module out of the SSR bundle's synchronously-evaluated graph.
 *   2. `eslint-plugin-react-hooks`'s `set-state-in-effect` rule flags a synchronous `setState`
 *      call sitting directly in an effect body (see `you-might-not-need-an-effect`) --
 *      `setConnection` below runs inside an `async` function's body, after an `await`, which is
 *      exactly the sanctioned "subscribe for updates from an external system, calling setState
 *      in a callback function" shape the rule's own guidance describes, not the "cascading
 *      render" shape it warns against.
 *
 * Rebuilt (the old connection stopped, a fresh one started) whenever `isSignedIn` changes -- via
 * `key={isSignedIn}` on the inner `NotificationHubConnector` below, React's own idiom for "reset
 * this subtree's state" (https://react.dev/learn/preserving-and-resetting-state). See
 * `lib/signalr.ts`'s `createNotificationConnection` remarks on why the token factory is only
 * attached when signed in. In this app `isSignedIn` changing is always a hard navigation in
 * practice (IdentityServer's own login page / RP-initiated logout are both external redirects --
 * lib/auth-actions.ts), so this remount is really "the whole provider tree reloads with a new
 * isSignedIn prop from the server layout" -- but keying off the prop directly also covers any
 * future in-app session transition without further changes here.
 */
export function NotificationProvider({ isSignedIn, username, children }: NotificationProviderProps) {
  return (
    <NotificationHubConnector key={String(isSignedIn)} isSignedIn={isSignedIn} username={username}>
      {children}
    </NotificationHubConnector>
  );
}

function NotificationHubConnector({ isSignedIn, username, children }: NotificationProviderProps) {
  const [connection, setConnection] = useState<HubConnection | null>(null);

  // Read inside the "AuctionCreated" handler via a ref, not a `useEffect` dependency -- a
  // `username` change alone (vanishingly unlikely without `isSignedIn` also changing, and
  // therefore a remount) shouldn't tear down and reopen the socket; the ref just keeps the
  // closure reading the current value.
  const usernameRef = useRef(username);
  useEffect(() => {
    usernameRef.current = username;
  }, [username]);

  useEffect(() => {
    // Guards against the classic async-effect race: if `isSignedIn` flips again (a new
    // `NotificationHubConnector` mounts under a new `key`) before this connection finishes
    // connecting, the in-flight one must not overwrite the newer instance's state or leak a
    // socket nothing references anymore.
    let cancelled = false;
    let conn: HubConnection | undefined;

    function handleAuctionWon(payload: AuctionFinishedPayload) {
      // Deliberately reads only `amount` -- `winnerEmail` travels on this payload too
      // (backend contract, Contracts/AuctionFinished.cs), but Requirements §13.5 forbids
      // surfacing email addresses outside the post-sale contact flow
      // (components/PostSaleContact.tsx), so it's never touched here even though it's
      // technically present on `payload`.
      const amount = payload.amount != null ? formatCurrency(payload.amount) : "the winning price";
      toastSuccess(
        `You won the auction for ${amount}! Check the auction page for the seller's contact details.`
      );
    }

    function handleAuctionSellerResult(payload: AuctionFinishedPayload) {
      if (payload.itemSold) {
        const amount = payload.amount != null ? formatCurrency(payload.amount) : "the winning bid";
        toastSuccess(`Your auction sold for ${amount}!`);
      } else {
        toastWarning("Your auction ended without meeting the reserve price -- it did not sell.");
      }
    }

    function handleAuctionCreated(payload: AuctionCreatedPayload) {
      // Low-noise (Docs/DesignGuide.md §8): skip the toast for the visitor who just created
      // this exact auction -- they already know, they were just looking at its own
      // confirmation.
      if (usernameRef.current && payload.seller === usernameRef.current) return;
      toastInfo(`New auction: ${payload.year} ${payload.make} ${payload.model}`);
    }

    async function connect() {
      // Dynamic import -- see this component's doc comment, point 1: keeps
      // "@microsoft/signalr" (and its browser-only environment detection) out of the
      // server-side render pass entirely.
      const { createNotificationConnection } = await import("@/lib/signalr");
      if (cancelled) return;

      conn = createNotificationConnection(isSignedIn);
      conn.on("AuctionWon", handleAuctionWon);
      conn.on("AuctionSellerResult", handleAuctionSellerResult);
      conn.on("AuctionCreated", handleAuctionCreated);

      setConnection(conn);

      try {
        await conn.start();
      } catch (error) {
        console.error("Failed to start the SignalR notifications connection.", error);
      }
    }

    void connect();

    return () => {
      cancelled = true;
      if (conn) {
        conn.off("AuctionWon", handleAuctionWon);
        conn.off("AuctionSellerResult", handleAuctionSellerResult);
        conn.off("AuctionCreated", handleAuctionCreated);
        // Fire-and-forget -- React's cleanup can't await, and stopping a connection that's
        // still mid-`start()` or already stopped both resolve harmlessly either way.
        void conn.stop();
      }
    };
  }, [isSignedIn]);

  return <NotificationHubContext.Provider value={connection}>{children}</NotificationHubContext.Provider>;
}

/**
 * Reads the shared HubConnection from context -- `null` until the provider's first connect
 * attempt has resolved (briefly, on every initial mount -- see this file's doc comment on why
 * the connection can only be built asynchronously). Consumers (e.g. `hooks/useLiveBids.ts`)
 * must guard for `null` and re-run their own effect once it becomes available, same as they
 * would for any other async-ready resource.
 */
export function useNotificationHub(): HubConnection | null {
  return useContext(NotificationHubContext);
}
