/**
 * Client-side pacer for GatewayService's "strict" rate-limit policy (`backend/GatewayService/
 * Program.cs`'s `AddPolicy("strict", ...)`, `RateLimiting:Mutating` in `appsettings.json` --
 * `PermitLimit: 10`, `WindowSeconds: 60`, a FIXED window keyed per client IP). That policy
 * applies to every mutating auction/bid route: `POST`/`PUT`/`DELETE /api/auctions{/**catch-all}`
 * (create, update, delete, upload-url, thumbnail) and `POST /api/bids` (`appsettings.json`'s
 * `auctions-write*`/`bids-place` route entries) -- MUCH stricter than the 100/60s "general"
 * policy `./api.ts`'s read-only fixture calls share.
 *
 * Discovered empirically running Task 15 Batch C's full suite: with every spec's own throwaway
 * auction (create/edit/delete/bid, several per spec) all sharing this ONE client-IP budget, the
 * suite's total mutating-request count comfortably exceeds 10 well before a fixed 60s window
 * elapses -- and because it's a fixed (not sliding) window, once exhausted every further
 * mutating request 429s until the ENTIRE window resets, cascading into the specific test's own
 * 30s Playwright timeout (a `page.waitForURL` that never resolves) rather than a clean,
 * recoverable per-request retry.
 *
 * The fix is a small in-process token-bucket-style pacer: every helper that's about to trigger
 * a mutating Gateway request (`./auction-builder.ts`'s `createAuctionViaUi`, `./bidding.ts`'s
 * `submitBid`, `edit.spec.ts`'s "Save changes" click, `delete.spec.ts`'s "Yes, delete it" click)
 * awaits `throttleMutation()` immediately beforehand -- proactively sleeping, if needed, so this
 * process never issues more than `LIMIT` such requests in any rolling 60s span. `LIMIT` is set
 * below the server's real `PermitLimit` (10) to leave headroom for OTHER specs' own mutating
 * calls this pacer doesn't wrap (e.g. `register.spec.ts`'s inline create-auction flow, a Batch B
 * spec out of this run's scope to modify) sharing the same 60s window.
 *
 * Only correct as a SINGLE in-process counter -- i.e. with `playwright.config.ts`'s `workers: 1`
 * for this project. Parallel workers are separate Node processes with no shared memory, so each
 * would maintain its own independent budget and could still collectively burst past the
 * server's real limit; a genuine cross-process limiter (a lockfile, a small local server) would
 * be substantially more machinery than a dev-only rate-limit budget warrants.
 */
const WINDOW_MS = 60_000;
const LIMIT = 8;
/** Past the server's own window boundary, so this pacer's retry never races the exact reset instant. */
const RESET_BUFFER_MS = 500;

let recentMutations: number[] = [];

export async function throttleMutation(): Promise<void> {
  const now = Date.now();
  recentMutations = recentMutations.filter((timestamp) => now - timestamp < WINDOW_MS);

  if (recentMutations.length >= LIMIT) {
    const oldest = recentMutations[0];
    const waitMs = WINDOW_MS - (now - oldest) + RESET_BUFFER_MS;
    await new Promise((resolve) => setTimeout(resolve, waitMs));
    await throttleMutation();
    return;
  }

  recentMutations.push(now);
}
