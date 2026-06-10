# ApexAutoBid - Task Tracker

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

- [ ] 1. Create the solution structure (`ApexAutoBid.sln`, `backend/`, `tests/`, `.editorconfig`, `.vscode/extensions.json`) — `dotnet-service-builder`
- [ ] 2. Create the `Contracts` shared project — `dotnet-service-builder`
  - [ ] 2.1. `AuctionCreated` event contract — `dotnet-service-builder`
  - [ ] 2.2. `AuctionUpdated` event contract — `dotnet-service-builder`
  - [ ] 2.3. `AuctionDeleted` event contract — `dotnet-service-builder`
  - [ ] 2.4. `BidPlaced` event contract — `dotnet-service-builder`
  - [ ] 2.5. `AuctionFinished` event contract — `dotnet-service-builder`
- [ ] 3. Create the Auction Service Clean Architecture projects (Domain, Application, Infrastructure, API) with correct NuGet packages per layer — `dotnet-service-builder`
- [ ] 4. Define entities in `AuctionService.Domain/Entities/` — `dotnet-service-builder`
  - [ ] 4.1. `Auction` entity — `dotnet-service-builder`
  - [ ] 4.2. `Item` entity — `dotnet-service-builder`
  - [ ] 4.3. `Status` enum in `AuctionService.Domain/Enums/` — `dotnet-service-builder`
- [ ] 5. Define DTOs in `AuctionService.Application/DTOs/` — `dotnet-service-builder`
  - [ ] 5.1. `AuctionDto` — `dotnet-service-builder`
  - [ ] 5.2. `CreateAuctionDto` — `dotnet-service-builder`
  - [ ] 5.3. `UpdateAuctionDto` — `dotnet-service-builder`
- [ ] 6. Set up PostgreSQL with Entity Framework in `AuctionService.Infrastructure/Data/` (DbContext, migrations) — `dotnet-service-builder`
- [ ] 7. Configure Mapster mapping configs (`IRegister`) in `AuctionService.Application/Mappings/` (Entity ↔ DTO) — `dotnet-service-builder`
- [ ] 8. Implement API endpoints in `AuctionService.API/Controllers/` — `dotnet-service-builder`
  - [ ] 8.1. `GET api/auctions` — list all auctions — `dotnet-service-builder`
  - [ ] 8.2. `GET api/auctions/{id}` — get auction by id — `dotnet-service-builder`
  - [ ] 8.3. `GET api/auctions?date=` — get auctions updated from a given date — `dotnet-service-builder`
  - [ ] 8.4. `POST api/auctions` — create auction (Auth) — `dotnet-service-builder`
  - [ ] 8.5. `PUT api/auctions/{id}` — update auction (Auth) — `dotnet-service-builder`
  - [ ] 8.6. `DELETE api/auctions/{id}` — delete auction (Auth) — `dotnet-service-builder`
- [ ] 9. Add seed data (10 auction items) — `dotnet-service-builder`
- [ ] 10. Set up RabbitMQ with MassTransit (including Outbox pattern) in `AuctionService.API/Program.cs` — `dotnet-service-builder`
- [ ] 11. Publish events: `AuctionCreated`, `AuctionUpdated`, `AuctionDeleted` — `dotnet-service-builder`
- [ ] 12. Add event consumers in `AuctionService.Application/Consumers/` — `dotnet-service-builder`
  - [ ] 12.1. `BidPlaced` consumer — `dotnet-service-builder`
  - [ ] 12.2. `AuctionFinished` consumer — `dotnet-service-builder`
- [ ] 13. Dockerize the Auction Service (multi-project restore pattern for Clean Architecture) — `dotnet-service-builder`, verify with `docker-validator`
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
  - [ ] 4.5. `AuctionFinished` — update status, winner, sold amount — `dotnet-service-builder`
- [ ] 5. Implement `GET api/search` endpoint in `SearchService.API/Controllers/` (searchTerm, pageSize, pageNumber, seller, winner, orderBy, filterBy) — `dotnet-service-builder`
- [ ] 6. Add HTTP polling fallback in `SearchService.Infrastructure/` to Auction Service (`GetAuctionsFromDate`) with Polly retry — `dotnet-service-builder`
- [ ] 7. Configure MassTransit Outbox pattern — `dotnet-service-builder`
- [ ] 8. Dockerize the Search Service (multi-project restore pattern for Clean Architecture) — `dotnet-service-builder`, verify with `docker-validator`
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

---

## Phase 3: Identity Service

**Goal:** Build a centralized authentication and authorization service.

**Purpose:** Issues JWT tokens via Duende IdentityServer, provides login/register UI, and secures backend services with bearer token authentication.

