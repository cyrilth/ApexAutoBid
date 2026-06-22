# ApexAutoBid - Task Tracker

## Progress Summary

> Maintained by the `task-manager` agent — update the matching row whenever tasks are checked off below. Counts include subtasks.

| Phase | Done | Total | Status |
|-------|------|-------|--------|
| 1. Auction Service | 28 | 57 | In progress |
| 2. Search Service | 0 | 30 | Not started |
| 3. Identity Service | 0 | 43 | Not started |
| 4. Gateway Service | 0 | 25 | Not started |
| 5. Bidding Service | 0 | 45 | Not started |
| 6. Notification Service | 0 | 19 | Not started |
| 7. Frontend (Next.js) | 0 | 55 | Not started |
| 8. Docker Compose Deployment | 0 | 11 | Not started |
| 9. Kubernetes Local Deployment | 0 | 18 | Not started |
| 10. CI/CD & Cloud Deployment | 0 | 16 | Not started |
| 11. Admin Dashboard | 0 | 52 | Not started |
| **Overall** | **28** | **371** | **In progress** |

Status values: `Not started` · `In progress` · `Done`

---

## Phase 1: Auction Service

**Goal:** Build the foundational microservice that manages auction CRUD operations.

**Purpose:** Establishes the project structure, shared event contracts, database patterns, and messaging infrastructure that all other services will build upon.

**Acceptance Criteria:**
- REST API for auctions is functional (all 6 endpoints)
- PostgreSQL database is seeded with 10 auction items
- Events are published to RabbitMQ on create/update/delete
- Consumers handle incoming `BidPlaced` and `AuctionFinished` events
- Unit tests (7 cases) and integration tests (3 cases) pass
- OpenAPI document (`/openapi/v1.json`) and Scalar docs UI (`/scalar`) are served
- Service runs in Docker

### Tasks

- [x] 1. Create the solution structure (`ApexAutoBid.slnx`, `backend/`, `tests/`, `.editorconfig`, `.vscode/extensions.json`, `backend/Directory.Build.props` with the shared `<Version>` per `Docs/Versioning.md`) — `dotnet-service-builder`
- [x] 2. Create the `Contracts` shared project — `dotnet-service-builder`
  - [x] 2.1. `AuctionCreated` event contract — `dotnet-service-builder`
  - [x] 2.2. `AuctionUpdated` event contract — `dotnet-service-builder`
  - [x] 2.3. `AuctionDeleted` event contract — `dotnet-service-builder`
  - [x] 2.4. `BidPlaced` event contract — `dotnet-service-builder`
  - [x] 2.5. `AuctionFinished` event contract (includes `WinnerEmail?` for post-sale contact exchange) — `dotnet-service-builder`
- [x] 3. Create the Auction Service Clean Architecture projects (Domain, Application, Infrastructure, API) with correct NuGet packages per layer — `dotnet-service-builder`
- [x] 4. Define entities in `AuctionService.Domain/Entities/` — `dotnet-service-builder`
  - [x] 4.1. `Auction` entity (includes `SellerEmail` from the `email` claim and nullable `WinnerEmail` — never mapped into `AuctionDto` by default) — `dotnet-service-builder`
  - [x] 4.2. `Item` entity + `ItemImage` child entity (ordered multi-image gallery, 1–10 per item; `SortOrder = 0` is the primary image) — `dotnet-service-builder`
  - [x] 4.3. `Status` enum in `AuctionService.Domain/Enums/` — `dotnet-service-builder`
- [x] 5. Define DTOs in `AuctionService.Application/DTOs/` — `dotnet-service-builder`
  - [x] 5.1. `AuctionDto` — `dotnet-service-builder`
  - [x] 5.2. `CreateAuctionDto` (requires 1–10 `Images` entries, ordered — first is primary) — `dotnet-service-builder`
  - [x] 5.3. `UpdateAuctionDto` (optional `Images?` — full gallery replacement under the same 1–10 bound) — `dotnet-service-builder`
- [x] 6. Set up PostgreSQL with Entity Framework in `AuctionService.Infrastructure/Data/` (DbContext, migrations) — `dotnet-service-builder`
- [x] 7. Configure Mapster mapping configs (`IRegister`) in `AuctionService.Application/Mappings/` (Entity ↔ DTO) — `dotnet-service-builder`
- [x] 8. Implement API endpoints in `AuctionService.API/Controllers/` — `dotnet-service-builder`
  - [x] 8.1. `GET api/auctions` — list all auctions — `dotnet-service-builder`
  - [x] 8.2. `GET api/auctions/{id}` — get auction by id — `dotnet-service-builder`
  - [x] 8.3. `GET api/auctions?date=` — get auctions updated from a given date — `dotnet-service-builder`
  - [x] 8.4. `POST api/auctions` — create auction (Auth) — `dotnet-service-builder`
  - [x] 8.5. `PUT api/auctions/{id}` — update auction (Auth) — `dotnet-service-builder`
  - [x] 8.6. `DELETE api/auctions/{id}` — delete auction (Auth) — `dotnet-service-builder`
  - [x] 8.7. Introduce repository + application-service layer per Architecture.md §7: `IAuctionRepository` (Domain/Interfaces) + `AuctionRepository` (Infrastructure/Data), `IAuctionService`/`AuctionAppService` (Application/Services) with an `AuctionWriteResult` enum; controllers depend on the service layer, not `DbContext` — `dotnet-service-builder`
- [x] 9. Add seed data — 10 auction items per `Requirements.md` §8.2 (relative `AuctionEnd` offsets, statuses incl. one ReserveNotMet and one sold with Winner/SoldAmount/WinnerEmail, single-image galleries with MinIO image URLs) — `dotnet-service-builder`
- [x] 10. Set up RabbitMQ with MassTransit (including Outbox pattern) in `AuctionService.API/Program.cs` — `dotnet-service-builder`
- [ ] 11. Publish events: `AuctionCreated`, `AuctionUpdated`, `AuctionDeleted` — `dotnet-service-builder`
- [ ] 12. Add event consumers in `AuctionService.Application/Consumers/` — `dotnet-service-builder`
  - [ ] 12.1. `BidPlaced` consumer — `dotnet-service-builder`
  - [ ] 12.2. `AuctionFinished` consumer (sets Winner, SoldAmount, Status, and `WinnerEmail`) — `dotnet-service-builder`
- [ ] 13. Dockerize the Auction Service (multi-project restore pattern for Clean Architecture; JSON console logging in the container environment — `Requirements.md` §13.5) — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 14. Write unit tests (AuctionService.UnitTests) — `dotnet-service-builder`
  - [ ] 14.1. CreateAuction — failed save returns 400 — `dotnet-service-builder`
  - [ ] 14.2. UpdateAuction — valid DTO returns OK — `dotnet-service-builder`
  - [ ] 14.3. UpdateAuction — invalid user returns 403 — `dotnet-service-builder`
  - [ ] 14.4. UpdateAuction — invalid GUID returns 404 — `dotnet-service-builder`
  - [ ] 14.5. DeleteAuction — valid user returns OK — `dotnet-service-builder`
  - [ ] 14.6. DeleteAuction — invalid GUID returns 404 — `dotnet-service-builder`
  - [ ] 14.7. DeleteAuction — invalid user returns 403 — `dotnet-service-builder`
