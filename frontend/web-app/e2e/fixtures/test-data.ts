/**
 * Test-data constants for e2e specs (Phase 7 Task 14 scaffold).
 *
 * The auctions/users referenced by Task 15's specs are the FIXED,
 * well-known seed records each backend service creates on first run against
 * an empty database -- not randomly generated, so a spec can hardcode an id
 * or username and it'll be the same in every fresh environment:
 *
 *  - backend/AuctionService/AuctionService.Infrastructure/Data/DbInitializer.cs
 *    -- seeds auctions with fixed, literal Guids (see the comment above its
 *    `SeedDataAsync`).
 *  - backend/BiddingService/BiddingService.Infrastructure/Data/DbInitializer.cs
 *    -- seeds bid history against those SAME literal auction Guids, so an
 *    auction detail spec's bid history and the listing page always agree.
 *  - backend/IdentityService/Data/DbInitializer.cs and SeedData.cs -- seed
 *    users/roles (sellers/bidders) IdentityServer's login page authenticates
 *    against.
 *
 * Specs should prefer these fixed values (or values a spec creates itself,
 * e.g. Task 15.9's "create auction" flow) over asserting on live/mutable
 * totals, so a spec stays correct regardless of what OTHER specs in the same
 * run have created/bid on/deleted.
 *
 * Populated below (Task 15 Batch A). Auction ids are deliberately NOT hardcoded here even
 * though they're fixed literal Guids today -- Batch A's specs that need a specific auction
 * (a particular status, one with bid history) fetch it dynamically via `./api.ts` instead, so
 * they stay correct even if a future seed regenerates ids. `SEED_MAKES` below only needs a
 * stable make string, not an id, so it's safe to keep as a plain constant.
 */

/**
 * `backend/IdentityService/SeedData.cs`'s `SeedUsers` array, verbatim: fixed usernames/emails,
 * one shared dev password (`SharedDevPassword`). `admin` additionally holds the "admin" role;
 * the other three are plain bidders/sellers only.
 */
export const SEEDED_USERS = {
  bob: { username: "bob", email: "bob@apexautobid.local", password: "Pass123$" },
  alice: { username: "alice", email: "alice@apexautobid.local", password: "Pass123$" },
  tom: { username: "tom", email: "tom@apexautobid.local", password: "Pass123$" },
  admin: { username: "admin", email: "admin@apexautobid.local", password: "Pass123$", isAdmin: true },
} as const;

/**
 * Facts about the Auction/Search Service seed set (`AuctionService.Infrastructure.Data
 * .DbInitializer`) stable enough to assert on directly, without needing a live API call first.
 * Specs needing a SPECIFIC auction (a particular id, a particular status, one with bid history)
 * fetch it dynamically via `./api.ts` instead -- the ten seed auctions' ids are fixed literal
 * Guids in this codebase today, but nothing here should assume that stays true, per Docs/Tasks.md
 * Phase 7 Task 15's "don't hardcode ids" guidance.
 */
export const SEED_MAKES = {
  /** Present on exactly 3 seeded auctions (Model T, Mustang, GT) -- Task 15.2's search-by-make case. */
  matching: "Ford",
  /** Not present on any seeded auction -- Task 15.2's "no results" case. */
  nonMatching: "Toyota",
} as const;

/**
 * Fresh, never-seen-before registration data for Task 15.16's email-verification spec (and any
 * other spec that needs a brand-new account, e.g. a future Batch C image-upload test) --
 * IdentityService rejects a duplicate username/email outright, so each test run needs its own.
 * `Date.now()` (rather than a random UUID) keeps the generated username/email human-scannable in
 * Mailpit/IdentityService's DB while still being unique per run; the "e2e_" prefix makes
 * spec-created accounts easy to recognize (and later prune) among real dev-seed data.
 */
export function uniqueE2EUser(): { username: string; email: string; password: string } {
  const suffix = Date.now();
  return {
    username: `e2e_${suffix}`,
    email: `e2e_${suffix}@apexautobid.local`,
    password: "Pass123$",
  };
}
