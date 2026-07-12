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
 * Left empty here -- Task 14 is scaffold-only; Task 15's specs populate the
 * actual seeded usernames/passwords/auction ids/makes they need as those
 * specs are written.
 */

export const SEEDED_USERS = {
  // e.g. seller: { username: "bob", password: "Pass123$" } -- Task 15.6+
} as const;

export const SEEDED_AUCTIONS = {
  // e.g. first: { id: "...", make: "Ford", model: "Mustang" } -- Task 15.12+
} as const;