**Acceptance Criteria:**
- IdentityServer issues valid JWT tokens
- Login and register pages are functional
- Auction Service rejects unauthenticated requests to protected endpoints
- Default users are seeded
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
- [ ] 4. Add Razor Pages for login/register UI — `dotnet-service-builder`
- [ ] 5. Seed default users (bob, alice, tom) — `dotnet-service-builder`
- [ ] 6. Configure Polly retry for database connections during startup — `dotnet-service-builder`
- [ ] 7. Add JWT bearer authentication to Auction Service — `dotnet-service-builder`
- [ ] 8. Add JWT bearer authentication to Bidding Service (prep for Phase 5) — `dotnet-service-builder`
- [ ] 9. Dockerize the Identity Service — `dotnet-service-builder`, verify with `docker-validator`
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

---

## Phase 4: Gateway Service

**Goal:** Build a single entry point that routes all client requests to backend services.

**Purpose:** Provides a unified API surface via YARP reverse proxy, handles JWT validation at the edge, and decouples clients from individual service URLs.

**Acceptance Criteria:**
- All API routes are proxied correctly to their respective services
- JWT authentication is validated at the gateway
- Both authenticated and anonymous endpoints work through the gateway
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
- [ ] 4. Dockerize the Gateway Service — `dotnet-service-builder`, verify with `docker-validator`
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

---

## Phase 5: Bidding Service

**Goal:** Build a service that handles bid placement, validation, and auction completion.

**Purpose:** Manages the bidding lifecycle — validates bids against business rules, uses gRPC to fetch auction data when needed, and runs a background job to finalize expired auctions.

**Acceptance Criteria:**
- Bids are validated (amount logic, seller check, auction status)
- gRPC fallback to Auction Service works for missing auction data
- Background service detects and finalizes expired auctions
- `BidPlaced` and `AuctionFinished` events are published
- OpenAPI document and Scalar docs UI are served with the OAuth2/Bearer security scheme
- Unit tests and integration tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Bidding Service Clean Architecture projects (Domain, Application, Infrastructure, API) with correct NuGet packages per layer — `dotnet-service-builder`
- [ ] 2. Define models in `BiddingService.Domain/` — `dotnet-service-builder`
  - [ ] 2.1. `Bid` entity in `BiddingService.Domain/Entities/` — `dotnet-service-builder`
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
- [ ] 10. Implement bid status logic — `dotnet-service-builder`
  - [ ] 10.1. Accepted — bid > current high bid and > reserve price — `dotnet-service-builder`
  - [ ] 10.2. AcceptedBelowReserve — bid > current high bid but < reserve price — `dotnet-service-builder`
  - [ ] 10.3. TooLow — bid <= current high bid — `dotnet-service-builder`
  - [ ] 10.4. Finished — auction already ended — `dotnet-service-builder`
- [ ] 11. Publish events: `BidPlaced`, `AuctionFinished` — `dotnet-service-builder`
- [ ] 12. Implement background service (check auctions past `AuctionEnd`, emit `AuctionFinished`) — `dotnet-service-builder`
- [ ] 13. Add JWT bearer authentication — `dotnet-service-builder`
- [ ] 14. Dockerize the Bidding Service (multi-project restore pattern for Clean Architecture) — `dotnet-service-builder`, verify with `docker-validator`
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

---

## Phase 6: Notification Service

**Goal:** Build a real-time notification service using SignalR.

**Purpose:** Pushes live updates to connected browser clients whenever auctions are created, bids are placed, or auctions finish — enabling a reactive UI without polling.

**Acceptance Criteria:**
- SignalR hub accepts client connections at `/notifications`
- Clients receive real-time notifications for `AuctionCreated`, `BidPlaced`, and `AuctionFinished`
- Integration tests pass
- Service runs in Docker

### Tasks

- [ ] 1. Create the Notification Service project with NuGet packages — `dotnet-service-builder`
- [ ] 2. Configure MassTransit with RabbitMQ (consumer-only — no outbox; this service has no database and publishes no events) — `dotnet-service-builder`
- [ ] 3. Create SignalR hub at `/notifications` — `dotnet-service-builder`
- [ ] 4. Implement event consumers that push to SignalR clients — `dotnet-service-builder`
  - [ ] 4.1. `AuctionCreated` — notify clients of new auction — `dotnet-service-builder`
  - [ ] 4.2. `BidPlaced` — notify clients of new bid — `dotnet-service-builder`
  - [ ] 4.3. `AuctionFinished` — notify clients of auction result — `dotnet-service-builder`
- [ ] 5. Dockerize the Notification Service — `dotnet-service-builder`, verify with `docker-validator`
- [ ] 6. Write integration tests (NotificationService.IntegrationTests) — `dotnet-service-builder`
  - [ ] 6.1. AuctionCreated consumer — pushes notification to SignalR clients — `dotnet-service-builder`
  - [ ] 6.2. BidPlaced consumer — pushes notification to SignalR clients — `dotnet-service-builder`
  - [ ] 6.3. AuctionFinished consumer — pushes notification to SignalR clients — `dotnet-service-builder`
  - [ ] 6.4. SignalR hub — client connects and receives messages — `dotnet-service-builder`