- [ ] 15. Write integration tests (AuctionService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 15.1. CreateAuction — invalid DTO returns 400 — `dotnet-service-builder`
  - [ ] 15.2. UpdateAuction — valid DTO and user returns 200 — `dotnet-service-builder`
  - [ ] 15.3. UpdateAuction — valid DTO and invalid user returns 403 — `dotnet-service-builder`
- [ ] 16. Add API documentation: OpenAPI generation (`Microsoft.AspNetCore.OpenApi`) + Scalar UI (`Scalar.AspNetCore`), with a Bearer security scheme document transformer for the JWT-protected endpoints — `dotnet-service-builder`
- [ ] 17. Create `docker/docker-compose.infra.yml` for local development — PostgreSQL, MongoDB, RabbitMQ, Mailpit, MinIO (+ `mc` init container creating the `auction-images` bucket and uploading the committed sample images from `docker/seed-images/`) — *main conversation*, validate with `docker-validator`
- [ ] 18. Implement auction image upload via presigned URLs (see `Requirements.md` §3.1 Image Upload) — `dotnet-service-builder`
  - [ ] 18.1. `POST api/auctions/upload-url` (Auth): validate content type (jpeg/png/webp) AND declared size against `Images__MaxSizeMB` (default 5 MB), return a 5-minute presigned PUT URL (AWSSDK.S3 → MinIO, `Content-Length` signed) + the final object URL; GUID object keys — `dotnet-service-builder`
  - [ ] 18.2. Dedicated MinIO access key for the Auction Service with a `PutObject`+`DeleteObject` policy on `auction-images/*` (created by the `mc` init container; dev key committed, production via env vars) — `dotnet-service-builder`
  - [ ] 18.3. Unit tests: unauthenticated returns 401; disallowed content type returns 400; declared size over the limit returns 400; response contains a GUID key and expiry — `dotnet-service-builder`
  - [ ] 18.4. `POST api/auctions/thumbnail` (Auth): resize the uploaded object with SixLabors.ImageSharp (max 400px, WebP) to `thumbs/{key}.webp` and return the URL (called per image; stored on the matching `ItemImage.ThumbnailUrl`); accept only keys inside `auction-images` (no arbitrary URLs — SSRF guard) — `dotnet-service-builder`
  - [ ] 18.5. Unit tests: thumbnail for a valid key returns URL; key outside the bucket returns 400 — `dotnet-service-builder`
  - [ ] 18.6. Server-side gallery enforcement on create/update: 1–10 image count (`Images__MaxPerAuction`) via DTO validation, HEAD-verify actual size of each platform-hosted object (reject + delete oversized uploads; plain-URL fallback images exempt from size check but counted); unit tests: zero images returns 400, over-limit count returns 400 — `dotnet-service-builder`
- [ ] 19. Add global error handling: `IExceptionHandler` + ProblemDetails (validation → 400, unhandled → 500; dev = full detail, prod = generic message + `traceId` — see `Requirements.md` §13.1) — `dotnet-service-builder`
- [ ] 20. Add the `AuditEntry` entity and write audit records for auction create/update/delete in the same `SaveChanges` (see `Requirements.md` §13.3) — `dotnet-service-builder`
- [ ] 21. Add health endpoints: `GET /health/live` + `GET /health/ready` (PostgreSQL, RabbitMQ — see `Requirements.md` §13.4) — `dotnet-service-builder`

---

## Phase 2: Search Service

**Goal:** Build a read-optimized search service synchronized from the Auction Service.

**Purpose:** Provides full-text search, filtering, sorting, and pagination over auction data using MongoDB, keeping data in sync via RabbitMQ events.

**Acceptance Criteria:**
- Search API returns paged results with filtering, sorting, and search
- MongoDB stays in sync with Auction Service via event consumers
- HTTP polling fallback works for initial data sync
- OpenAPI document and Scalar docs UI are served
- Unit tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Search Service Clean Architecture projects (Domain, Application, Infrastructure, API) with correct NuGet packages per layer — `dotnet-service-builder`
- [ ] 2. Define the `Item` entity in `SearchService.Domain/Entities/` (MongoDB document mirroring AuctionDto fields) — `dotnet-service-builder`
- [ ] 3. Set up MongoDB connection via `MongoDB.Entities` in `SearchService.Infrastructure/Data/` — `dotnet-service-builder`
- [ ] 4. Implement event consumers in `SearchService.Application/Consumers/` — `dotnet-service-builder`
  - [ ] 4.1. `AuctionCreated` — insert item into MongoDB — `dotnet-service-builder`
  - [ ] 4.2. `AuctionUpdated` — update item fields — `dotnet-service-builder`
  - [ ] 4.3. `AuctionDeleted` — remove item — `dotnet-service-builder`
  - [ ] 4.4. `BidPlaced` — update current high bid — `dotnet-service-builder`
  - [ ] 4.5. `AuctionFinished` — update status, winner, sold amount (ignore `WinnerEmail` — emails are never stored in the search index) — `dotnet-service-builder`
- [ ] 5. Implement `GET api/search` endpoint in `SearchService.API/Controllers/` (searchTerm, pageSize, pageNumber, seller, winner, orderBy, filterBy) — `dotnet-service-builder`
- [ ] 6. Add HTTP polling fallback in `SearchService.Infrastructure/` to Auction Service (`GetAuctionsFromDate`) with Polly retry — `dotnet-service-builder`
- [ ] 7. Configure MassTransit Outbox pattern — `dotnet-service-builder`
- [ ] 8. Dockerize the Search Service (multi-project restore pattern for Clean Architecture; JSON console logging in the container environment — `Requirements.md` §13.5) — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 9. Write unit tests (SearchService.UnitTests) — `dotnet-service-builder`
  - [ ] 9.1. Search — returns paged results — `dotnet-service-builder`
  - [ ] 9.2. Search — filters by searchTerm — `dotnet-service-builder`
  - [ ] 9.3. Search — filters by seller — `dotnet-service-builder`
  - [ ] 9.4. Search — filters by winner — `dotnet-service-builder`
  - [ ] 9.5. Search — sorts by make — `dotnet-service-builder`
  - [ ] 9.6. Search — sorts by endingSoon — `dotnet-service-builder`
  - [ ] 9.7. Search — filters by status (live, finished, endingSoon) — `dotnet-service-builder`
- [ ] 10. Write integration tests (SearchService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 10.1. AuctionCreated consumer — inserts item into MongoDB — `dotnet-service-builder`
  - [ ] 10.2. AuctionUpdated consumer — updates item fields — `dotnet-service-builder`
  - [ ] 10.3. AuctionDeleted consumer — removes item — `dotnet-service-builder`
  - [ ] 10.4. Search endpoint — returns filtered results — `dotnet-service-builder`
- [ ] 11. Verify end-to-end: create auction → appears in Search Service via event — `test-runner`
- [ ] 12. Add API documentation: OpenAPI generation + Scalar UI (anonymous-only API — no security scheme needed) — `dotnet-service-builder`
- [ ] 13. Add global error handling: `IExceptionHandler` + ProblemDetails (see `Requirements.md` §13.1) — `dotnet-service-builder`
- [ ] 14. Add health endpoints: `GET /health/live` + `GET /health/ready` (MongoDB, RabbitMQ — see `Requirements.md` §13.4) — `dotnet-service-builder`

---

## Phase 3: Identity Service

**Goal:** Build a centralized authentication and authorization service.

**Purpose:** Issues JWT tokens via Duende IdentityServer, provides login/register UI, and secures backend services with bearer token authentication.

**Acceptance Criteria:**
- IdentityServer issues valid JWT tokens
- Login and register pages are functional
- Auction Service rejects unauthenticated requests to protected endpoints
- Default users are seeded
- Registration sends a confirmation email (visible in Mailpit); unconfirmed users receive 403 when creating auctions
- "Sign in with Google" works when the Google env vars are set, and yields an account with a confirmed email
- Registration rejects submissions without a valid Turnstile token; repeated failed logins lock the account
- Scalar docs login flow obtains a JWT via IdentityServer (authorization code + PKCE) and authenticated "try it" requests succeed
- Unit tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Identity Service project from Duende IdentityServer template with ASP.NET Core Identity — `dotnet-service-builder`
- [ ] 2. Configure PostgreSQL as the identity store (Npgsql) — `dotnet-service-builder`
- [ ] 3. Configure IdentityServer — `dotnet-service-builder`
  - [ ] 3.1. Clients — `dotnet-service-builder`
  - [ ] 3.2. Scopes — `dotnet-service-builder`
  - [ ] 3.3. Resources — `dotnet-service-builder`
  - [ ] 3.4. Include `username`, `email`, and `role` claims in access tokens (email for post-sale contact exchange; role for the admin dashboard in Phase 11) — `dotnet-service-builder`
- [ ] 4. Add Razor Pages for login/register UI — `dotnet-service-builder`
- [ ] 5. Seed default users per `Requirements.md` §8.1 (bob, alice, tom + admin with the `admin` role — confirmed emails, shared dev password) — `dotnet-service-builder`
- [ ] 6. Configure Polly retry for database connections during startup — `dotnet-service-builder`
- [ ] 7. Add JWT bearer authentication to Auction Service — `dotnet-service-builder`
- [ ] 8. Add JWT bearer authentication to Bidding Service (prep for Phase 5) — `dotnet-service-builder`
- [ ] 9. Dockerize the Identity Service (JSON console logging in the container environment — `Requirements.md` §13.5) — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 10. Write unit tests (IdentityService.UnitTests) — `dotnet-service-builder`
  - [ ] 10.1. Login — valid credentials returns token — `dotnet-service-builder`
  - [ ] 10.2. Login — invalid credentials returns 401 — `dotnet-service-builder`
  - [ ] 10.3. Register — valid data creates user — `dotnet-service-builder`
  - [ ] 10.4. Register — duplicate username returns error — `dotnet-service-builder`
- [ ] 11. Write integration tests (IdentityService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 11.1. Token endpoint — returns valid JWT with correct claims — `dotnet-service-builder`
  - [ ] 11.2. Protected endpoint — rejects request without token — `dotnet-service-builder`
  - [ ] 11.3. Protected endpoint — accepts request with valid token — `dotnet-service-builder`
- [ ] 12. Verify end-to-end: obtain token → call authenticated Auction Service endpoints — `test-runner`
- [ ] 13. Enable login-from-docs (Scalar ↔ IdentityServer) — `dotnet-service-builder`
  - [ ] 13.1. Register a `scalar` client in `Config.cs` (authorization code + PKCE, public client without secret, redirect URIs for the Scalar docs pages) — `dotnet-service-builder`
  - [ ] 13.2. Enable CORS on the IdentityServer token endpoint for browser-based code exchange from the docs pages — `dotnet-service-builder`
  - [ ] 13.3. Switch the Auction Service Scalar config to the OAuth2 authorization code flow with PKCE (`AddAuthorizationCodeFlow`) — `dotnet-service-builder`
- [ ] 14. Add email verification — `dotnet-service-builder`
  - [ ] 14.1. Enable `RequireConfirmedEmail` and the confirmation flow in the register UI (confirmation link) — `dotnet-service-builder`
  - [ ] 14.2. Email sender: SMTP to Mailpit in dev (`localhost:1025`, no credentials); production SMTP credentials via environment variables — `dotnet-service-builder`
  - [ ] 14.3. Include the `email_verified` claim in access tokens — `dotnet-service-builder`
  - [ ] 14.4. Require the `email_verified` claim for `POST api/auctions` in the Auction Service (403 otherwise) — `dotnet-service-builder`
- [ ] 15. Add Google external login — `dotnet-service-builder`
  - [ ] 15.1. Add `Microsoft.AspNetCore.Authentication.Google`; client ID/secret from environment variables only (real external credentials — never committed; Google login is disabled when the variables are absent) — `dotnet-service-builder`
  - [ ] 15.2. "Sign in with Google" on the login/register pages; treat Google-asserted verified emails as confirmed — `dotnet-service-builder`
- [ ] 16. Add bot protection — `dotnet-service-builder`
  - [ ] 16.1. Cloudflare Turnstile widget on the register page + server-side `siteverify` validation of the token (plain `HttpClient`) — `dotnet-service-builder`
  - [ ] 16.2. Dev/Docker use Cloudflare's official always-pass test keys (committed — published for this purpose); production keys via environment variables — `dotnet-service-builder`
  - [ ] 16.3. Enable ASP.NET Core Identity account lockout on repeated failed logins — `dotnet-service-builder`
  - [ ] 16.4. Rate limit the login, register, and token endpoints (`Microsoft.AspNetCore.RateLimiting`, limits from configuration) — `dotnet-service-builder`
- [ ] 17. Add global error handling: `IExceptionHandler` + ProblemDetails for the API endpoints (see `Requirements.md` §13.1) — `dotnet-service-builder`
- [ ] 18. Add health endpoints: `GET /health/live` + `GET /health/ready` (PostgreSQL — see `Requirements.md` §13.4) — `dotnet-service-builder`
- [ ] 19. Add an `EmailVerified` authorization policy (`[Authorize(Policy = "EmailVerified")]`) and apply it uniformly to all mutating auction endpoints (POST/PUT/DELETE) — replaces the ad-hoc per-endpoint email_verified check currently only on create — `dotnet-service-builder`

---

## Phase 4: Gateway Service

**Goal:** Build a single entry point that routes all client requests to backend services.

**Purpose:** Provides a unified API surface via YARP reverse proxy, handles JWT validation at the edge, and decouples clients from individual service URLs.

**Acceptance Criteria:**
- All API routes are proxied correctly to their respective services
- JWT authentication is validated at the gateway
- Both authenticated and anonymous endpoints work through the gateway
- Requests exceeding the rate limit receive 429
- Aggregated Scalar docs page serves all service APIs through the gateway, with one OAuth2 login covering all "try it" requests
- Integration tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Gateway Service project with NuGet packages — `dotnet-service-builder`
- [ ] 2. Configure YARP reverse proxy routes — `dotnet-service-builder`
  - [ ] 2.1. `/api/auctions/*` → Auction Service — `dotnet-service-builder`
  - [ ] 2.2. `/api/search*` → Search Service — `dotnet-service-builder`
  - [ ] 2.3. `/api/bids/*` → Bidding Service (prep for Phase 5) — `dotnet-service-builder`
  - [ ] 2.4. `/notifications` → Notification Service (prep for Phase 6) — `dotnet-service-builder`
- [ ] 3. Configure JWT bearer authentication on the gateway — `dotnet-service-builder`
- [ ] 4. Dockerize the Gateway Service (JSON console logging in the container environment — `Requirements.md` §13.5) — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 5. Write integration tests (GatewayService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 5.1. Route `/api/auctions` — proxies to Auction Service — `dotnet-service-builder`
  - [ ] 5.2. Route `/api/search` — proxies to Search Service — `dotnet-service-builder`
  - [ ] 5.3. Auth endpoint — rejects request without token — `dotnet-service-builder`
  - [ ] 5.4. Auth endpoint — accepts request with valid token — `dotnet-service-builder`
  - [ ] 5.5. Anon endpoint — accepts request without token — `dotnet-service-builder`
- [ ] 6. Verify end-to-end: client → gateway → backend services (auth and anon) — `test-runner`
- [ ] 7. Aggregate API documentation at the gateway — `dotnet-service-builder`
  - [ ] 7.1. Proxy each service's `/openapi/v1.json` through YARP — `dotnet-service-builder`
  - [ ] 7.2. Host a single Scalar UI at the gateway listing all service documents (`AddDocument` per service) — `dotnet-service-builder`
  - [ ] 7.3. Configure the OAuth2 authorization code flow (PKCE, `scalar` client) against IdentityServer so one login covers all "try it" requests — `dotnet-service-builder`
- [ ] 8. Add rate limiting at the gateway (`Microsoft.AspNetCore.RateLimiting`) — `dotnet-service-builder`
  - [ ] 8.1. General per-IP fixed-window policy on all proxied routes; stricter policy on mutating endpoints (`POST api/bids`, `POST/PUT/DELETE api/auctions`); limits from configuration — `dotnet-service-builder`
  - [ ] 8.2. Integration test — exceeding the limit returns 429 — `dotnet-service-builder`
- [ ] 9. Expose `GET api/version` (Anon) — handled by the gateway itself, not proxied; returns the platform version from assembly metadata (see `Docs/Versioning.md`) — `dotnet-service-builder`
- [ ] 10. Return ProblemDetails for gateway-generated errors (edge 401/403, 429 rate limiting); proxied service errors pass through unchanged (see `Requirements.md` §13.1) — `dotnet-service-builder`
- [ ] 11. Add health endpoints: `GET /health/live` + `GET /health/ready` — gateway-only checks, no downstream fan-out (see `Requirements.md` §13.4) — `dotnet-service-builder`

---

## Phase 5: Bidding Service

**Goal:** Build a service that handles bid placement, validation, and auction completion.

**Purpose:** Manages the bidding lifecycle — validates bids against business rules, uses gRPC to fetch auction data when needed, and runs a background job to finalize expired auctions.

**Acceptance Criteria:**
- Bids are validated (amount logic, seller check, auction status)
- gRPC fallback to Auction Service works for missing auction data
- Background service detects and finalizes expired auctions
- `BidPlaced` and `AuctionFinished` events are published
- After a sale, the seller and winner can see each other's email via `GET api/auctions/{id}` — no other caller can
- OpenAPI document and Scalar docs UI are served with the OAuth2/Bearer security scheme
- Unit tests and integration tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Bidding Service Clean Architecture projects (Domain, Application, Infrastructure, API) with correct NuGet packages per layer — `dotnet-service-builder`
- [ ] 2. Define models in `BiddingService.Domain/` — `dotnet-service-builder`
  - [ ] 2.1. `Bid` entity in `BiddingService.Domain/Entities/` (includes Bidder and `BidderEmail` from claims; email is never returned by the bids API) — `dotnet-service-builder`
  - [ ] 2.2. `BidStatus` enum in `BiddingService.Domain/Enums/` — `dotnet-service-builder`
  - [ ] 2.3. Local `Auction` entity in `BiddingService.Domain/Entities/` — `dotnet-service-builder`
- [ ] 3. Set up MongoDB connection via `MongoDB.Entities` in `BiddingService.Infrastructure/Data/` — `dotnet-service-builder`
- [ ] 4. Configure MassTransit with Outbox pattern — `dotnet-service-builder`
- [ ] 5. Implement event consumer for `AuctionCreated` in `BiddingService.Application/Consumers/` (store local auction record) — `dotnet-service-builder`
- [ ] 6. Implement gRPC client in `BiddingService.Infrastructure/` to call Auction Service (fallback for missing auction data) — `dotnet-service-builder`
- [ ] 7. Configure Polly retry policies for gRPC calls in `BiddingService.Infrastructure/` — `dotnet-service-builder`
- [ ] 8. Implement gRPC server endpoint in Auction Service (`GetAuction`) — `dotnet-service-builder`
- [ ] 9. Implement API endpoints in `BiddingService.API/Controllers/` — `dotnet-service-builder`
  - [ ] 9.1. `POST api/bids` — place bid (Auth) — `dotnet-service-builder`
  - [ ] 9.2. `GET api/bids/{auctionId}` — get bids for auction (Anon) — `dotnet-service-builder`
- [ ] 10. Implement bid validation and status logic — `dotnet-service-builder`
  - [ ] 10.1. Reject bid when bidder is the auction's seller — 400 Bad Request — `dotnet-service-builder`
  - [ ] 10.2. Accepted — bid > current high bid and > reserve price — `dotnet-service-builder`
  - [ ] 10.3. AcceptedBelowReserve — bid > current high bid but < reserve price — `dotnet-service-builder`
  - [ ] 10.4. TooLow — bid <= current high bid — `dotnet-service-builder`
  - [ ] 10.5. Finished — auction already ended — `dotnet-service-builder`
- [ ] 11. Publish events: `BidPlaced`, `AuctionFinished` — `dotnet-service-builder`
- [ ] 12. Implement background service (check auctions past `AuctionEnd`, emit `AuctionFinished` — set `WinnerEmail` from the winning bid's `BidderEmail` when sold; check interval from `Bidding__FinalizationIntervalSeconds`, default 10s, so short dev auctions finalize promptly) — `dotnet-service-builder`
- [ ] 13. Add JWT bearer authentication (require the `email_verified` claim for `POST api/bids` — 403 otherwise) — `dotnet-service-builder`
- [ ] 14. Dockerize the Bidding Service (multi-project restore pattern for Clean Architecture; JSON console logging in the container environment — `Requirements.md` §13.5) — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 15. Write unit tests (BiddingService.UnitTests) — `dotnet-service-builder`
  - [ ] 15.1. PlaceBid — valid bid returns Accepted — `dotnet-service-builder`
  - [ ] 15.2. PlaceBid — bid below reserve returns AcceptedBelowReserve — `dotnet-service-builder`
  - [ ] 15.3. PlaceBid — bid too low returns TooLow — `dotnet-service-builder`
  - [ ] 15.4. PlaceBid — auction finished returns Finished — `dotnet-service-builder`
  - [ ] 15.5. PlaceBid — bidder is seller returns 400 — `dotnet-service-builder`
  - [ ] 15.6. PlaceBid — auction not found triggers gRPC fallback — `dotnet-service-builder`
  - [ ] 15.7. GetBids — returns bids for auction — `dotnet-service-builder`
- [ ] 16. Write integration tests (BiddingService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 16.1. PlaceBid — valid bid publishes `BidPlaced` event — `dotnet-service-builder`
  - [ ] 16.2. PlaceBid — unauthenticated returns 401 — `dotnet-service-builder`
  - [ ] 16.3. AuctionCreated consumer — stores local auction record — `dotnet-service-builder`
  - [ ] 16.4. Background service — finalizes expired auction — `dotnet-service-builder`
- [ ] 17. Verify end-to-end: place bid → Auction Service updates CurrentHighBid → Search Service updates — `test-runner`
- [ ] 18. Add API documentation: OpenAPI generation + Scalar UI with the OAuth2/Bearer security scheme (same pattern as Auction Service) — `dotnet-service-builder`
- [ ] 19. Implement post-sale contact exchange in the Auction Service — `dotnet-service-builder`
  - [ ] 19.1. `GET api/auctions/{id}`: once sold, include `WinnerEmail` only when the caller is the seller, and `SellerEmail` only when the caller is the winner — `dotnet-service-builder`
  - [ ] 19.2. Unit tests: seller sees WinnerEmail, winner sees SellerEmail, everyone else (incl. anonymous) sees neither — `dotnet-service-builder`
- [ ] 20. Seed local auction records and bid history per `Requirements.md` §8.3 (bids carry `BidderEmail`; states consistent with the Auction Service seed) — `dotnet-service-builder`
- [ ] 21. Add global error handling: `IExceptionHandler` + ProblemDetails (see `Requirements.md` §13.1; bid outcomes like TooLow/Finished are normal responses, not errors) — `dotnet-service-builder`
- [ ] 22. Add health endpoints: `GET /health/live` + `GET /health/ready` (MongoDB, RabbitMQ — see `Requirements.md` §13.4) — `dotnet-service-builder`

---

## Phase 6: Notification Service

**Goal:** Build a real-time notification service using SignalR.

**Purpose:** Pushes live updates to connected browser clients whenever auctions are created, bids are placed, or auctions finish — enabling a reactive UI without polling.

**Acceptance Criteria:**
- SignalR hub accepts client connections at `/notifications`
- Clients receive real-time notifications for `AuctionCreated`, `BidPlaced`, and `AuctionFinished`
- When an auction finishes, the winner receives a targeted `AuctionWon` message and the seller a targeted `AuctionSellerResult` message (authenticated connections only)
- Integration tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Notification Service project with NuGet packages — `dotnet-service-builder`
- [ ] 2. Configure MassTransit with RabbitMQ (consumer-only — no outbox; this service has no database and publishes no events) — `dotnet-service-builder`
- [ ] 3. Create SignalR hub at `/notifications` — `dotnet-service-builder`
  - [ ] 3.1. Allow anonymous connections (broadcasts) — `dotnet-service-builder`
  - [ ] 3.2. Add JWT bearer authentication (`access_token` query param) + username-based `IUserIdProvider` for targeted messages — `dotnet-service-builder`
- [ ] 4. Implement event consumers that push to SignalR clients — `dotnet-service-builder`
  - [ ] 4.1. `AuctionCreated` — notify clients of new auction — `dotnet-service-builder`
  - [ ] 4.2. `BidPlaced` — notify clients of new bid — `dotnet-service-builder`
  - [ ] 4.3. `AuctionFinished` — notify clients of auction result — `dotnet-service-builder`
  - [ ] 4.4. `AuctionFinished` — additionally send targeted `AuctionWon` to the winner (when ItemSold) and `AuctionSellerResult` to the seller via `Clients.User(...)` — `dotnet-service-builder`
- [ ] 5. Dockerize the Notification Service (JSON console logging in the container environment — `Requirements.md` §13.5) — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 6. Write integration tests (NotificationService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 6.1. AuctionCreated consumer — pushes notification to SignalR clients — `dotnet-service-builder`
  - [ ] 6.2. BidPlaced consumer — pushes notification to SignalR clients — `dotnet-service-builder`
  - [ ] 6.3. AuctionFinished consumer — pushes notification to SignalR clients — `dotnet-service-builder`
  - [ ] 6.4. SignalR hub — client connects and receives messages — `dotnet-service-builder`
  - [ ] 6.5. AuctionFinished consumer — winner and seller receive targeted messages; anonymous clients receive only the broadcast — `dotnet-service-builder`
- [ ] 7. Verify end-to-end: place bid → notification pushed to connected SignalR client — `test-runner`
- [ ] 8. Add health endpoints: `GET /health/live` + `GET /health/ready` (RabbitMQ — see `Requirements.md` §13.4) — `dotnet-service-builder`

---

## Phase 7: Frontend (Next.js Client App)

**Goal:** Build the user-facing web application.

**Purpose:** Provides the complete UI for browsing auctions, placing bids, managing listings, and receiving real-time updates — all authenticated through IdentityServer.

**Acceptance Criteria:**
- Users can browse, search, filter, and sort auctions
- Users can log in, create/edit/delete auctions, and place bids
- Bid updates appear in real-time via SignalR
- Playwright e2e tests pass
- App runs in Docker with optimized production build

### Tasks

- [ ] 1. Create the Next.js project (`frontend/web-app`) with dependencies — `frontend-builder`
- [ ] 2. Configure Tailwind CSS and Flowbite React — theme tokens (Teal Breeze `primary` scale, `accent` colors) and Inter font per `Docs/DesignGuide.md` — `frontend-builder`
- [ ] 3. Set up next-auth with Duende IdentityServer (OAuth2/OIDC) — `frontend-builder`
- [ ] 4. Implement auction listing page — `frontend-builder`
  - [ ] 4.1. Fetch from Search Service (via Gateway) — `frontend-builder`
  - [ ] 4.2. Pagination, filtering (live, endingSoon, finished), sorting (make, new, endingSoon) — `frontend-builder`
  - [ ] 4.3. Search by term, seller, winner — `frontend-builder`
- [ ] 5. Implement auction detail page — `frontend-builder`
  - [ ] 5.1. Display DetailedSpecs (seller, make, model, year, mileage, reserve price) — `frontend-builder`
  - [ ] 5.2. Auction countdown timer (`react-countdown`) — `frontend-builder`
  - [ ] 5.3. Bid history list — `frontend-builder`
  - [ ] 5.4. Post-sale contact info on sold auctions (seller sees winner's email, winner sees seller's email) — `frontend-builder`
  - [ ] 5.5. Social share buttons — Facebook/X/WhatsApp share intents + native Web Share API button (`navigator.share`; covers Instagram via the mobile share sheet) — `frontend-builder`
  - [ ] 5.6. Open Graph + Twitter Card metadata via `generateMetadata` (og:title, og:description, og:image from the primary image's thumbnail with primary-image fallback, summary_large_image) for rich link previews in WhatsApp/iMessage/Slack/social apps — `frontend-builder`
  - [ ] 5.7. Image gallery — primary image first, click/swipe navigation through the remaining images (listing cards elsewhere show only the primary thumbnail) — `frontend-builder`
- [ ] 6. Implement auction create/edit form — `frontend-builder`
  - [ ] 6.1. `react-hook-form` for form handling — `frontend-builder`
  - [ ] 6.2. `react-datepicker` for auction end date — `frontend-builder`
  - [ ] 6.3. Image upload — multi-file picker (1–10 images, client-side ≤5 MB pre-validation) uploading each file direct to storage via presigned PUT (`upload-url` endpoint), drag-to-reorder to set the primary image, with a plain URL input as fallback — `frontend-builder`
  - [ ] 6.4. Optional "Generate thumbnail" step after upload (calls `POST api/auctions/thumbnail` per image, previews the result) — `frontend-builder`
- [ ] 7. Implement auction delete (with confirmation) — `frontend-builder`
- [ ] 8. Implement bid placement UI — `frontend-builder`
  - [ ] 8.1. Bid input and submit — `frontend-builder`
  - [ ] 8.2. Real-time bid updates via SignalR (`@microsoft/signalr`) — `frontend-builder`
  - [ ] 8.3. Connect to the hub with the access token when logged in; show targeted "You won" toast (winner) and auction-result toast (seller) — `frontend-builder`
- [ ] 9. Set up Zustand store for client-side state management — `frontend-builder`
- [ ] 10. Add toast notifications (`react-hot-toast`) — `frontend-builder`
- [ ] 11. Add currency formatting helper (`numberWithCommas`) — `frontend-builder`
- [ ] 12. Configure Next.js image optimization (`sharp`) with `remotePatterns` whitelisting the MinIO/object-storage host from an environment variable — `frontend-builder`
- [ ] 13. Dockerize the Next.js app (multi-stage build, standalone output, non-root user) — `frontend-builder`, verify with `docker-validator`
- [ ] 14. Set up Playwright test project — `frontend-builder`
- [ ] 15. Write Playwright e2e tests — `frontend-builder`, run with `playwright-tester`
  - [ ] 15.1. Home page — loads auction listings — `frontend-builder`
  - [ ] 15.2. Search — filters auctions by search term — `frontend-builder`
  - [ ] 15.3. Pagination — navigates between pages — `frontend-builder`
  - [ ] 15.4. Filtering — filters by live, endingSoon, finished — `frontend-builder`
  - [ ] 15.5. Sorting — sorts by make, new, endingSoon — `frontend-builder`
  - [ ] 15.6. Auth — login flow via IdentityServer — `frontend-builder`
  - [ ] 15.7. Auth — logout returns to home page — `frontend-builder`
  - [ ] 15.8. Auth — unauthenticated user cannot access create page — `frontend-builder`
  - [ ] 15.9. Create auction — fills form and submits successfully — `frontend-builder`
  - [ ] 15.10. Edit auction — updates auction details — `frontend-builder`
  - [ ] 15.11. Delete auction — removes auction with confirmation — `frontend-builder`
  - [ ] 15.12. Auction detail — displays specs, countdown, and bid history — `frontend-builder`
  - [ ] 15.13. Place bid — submits bid and updates UI — `frontend-builder`
  - [ ] 15.14. Real-time — bid placed by another user appears without refresh — `frontend-builder`
  - [ ] 15.15. Toast notifications — displays on success and error actions — `frontend-builder`
  - [ ] 15.16. Email verification — register a new user, fetch the confirmation link via the Mailpit API, confirm, then create an auction successfully — `frontend-builder`
  - [ ] 15.17. Image upload — create an auction with multiple file uploads; the images land in storage and the gallery renders on the detail page (primary image first) — `frontend-builder`
  - [ ] 15.18. Link preview metadata — the auction detail page head contains og:title, og:description, og:image, and twitter:card tags — `frontend-builder`
- [ ] 16. Page footer shows the frontend version (`package.json`) and the backend version fetched from `GET api/version` (see `Docs/Versioning.md`) — `frontend-builder`
- [ ] 17. Verify end-to-end: full user flow (browse → login → create auction → bid → real-time updates) — `playwright-tester`
- [ ] 18. Add global error boundaries: root `global-error.tsx`, route-level `error.tsx` (friendly message + "Try again" reset), and `not-found.tsx` — styled per `Docs/DesignGuide.md` (see `Requirements.md` §13.2) — `frontend-builder`
- [ ] 19. Surface API ProblemDetails failures as red toasts (`title` only — never `detail` or stack traces in production; see `Requirements.md` §13.2) — `frontend-builder`
- [ ] 20. Add `GET /api/health` route handler returning 200 (see `Requirements.md` §13.4) — `frontend-builder`

---

## Phase 8: Docker Compose Deployment

**Goal:** Run the entire application stack locally with a single command.

**Purpose:** Validates that all services work together in a containerized environment, with proper networking, environment config, and an Nginx reverse proxy with SSL.

**Acceptance Criteria:**
- `docker compose up` starts all infrastructure, backend, and frontend services
- All services communicate correctly within the Docker network
- Application is accessible via Nginx proxy
- All Postman collection tests pass
- Full user flows work end-to-end

### Tasks

- [ ] 1. Create `docker/docker-compose.yml` — *main conversation*, validate with `docker-validator`
  - [ ] 1.1. Infrastructure services: PostgreSQL, MongoDB, RabbitMQ, Mailpit (dev email catcher), MinIO + `mc` bucket/seed-image init — *main conversation*
  - [ ] 1.2. Backend services: Auction, Search, Bidding, Identity, Gateway, Notification — *main conversation*
  - [ ] 1.3. Frontend: Next.js web app — *main conversation*
  - [ ] 1.4. `healthcheck` blocks for infrastructure and app services (`/health/live`–`/health/ready`, web app `/api/health` — see `Requirements.md` §13.4) and startup ordering via `depends_on: condition: service_healthy` — *main conversation*
- [ ] 2. Configure environment variables and connection strings for all services (dev-only values inline in `docker-compose.yml` — committed by design, see `Requirements.md` §6) — *main conversation*, validate with `docker-validator`
- [ ] 3. Configure inter-service networking — *main conversation*, validate with `docker-validator`
- [ ] 4. Set up Nginx reverse proxy with SSL (via acme-companion) — *main conversation*, validate with `docker-validator`
- [ ] 5. Verify full stack runs with `docker compose up` — `docker-validator`
- [ ] 6. Create a Postman collection covering all API endpoints, verify it passes against the running stack, and commit it at `Docs/postman/ApexAutoBid.postman_collection.json` — *main conversation*
- [ ] 7. Test all user flows end-to-end in the containerized environment — `playwright-tester`

---

## Phase 9: Kubernetes Local Deployment

**Goal:** Deploy the application to a local Kubernetes cluster.

**Purpose:** Validates Kubernetes manifests and service orchestration locally before deploying to production, ensuring all deployments, services, secrets, and ingress rules are correct.

**Acceptance Criteria:**
- All Kubernetes manifests apply without errors
- All pods are running and healthy
- Services communicate correctly within the cluster
- Application is accessible via ingress endpoint

### Tasks

- [ ] 1. Create Kubernetes manifests — *main conversation*
  - [ ] 1.1. `dev-secrets.yaml` — secrets for databases, RabbitMQ, and app config (dev-only values, committed by design; production secrets are handled separately in Phase 10) — *main conversation*
  - [ ] 1.2. Deployment + ClusterIP for PostgreSQL — *main conversation*
  - [ ] 1.3. Deployment + ClusterIP for MongoDB — *main conversation*
  - [ ] 1.4. Deployment + ClusterIP for RabbitMQ — *main conversation*
  - [ ] 1.5. Deployment + ClusterIP for Auction Service — *main conversation*
  - [ ] 1.6. Deployment + ClusterIP for Search Service — *main conversation*
  - [ ] 1.7. Deployment + ClusterIP for Bidding Service — *main conversation*
  - [ ] 1.8. Deployment + ClusterIP for Identity Service — *main conversation*
  - [ ] 1.9. Deployment + ClusterIP for Gateway Service — *main conversation*
  - [ ] 1.10. Deployment + ClusterIP for Notification Service — *main conversation*
  - [ ] 1.11. Deployment + ClusterIP for Web App — *main conversation*
  - [ ] 1.12. Ingress controller configuration — *main conversation*
  - [ ] 1.13. Deployment + ClusterIP + PersistentVolumeClaim for MinIO, plus a bucket/seed-image init Job — *main conversation*
  - [ ] 1.14. Liveness (`/health/live`) and readiness (`/health/ready`) probes on every service Deployment; web app probes `/api/health` (see `Requirements.md` §13.4) — *main conversation*
- [ ] 2. Deploy to local Kubernetes (Minikube or Docker Desktop K8s) — *main conversation*
- [ ] 3. Verify all services are running and communicating — *main conversation*
- [ ] 4. Test full application via ingress endpoint — `playwright-tester`

---

## Phase 10: CI/CD & Cloud Kubernetes Deployment

**Goal:** Automate builds and deploy the application to a cloud Kubernetes cluster.

**Purpose:** Establishes CI/CD pipelines so that pushing to `main` automatically builds, pushes Docker images, and deploys to a production cluster accessible on the internet.

**Acceptance Criteria:**
- Pull requests to `develop` and `main` run the full test suite and must pass before merge (`main` is branch-protected)
- GitHub Actions deploy workflows trigger on push to `main` for each service
- Docker images are built, tagged with the commit SHA (plus `latest`), and pushed to Docker Hub automatically
- The deploy workflow rolls the cluster to the new SHA tag — no manual step after merge
- Application is deployed and accessible on the internet
- DNS and ingress are configured for public access

### Tasks

- [ ] 1. Create the PR validation workflow (`.github/workflows/ci.yml`) — *main conversation*
  - [ ] 1.1. Trigger on pull requests to `develop` and `main`, and on pushes to `develop` — *main conversation*
  - [ ] 1.2. Backend job: restore, build, and run all `dotnet test` projects — *main conversation*
  - [ ] 1.3. Frontend job: install, lint, and build the Next.js app — *main conversation*
- [ ] 2. Enable branch protection on `main` — require a pull request and a green CI run before merge — *main conversation*
- [ ] 3. Create per-service deploy workflows — *main conversation*
  - [ ] 3.1. Trigger on push to `main` for relevant service paths — *main conversation*
  - [ ] 3.2. Build the Docker image — *main conversation*
  - [ ] 3.3. Tag with the commit SHA and `latest`; push both tags to Docker Hub — *main conversation*
  - [ ] 3.4. Deploy job: roll the cluster to the SHA tag (`kubectl set image`) using the kubeconfig from repository secrets — *main conversation*
- [ ] 4. Configure GitHub repository secrets (Docker Hub credentials, production kubeconfig) — *main conversation*
- [ ] 5. Set up production Kubernetes cluster (cloud provider) — *main conversation*
- [ ] 6. Configure production secrets and environment variables (applied directly to the cluster from a local, untracked manifest — never committed) — *main conversation*
- [ ] 7. Initial deployment: apply all manifests to the cloud cluster (subsequent image updates flow through the deploy workflows) — *main conversation*
- [ ] 8. Configure DNS and ingress for public access — *main conversation*
- [ ] 9. Verify production deployment end-to-end — `playwright-tester`

---

## Phase 11: Admin Dashboard

**Goal:** Build a role-gated admin area for managing users, auctions, bids, and banner messages, with platform statistics.

**Purpose:** Gives operators the tools to run the platform — user administration, auction/bid moderation, and site-wide announcements — using the `admin` role claim seeded in Phase 3. No new service is added; each capability lives in the service that owns the data (see `Requirements.md` §10). Can be built any time after Phase 7; deployment phases (8–10) need no changes for it.

**Acceptance Criteria:**
- Every `api/admin/*` endpoint returns 403 for non-admin callers
- Admin can list/search/create users, reset passwords, resend confirmation links, assign roles, and lock accounts
- Admin can create auctions for any seller, end auctions early, cancel auctions, and remove bids — with all changes propagated to Search/Bidding/Notification via events
- Banners published by an admin appear on the home/auction pages without a refresh (SignalR)
- The dashboard shows user, auction, and bid statistics
- Unit, integration, and Playwright admin tests pass

### Tasks

- [ ] 1. Extend the `Contracts` project: `AuctionCancelled`, `BidRemoved`, `BannerPublished`; add `AuctionEnd?` to `AuctionUpdated` (see `Requirements.md` §10.5) — `dotnet-service-builder`
- [ ] 2. Identity Service admin API (`Requirements.md` §10.1) — `dotnet-service-builder`
  - [ ] 2.1. `GET api/admin/users` — list/search users (paged) — `dotnet-service-builder`
  - [ ] 2.2. `POST api/admin/users` — create user (optionally pre-confirmed) — `dotnet-service-builder`
  - [ ] 2.3. `POST api/admin/users/{id}/reset-password` — temp password or email reset link — `dotnet-service-builder`
  - [ ] 2.4. `POST api/admin/users/{id}/resend-confirmation` — generate a new email-validation link — `dotnet-service-builder`
  - [ ] 2.5. `PUT api/admin/users/{id}/roles` — assign/remove roles — `dotnet-service-builder`
  - [ ] 2.6. `PUT api/admin/users/{id}/lock` — lock/unlock account — `dotnet-service-builder`
  - [ ] 2.7. `GET api/admin/users/stats` — user counts (total, confirmed, locked) — `dotnet-service-builder`
  - [ ] 2.8. Document the admin API with OpenAPI + Scalar (Bearer, admin role noted) and add it to the gateway's aggregated docs — `dotnet-service-builder`
  - [ ] 2.9. Write `AuditEntry` records for all admin user-management actions (see `Requirements.md` §13.3) — `dotnet-service-builder`
- [ ] 3. Auction Service admin features (`Requirements.md` §10.2–10.3) — `dotnet-service-builder`
  - [ ] 3.1. Honor an explicit `Seller` on `POST api/auctions` for admin callers only — `dotnet-service-builder`
  - [ ] 3.2. `POST api/admin/auctions/{id}/end` — set `AuctionEnd = UtcNow`, emit `AuctionUpdated` with `AuctionEnd` — `dotnet-service-builder`
  - [ ] 3.3. `POST api/admin/auctions/{id}/cancel` — status `Cancelled`, emit `AuctionCancelled` — `dotnet-service-builder`
  - [ ] 3.4. Auction duration validation on create/update — resolution order: DB `PlatformSettings` → env vars (`Auction__MinDuration`/`Auction__MaxDuration`) → defaults 1 hour–90 days; dev sets min to 1 minute (`appsettings.Development.json` / compose); admins exempt — `dotnet-service-builder`
  - [ ] 3.5. `Banner` entity + admin CRUD (`GET/POST api/admin/banners`, `PUT/DELETE api/admin/banners/{id}`) + public `GET api/banners` (Anon); emit `BannerPublished` — `dotnet-service-builder`
  - [ ] 3.6. Consume `BidRemoved` — refresh `CurrentHighBid` — `dotnet-service-builder`
  - [ ] 3.7. `GET api/admin/auctions/stats` — auction counts by status — `dotnet-service-builder`
  - [ ] 3.8. `PlatformSettings` entity + `GET/PUT api/admin/settings/duration` (DB-backed min/max, takes effect immediately) + anon `GET api/auctions/duration-limits` for the create form — `dotnet-service-builder`
  - [ ] 3.9. Write `AuditEntry` records for admin auction moderation (end/cancel), banner CRUD, and settings changes (see `Requirements.md` §13.3) — `dotnet-service-builder`
- [ ] 4. Search Service: consume `AuctionCancelled` (status) and `BidRemoved` (CurrentHighBid); include `cancelled` in status filtering — `dotnet-service-builder`
- [ ] 5. Bidding Service admin features — `dotnet-service-builder`
  - [ ] 5.1. `DELETE api/admin/bids/{id}` — remove bid, recalculate high bid, emit `BidRemoved` — `dotnet-service-builder`
  - [ ] 5.2. Consume `AuctionCancelled` — mark local auction finished; refuse further bids; never emit `AuctionFinished` — `dotnet-service-builder`
  - [ ] 5.3. Apply `AuctionUpdated.AuctionEnd` to local auction records — `dotnet-service-builder`
  - [ ] 5.4. `GET api/admin/bids/stats` — total bid count — `dotnet-service-builder`
  - [ ] 5.5. Write an `AuditEntry` for admin bid removal capturing the removed bid (see `Requirements.md` §13.3) — `dotnet-service-builder`
- [ ] 6. Notification Service: consume `AuctionCancelled` (broadcast + targeted to seller) and `BannerPublished` (broadcast) — `dotnet-service-builder`
- [ ] 7. Gateway: route `api/admin/*` paths by resource segment, matching the base collection path as well as child paths (`users*` → Identity, `auctions*`/`banners*`/`settings*` → Auction, `bids*` → Bidding) and require the `admin` role claim at the edge (defense in depth — services still enforce it) — `dotnet-service-builder`
- [ ] 8. Frontend admin area (`/admin`, role-gated) — `frontend-builder`
  - [ ] 8.1. Role-based route protection (role claim from the session; non-admins redirected) — `frontend-builder`
  - [ ] 8.2. Dashboard page — stats cards from the per-service stats endpoints (`api/admin/users/stats`, `api/admin/auctions/stats`, `api/admin/bids/stats`) — `frontend-builder`
  - [ ] 8.3. Users page — list/search, create, reset password, resend confirmation, roles, lock — `frontend-builder`
  - [ ] 8.4. Auctions page — create with seller assignment, end now, cancel, remove bid from the bid history — `frontend-builder`
  - [ ] 8.5. Banners page — CRUD with scope (global/home/auction) and active window — `frontend-builder`
  - [ ] 8.6. Render active banners on the home page and auction detail pages, live-updating on `BannerPublished` — `frontend-builder`
  - [ ] 8.7. Settings page — edit the platform min/max auction duration (`PUT api/admin/settings/duration`) — `frontend-builder`
- [ ] 9. Write unit tests — `dotnet-service-builder`
  - [ ] 9.1. Admin endpoints return 403 for non-admin callers (each service) — `dotnet-service-builder`
  - [ ] 9.2. RemoveBid — recalculates the high bid and emits `BidRemoved` — `dotnet-service-builder`
  - [ ] 9.3. PlaceBid — cancelled auction refuses bids — `dotnet-service-builder`
  - [ ] 9.4. CreateAuction — `AuctionEnd` outside the configured duration bounds returns 400 (non-admin) — `dotnet-service-builder`
  - [ ] 9.5. CreateAuction — non-admin passing an explicit `Seller` is ignored/rejected — `dotnet-service-builder`
  - [ ] 9.6. Admin moderation actions write `AuditEntry` records (each auditing service) — `dotnet-service-builder`
- [ ] 10. Write Playwright admin e2e tests — `frontend-builder`, run with `playwright-tester`
  - [ ] 10.1. Admin sees `/admin`; a regular user is redirected away — `frontend-builder`
  - [ ] 10.2. Create a user and reset their password — `frontend-builder`
  - [ ] 10.3. Create an auction with an assigned seller — `frontend-builder`
  - [ ] 10.4. End an auction now; cancel an auction — `frontend-builder`
  - [ ] 10.5. Publish a banner — it appears on the home page without a refresh — `frontend-builder`
- [ ] 11. Verify end-to-end admin flows (moderation events propagate to Search/Bidding/Notification) — `test-runner`