- [ ] 7. Verify end-to-end: place bid → notification pushed to connected SignalR client — `test-runner`

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
- [ ] 2. Configure Tailwind CSS and Flowbite React — `frontend-builder`
- [ ] 3. Set up next-auth with Duende IdentityServer (OAuth2/OIDC) — `frontend-builder`
- [ ] 4. Implement auction listing page — `frontend-builder`
  - [ ] 4.1. Fetch from Search Service (via Gateway) — `frontend-builder`
  - [ ] 4.2. Pagination, filtering (live, endingSoon, finished), sorting (make, new, endingSoon) — `frontend-builder`
  - [ ] 4.3. Search by term, seller, winner — `frontend-builder`
- [ ] 5. Implement auction detail page — `frontend-builder`
  - [ ] 5.1. Display DetailedSpecs (seller, make, model, year, mileage, reserve price) — `frontend-builder`
  - [ ] 5.2. Auction countdown timer (`react-countdown`) — `frontend-builder`
  - [ ] 5.3. Bid history list — `frontend-builder`
- [ ] 6. Implement auction create/edit form — `frontend-builder`
  - [ ] 6.1. `react-hook-form` for form handling — `frontend-builder`
  - [ ] 6.2. `react-datepicker` for auction end date — `frontend-builder`
  - [ ] 6.3. Image URL input — `frontend-builder`
- [ ] 7. Implement auction delete (with confirmation) — `frontend-builder`
- [ ] 8. Implement bid placement UI — `frontend-builder`
  - [ ] 8.1. Bid input and submit — `frontend-builder`
  - [ ] 8.2. Real-time bid updates via SignalR (`@microsoft/signalr`) — `frontend-builder`
- [ ] 9. Set up Zustand store for client-side state management — `frontend-builder`
- [ ] 10. Add toast notifications (`react-hot-toast`) — `frontend-builder`
- [ ] 11. Add currency formatting helper (`numberWithCommas`) — `frontend-builder`
- [ ] 12. Configure Next.js image optimization (`sharp`) — `frontend-builder`
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
- [ ] 16. Verify end-to-end: full user flow (browse → login → create auction → bid → real-time updates) — `playwright-tester`

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
  - [ ] 1.1. Infrastructure services: PostgreSQL, MongoDB, RabbitMQ — *main conversation*
  - [ ] 1.2. Backend services: Auction, Search, Bidding, Identity, Gateway, Notification — *main conversation*
  - [ ] 1.3. Frontend: Next.js web app — *main conversation*
- [ ] 2. Configure environment variables and connection strings for all services — *main conversation*, validate with `docker-validator`
- [ ] 3. Configure inter-service networking — *main conversation*, validate with `docker-validator`
- [ ] 4. Set up Nginx reverse proxy with SSL (via acme-companion) — *main conversation*, validate with `docker-validator`
- [ ] 5. Verify full stack runs with `docker compose up` — `docker-validator`
- [ ] 6. Import and verify Postman collection against all API endpoints — *main conversation*
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
  - [ ] 1.1. `dev-secrets.yaml` — secrets for databases, RabbitMQ, and app config — *main conversation*
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
- [ ] 2. Deploy to local Kubernetes (Minikube or Docker Desktop K8s) — *main conversation*
- [ ] 3. Verify all services are running and communicating — *main conversation*
- [ ] 4. Test full application via ingress endpoint — `playwright-tester`

---

## Phase 10: CI/CD & Cloud Kubernetes Deployment

**Goal:** Automate builds and deploy the application to a cloud Kubernetes cluster.

**Purpose:** Establishes CI/CD pipelines so that pushing to `main` automatically builds, pushes Docker images, and deploys to a production cluster accessible on the internet.

**Acceptance Criteria:**
- GitHub Actions workflows trigger on push to `main` for each service
- Docker images are built and pushed to Docker Hub automatically
- Application is deployed and accessible on the internet
- DNS and ingress are configured for public access

### Tasks

- [ ] 1. Create GitHub Actions workflows — *main conversation*
  - [ ] 1.1. Trigger on push to `main` for relevant service paths — *main conversation*
  - [ ] 1.2. Build Docker image — *main conversation*
  - [ ] 1.3. Push to Docker Hub — *main conversation*
- [ ] 2. Configure Docker Hub secrets in GitHub repository — *main conversation*
- [ ] 3. Set up production Kubernetes cluster (cloud provider) — *main conversation*
- [ ] 4. Configure production secrets and environment variables — *main conversation*
- [ ] 5. Deploy all services to cloud Kubernetes — *main conversation*
- [ ] 6. Configure DNS and ingress for public access — *main conversation*
- [ ] 7. Verify production deployment end-to-end — `playwright-tester`
