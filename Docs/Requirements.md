# ApexAutoBid - Requirements

## 1. Project Overview

**ApexAutoBid** is a car auction web application built with a **microservices architecture** using **.NET** for backend services and **Next.js** for the client application. The system allows users to create, browse, bid on, and win car auctions in real time.

### Learning Objectives

- Build an application with .NET using Microservices architecture
- Build a client app using Next.js (App Router)
- Use RabbitMQ for service bus communications
- Install and use IdentityServer in the microservices app
- Test projects using unit and integration testing with xUnit
- Publish the microservices app to Docker Compose
- Publish the microservices app to Kubernetes cluster (locally and on the internet)

---

## 2. Architecture

The application runs inside a **Docker Host** and consists of the following components:

**Client platforms:**
- **WebApp** - Next.js (primary, implemented in this project)
- **MobileApp** - iOS/Android (future, shown in architecture diagram but not in scope)

| Component | Technology | Database |
|-----------|-----------|----------|
| Client App (BFF) | Next.js | - |
| Gateway | .NET + YARP Reverse Proxy | - |
| Identity Service | Duende IdentityServer + ASP.NET Core Identity | PostgreSQL |
| Auction Service | .NET Web API + Entity Framework | PostgreSQL |
| Search Service | .NET Web API + MongoDB.Entities | MongoDB |
| Bidding Service | .NET Web API + MongoDB.Entities | MongoDB |
| Notification Service | .NET Web + SignalR | - |
| Event Bus | RabbitMQ (via MassTransit) | - |
| Ingress | Nginx Proxy | - |

---

## 3. Service Specifications

### 3.1 Auction Service

**Infrastructure:** .NET Web API, PostgreSQL, Entity Framework, RabbitMQ (MassTransit)

**NuGet Packages:**
- Mapster + Mapster.DependencyInjection (chosen over AutoMapper, which is commercially licensed since v15)
- Microsoft.AspNetCore.Authentication.JwtBearer
- Microsoft.EntityFrameworkCore.Design
- Npgsql.EntityFrameworkCore.PostgreSQL
- MassTransit.RabbitMQ
- MassTransit.EntityFrameworkCore (transactional outbox)
- Grpc.AspNetCore (gRPC server called by the Bidding Service)
- Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore (API documentation)

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| POST | api/auctions | Create auction | Auth |
| PUT | api/auctions/:id | Update auction | Auth |
| DELETE | api/auctions/:id | Delete auction | Auth |
| GET | api/auctions | Get all auctions | Anon |
| GET | api/auctions?date= | Get auctions updated from a given date | Anon |
| GET | api/auctions/:id | Get auction by id | Anon |

**Commands:**
- **CreateAuction** - Creates an Item. Emits `AuctionCreated`. Requires the `email_verified` claim — returns 403 otherwise (see §3.4 Email Verification)
- **UpdateAuction** - Updates an Auction. Emits `AuctionUpdated`
- **DeleteAuction** - Deletes an Auction (only if no bids or reserve not met). Emits `AuctionDeleted`

**Queries:**
- **GetAuctionById** - Returns `AuctionDto`
- **GetAuctions** - Returns list of `AuctionDto`
- **GetAuctionsFromDate** - Returns auctions updated from a given date

**Events Emitted:** `AuctionCreated`, `AuctionUpdated`, `AuctionDeleted`
**Events Consumed:** `BidService.BidPlaced`, `BidService.AuctionFinished`

**Consumed Event Payloads:**

*BidService.BidPlaced (consumed by Auction Service):*

| Property | Type |
|----------|------|
| Id | string |
| AuctionId | string |
| Bidder | string |
| BidTime | DateTime |
| Amount | int |
| BidStatus | string |

*BidService.AuctionFinished (consumed by Auction Service):*

| Property | Type |
|----------|------|
| ItemSold | bool |
| AuctionId | string |
| Winner? | string |
| WinnerEmail? | string |
| Seller | string |
| Amount? | int |

**Models:**

**Auction.cs**

| Property | Type | Default |
|----------|------|---------|
| Id | Guid | |
| ReservePrice | int | 0 |
| Seller | string | (from claim) |
| SellerEmail | string | (from `email` claim) |
| Winner? | string | |
| WinnerEmail? | string | (from `AuctionFinished`) |
| SoldAmount? | int | |
| CurrentHighBid? | int | |
| CreatedAt | DateTime | UtcNow |
| UpdatedAt | DateTime | UtcNow |
| AuctionEnd | DateTime | |
| Status | Status | Live |
| Item | Item | |

**Item.cs**

| Property | Type |
|----------|------|
| Id | Guid |
| Make | string |
| Model | string |
| Year | int |
| Color | string |
| Mileage | int |
| ImageUrl | string |
| Auction | Auction (FK) |
| AuctionId | Guid |

**Status Enum:** Live, Finished, ReserveNotMet

**DTOs:** AuctionDto, CreateAuctionDto (Make, Model, Color, Mileage, Year, ReservePrice, ImageUrl, AuctionEnd), UpdateAuctionDto (Make?, Model?, Color?, Mileage?, Year?)

**Post-Sale Contact Exchange:**

So the winner and seller can contact each other (e.g., by email) after a sale, `GET api/auctions/:id` conditionally includes contact fields once the auction is sold (`Status = Finished` with a winner):
- `WinnerEmail` is included **only** when the caller's username claim equals `Seller`
- `SellerEmail` is included **only** when the caller's username claim equals `Winner`
- All other callers (including anonymous) receive neither field

Email flow: `SellerEmail` is captured from the JWT `email` claim at auction creation; `WinnerEmail` arrives on the `AuctionFinished` event (the Bidding Service stores each bidder's email with the bid). Emails are never included in `AuctionCreated`/`AuctionUpdated` events, search results, or SignalR messages.

**Emitted Event Payloads:**

*AuctionCreated:*

| Property | Type |
|----------|------|
| Id | Guid |
| CreatedAt | DateTime |
| UpdatedAt | DateTime |
| AuctionEnd | DateTime |
| Seller | string |
| Winner | string |
| Make | string |
| Model | string |
| Year | int |
| Color | string |
| Mileage | int |
| ImageUrl | string |
| Status | string |
| ReservePrice | int |
| SoldAmount? | int |
| CurrentHighBid? | int |

*AuctionUpdated:*

| Property | Type |
|----------|------|
| Id | string |
| Make | string |
| Model | string |
| Color | string |
| Mileage | int |
| Year | int |

*AuctionDeleted:*

| Property | Type |
|----------|------|
| Id | string |

---

### 3.2 Search Service

**Infrastructure:** .NET Web API, MongoDB, RabbitMQ (MassTransit)

**NuGet Packages:**
- Mapster + Mapster.DependencyInjection (chosen over AutoMapper, which is commercially licensed since v15)
- Microsoft.Extensions.Http.Resilience (Polly v8)
- MongoDB.Entities
- MassTransit.RabbitMQ
- MassTransit.MongoDb (transactional outbox)
- Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore (API documentation)

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | api/search?query | Paged list of auctions (searchTerm, pageSize, pageNumber, seller, winner, orderBy, filterBy) | Anon |

**Events Consumed:** `AuctionService.AuctionCreated`, `AuctionService.AuctionUpdated`, `AuctionService.AuctionDeleted`, `BidService.AuctionFinished`, `BidService.BidPlaced`

> Privacy: the `WinnerEmail` field on `AuctionFinished` is **ignored** by this service — emails are never stored in or served from the search index.

**Model (Item.cs):** Mirrors AuctionDto fields (Id, CreatedAt, UpdatedAt, AuctionEnd, Seller, Winner, Make, Model, Year, Color, Mileage, ImageUrl, Status, ReservePrice, SoldAmount?, CurrentHighBid?)

**Search Query Parameters:**

| Param | Description |
|-------|-------------|
| searchTerm | Free text search across make/model |
| pageSize | Number of results per page |
| pageNumber | Page number for pagination |
| seller | Filter by seller username |
| winner | Filter by winner username |
| orderBy | Sort results (e.g., make, new, endingSoon) |
| filterBy | Filter auctions (e.g., finished, endingSoon, live) |

**Consumed Event Payloads:**

*AuctionCreated:*

| Property | Type |
|----------|------|
| Id | Guid |
| CreatedAt | DateTime |
| UpdatedAt | DateTime |
| AuctionEnd | DateTime |
| Seller | string |
| Winner | string |
| Make | string |
| Model | string |
| Year | int |
| Color | string |
| Mileage | int |
| ImageUrl | string |
| Status | string |
| ReservePrice | int |
| SoldAmount? | int |
| CurrentHighBid? | int |

*AuctionUpdated:*

| Property | Type |
|----------|------|
| Id | string |
| Make | string |
| Model | string |
| Color | string |
| Mileage | int |
| Year | int |

*AuctionDeleted:*

| Property | Type |
|----------|------|
| Id | string |

*BidService.AuctionFinished:*

| Property | Type |
|----------|------|
| ItemSold | bool |
| AuctionId | string |
| Winner? | string |
| WinnerEmail? | string |
| Seller | string |
| Amount? | int |

*BidService.BidPlaced:*

| Property | Type |
|----------|------|
| Id | string |
| AuctionId | string |
| Bidder | string |
| BidTime | DateTime |
| Amount | int |
| BidStatus | string |

---

### 3.3 Bidding Service

**Infrastructure:** .NET Web API, MongoDB, RabbitMQ (MassTransit), gRPC

**NuGet Packages:**
- Mapster + Mapster.DependencyInjection (chosen over AutoMapper, which is commercially licensed since v15)
- Microsoft.Extensions.Http.Resilience (Polly v8)
- MongoDB.Entities
- MassTransit.RabbitMQ
- MassTransit.MongoDb (transactional outbox)
- Google.Protobuf
- Grpc.Net.Client
- Grpc.Tools
- Microsoft.AspNetCore.Authentication.JwtBearer
- Polly
- Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore (API documentation)

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| POST | api/bids | Place bid (auctionId, amount) | Auth |
| GET | api/bids/{auctionId} | Get bids for auction | Anon |

**Bid Validation Rules (POST api/bids):**
- The caller must have the `email_verified` claim — otherwise 403 (see §3.4 Email Verification)
- The auction must exist locally or be fetchable via the gRPC fallback — otherwise 404
- **The seller cannot bid on their own auction** (bidder claim == auction `Seller`) — returns 400 Bad Request
- Bids placed after `AuctionEnd` are saved with status `Finished` and do not count
- Otherwise the bid is saved with a status: higher than current high bid and ≥ reserve price → `Accepted`; higher than current high bid but below reserve → `AcceptedBelowReserve`; not higher than current high bid → `TooLow`

**Events Emitted:** `BidPlaced`, `AuctionFinished`
**Events Consumed:** `AuctionService.AuctionCreated`

**gRPC:** The Bidding Service uses gRPC to directly call the Auction Service as a fallback mechanism when event data is not yet available (e.g., fetching auction details if the `AuctionCreated` event hasn't been consumed yet). Uses Polly for retry/resilience.

**Resilience:** Uses **Polly v8** via `Microsoft.Extensions.Http.Resilience` for retry policies on HTTP/gRPC calls to handle transient failures.

**Models:**

**Bid.cs:** ID, AuctionId, Bidder (from claim), BidderEmail (from `email` claim), BidTime (UtcNow), Amount, BidStatus

> `BidderEmail` is stored so the background service can set `WinnerEmail` on `AuctionFinished` from the winning bid (post-sale contact exchange — see §3.1). It is never returned by `GET api/bids/{auctionId}`.

**BidStatus Enum:** Accepted, AcceptedBelowReserve, TooLow, Finished

**Auction.cs (local):** ID, AuctionEnd, Seller, ReservePrice, Finished

**Emitted Event Payloads:**

*BidPlaced:*

| Property | Type |
|----------|------|
| Id | string |
| AuctionId | string |
| Bidder | string |
| BidTime | DateTime |
| Amount | int |
| BidStatus | string |

*AuctionFinished:*

| Property | Type |
|----------|------|
| ItemSold | bool |
| AuctionId | string |
| Winner? | string |
| WinnerEmail? | string |
| Seller | string |
| Amount? | int |

**Consumed Event Payload:**

*AuctionService.AuctionCreated:* (same as AuctionCreated payload in Section 3.1)

---

### 3.4 Identity Service

**Infrastructure:** Duende IdentityServer + ASP.NET Core Identity, PostgreSQL

**NuGet Packages:**
- Npgsql.EntityFrameworkCore.PostgreSQL
- Polly
- Microsoft.AspNetCore.Authentication.Google (external login)

**Resilience:** Uses **Polly** for retry policies on database connections during startup.

**Token Claims:** access tokens include `username` (used as `Seller`/`Winner`/`Bidder` identity across services), `email` (captured by the Auction and Bidding Services for post-sale contact exchange — see §3.1), and `email_verified` (true once the user confirms their email or signs in with Google).

**Email Verification:**
- ASP.NET Core Identity with `RequireConfirmedEmail`: registration sends a confirmation link; unconfirmed accounts can log in and browse, but creating auctions and placing bids require the `email_verified` claim (enforced by the Auction and Bidding Services → 403). This keeps the post-sale contact exchange trustworthy.
- Email sending: dev/Docker uses **Mailpit** (SMTP on :1025, web UI on :8025, no credentials); production uses a real SMTP provider with credentials supplied via environment variables
- Seed users (bob, alice, tom) are seeded with confirmed emails

**External Logins:**
- **Google** sign-in on the IdentityServer login/register pages via `Microsoft.AspNetCore.Authentication.Google`
- Google-asserted verified emails are treated as confirmed (no confirmation email sent)
- The Google client ID/secret are real external credentials — supplied via environment variables, never committed in **any** environment (exception to the committed-dev-secrets rule; Google login is simply disabled when the variables are absent)

**Bot Protection:**
- **Cloudflare Turnstile** on the registration page (the only anonymous endpoint with a costly side effect — it creates accounts and sends email). The register POST validates the Turnstile token server-side via the `siteverify` API (plain `HttpClient`, no NuGet package needed) and rejects the registration on failure.
- Dev/Docker use Cloudflare's **official always-pass test keys**, which are published for this purpose and safe to commit — so local setup and Playwright e2e tests work with zero configuration. Production site/secret keys are supplied via environment variables (never committed).
- **Account lockout** (ASP.NET Core Identity built-in) is enabled: repeated failed logins temporarily lock the account.
- The login, register, and token endpoints are **rate limited** via `Microsoft.AspNetCore.RateLimiting` (built into .NET — no package) to blunt credential stuffing and registration floods; over-limit requests receive 429.
- Login and bidding/auction endpoints do **not** use a captcha — they are protected by lockout + rate limiting (gateway) + the verified-email requirement instead.

**IdentityServer Clients:**
- Next.js web app (authorization code flow via next-auth)
- `scalar` — API docs pages (authorization code + PKCE, public client without secret); requires CORS on the token endpoint for browser-based code exchange

---

### 3.5 Gateway Service

**Infrastructure:** .NET Web, YARP Reverse Proxy

**NuGet Packages:**
- Microsoft.AspNetCore.Authentication.JwtBearer
- Yarp.ReverseProxy
- Scalar.AspNetCore (aggregated API docs UI)

**Rate Limiting:** `Microsoft.AspNetCore.RateLimiting` middleware (built into .NET) in front of the YARP proxy:
- A general fixed-window policy on all proxied routes (per client IP)
- A stricter policy on mutating endpoints (`POST api/bids`, `POST/PUT/DELETE api/auctions`)
- Over-limit requests receive **429 Too Many Requests**; limits are configuration values, not hardcoded

---

### 3.6 Notification Service

**Infrastructure:** .NET Web, RabbitMQ (MassTransit), SignalR

**NuGet Packages:**
- MassTransit.RabbitMQ
- Microsoft.AspNetCore.Authentication.JwtBearer (for targeted notifications)

**SignalR Hub:** `/notifications` — anonymous connections allowed (receive broadcasts only). Authenticated clients pass their JWT via the `access_token` query parameter; a username-based `IUserIdProvider` maps the connection to the `username` claim so messages can be targeted with `Clients.User(...)`.

**Events Consumed:** `AuctionService.AuctionCreated`, `BidService.AuctionFinished`, `BidService.BidPlaced`

**Notification Targeting:**

| Event | Broadcast (all clients) | Targeted (authenticated) |
|-------|------------------------|--------------------------|
| AuctionCreated | `AuctionCreated` | — |
| BidPlaced | `BidPlaced` | — |
| AuctionFinished | `AuctionFinished` | `AuctionWon` → winner (when ItemSold); `AuctionSellerResult` → seller (sold or reserve not met, with amount) |

Usernames in `Winner`/`Seller` are the SignalR user identifiers — no extra lookup needed. Targeted messages carry usernames and amounts only — never email addresses (`WinnerEmail` on the consumed event is not forwarded); contact details are exchanged via the Auction Service API (see §3.1).

**Consumed Event Payloads:**

*AuctionCreated:* (same as AuctionCreated payload in Section 3.1)

*BidService.AuctionFinished:*

| Property | Type |
|----------|------|
| ItemSold | bool |
| AuctionId | string |
| Winner? | string |
| WinnerEmail? | string |
| Seller | string |
| Amount? | int |

*BidService.BidPlaced:*

| Property | Type |
|----------|------|
| Id | string |
| AuctionId | string |
| Bidder | string |
| BidTime | DateTime |
| Amount | int |
| BidStatus | string |

---

## 4. Client Application (Next.js)

**Framework:** Next.js (App Router)

**Key Dependencies:**
- `next-auth` - Authentication
- `@microsoft/signalr` - Real-time notifications
- `flowbite-react` - UI components
- `tailwindcss` - Styling
- `zustand` - State management
- `react-hook-form` - Form handling
- `react-hot-toast` - Toast notifications
- `react-countdown` - Auction countdown timers
- `react-datepicker` - Date picking for auction creation
- `query-string` - URL query parameter handling
- `date-fns` - Date formatting
- `sharp` - Image optimization

**Key Features:**
- Browse and search auctions with filtering/sorting/paging
- View auction details (make, model, year, mileage, seller, reserve price)
- Create/update/delete auctions (authenticated)
- Place bids on auctions (authenticated)
- Real-time bid notifications via SignalR
- Targeted real-time notifications: "You won" toast for the auction winner and a result toast for the seller (requires connecting to the hub with the access token when logged in)
- Post-sale contact exchange: on a sold auction's detail page, the seller sees the winner's email and the winner sees the seller's email (no one else sees either)
- Countdown timers for auction end times
- Currency formatting with comma separators

---

## 5. Event Bus Communication

All inter-service communication uses **RabbitMQ** via **MassTransit** (publish/subscribe pattern).

Additionally, the Bidding Service uses **gRPC** for direct service-to-service communication with the Auction Service (as a fallback when event data is unavailable).

### Event Contracts

| Event | Publisher | Consumers |
|-------|-----------|-----------|
| AuctionCreated | Auction Service | Search, Bidding, Notification |
| AuctionUpdated | Auction Service | Search |
| AuctionDeleted | Auction Service | Search |
| BidPlaced | Bidding Service | Auction, Search, Notification |
| AuctionFinished | Bidding Service | Auction, Search, Notification |

---

## 6. Deployment & Infrastructure

### Secrets & Configuration Management

Per-environment strategy. Dev credentials are throwaway, local-only values and are **committed to the repo** for simplicity; production and CI credentials are **never committed**.

| Environment | Mechanism | Committed? |
|-------------|-----------|------------|
| Local development (dotnet run) | `appsettings.Development.json` per service (connection strings, RabbitMQ, IdentityServer URLs) | Yes (dev-only values) |
| Docker Compose | `environment:` blocks inline in `docker/docker-compose.yml` | Yes (dev-only values) |
| Local Kubernetes | `k8s/dev-secrets.yaml` (Opaque Secrets; base64-encoded, not encrypted) | Yes (dev-only values) |
| CI/CD | GitHub repository secrets (Docker Hub credentials) | No |
| Production Kubernetes | Kubernetes Secrets applied directly to the cloud cluster (`kubectl apply` from a local, untracked manifest) | No |

Rule: any value that grants access to a non-local resource (cloud DB, Docker Hub, production cluster, real signing keys) must never appear in the repo. `appsettings.json` (non-Development) holds only non-sensitive defaults.

External provider credentials are real credentials in **every** environment and follow the never-committed rule even in dev: the Google OAuth client ID/secret, production SMTP credentials, and production Cloudflare Turnstile keys are supplied via environment variables only. Dev email needs no credentials at all (Mailpit), and dev Turnstile uses Cloudflare's official always-pass test keys (published for this purpose — safe to commit).

### Docker Compose (Local Development)
- All services containerized
- PostgreSQL and MongoDB as database containers
- RabbitMQ as message broker
- Mailpit as dev email catcher (SMTP :1025, web UI :8025)
- Nginx reverse proxy with SSL (via acme-companion)

### Kubernetes (Production)
- Secrets management for database credentials, RabbitMQ, and app secrets (see Secrets & Configuration Management above — production values applied directly to the cluster, never committed)
- ClusterIP services for internal communication
- Ingress controllers for external access

### CI/CD
- GitHub Actions workflows for building and pushing Docker images
- Triggered on push to `main` branch for relevant service paths

### Next.js Dockerfile
- Multi-stage build: deps -> builder -> runner
- Runs as non-root `nextjs` user
- Standalone output mode
- Exposed on port 3000

---

## 7. Testing Requirements

### Unit Tests (xUnit)
- CreateAuction - failed save returns 400
- UpdateAuction - valid DTO returns OK
- UpdateAuction - invalid user returns 403 Forbid
- UpdateAuction - invalid GUID returns 404
- DeleteAuction - valid user returns OK
- DeleteAuction - invalid GUID returns 404
- DeleteAuction - invalid user returns 403
- PlaceBid - seller bidding on own auction returns 400
- PlaceBid - unknown auction returns 404
- CreateAuction - unverified email returns 403
- PlaceBid - unverified email returns 403
- Register - sends a confirmation email (Identity Service)
- ConfirmEmail - valid token marks the email confirmed (Identity Service)
- GetAuctionById - after sale, seller receives WinnerEmail
- GetAuctionById - after sale, winner receives SellerEmail
- GetAuctionById - other users and anonymous callers receive no contact fields

### Integration Tests (xUnit)
- CreateAuction - invalid DTO returns 400
- UpdateAuction - valid DTO and user returns 200
- UpdateAuction - valid DTO and invalid user returns 403

---

## 8. Seed Data

10 pre-configured auction items for development:
1. Ford GT (White, 2020, 50K miles, $20K reserve) - bob
2. Bugatti Veyron (Black, 2018, 15K miles, $90K reserve) - alice
3. Ford Mustang (Black, 2023, 65K miles, no reserve) - bob
4. Mercedes SLK (Silver, 2020, 15K miles, $50K reserve, ReserveNotMet) - tom
5. BMW X1 (White, 2017, 90K miles, $20K reserve) - alice
6. Ferrari Spider (Red, 2015, 50K miles, $20K reserve) - bob
7. Ferrari F-430 (Red, 2022, 5K miles, $150K reserve) - alice
8. Audi R8 (White, 2021, 10K miles, no reserve) - bob
9. Audi TT (Black, 2020, 25K miles, $20K reserve) - tom
10. Ford Model T (Rust, 1938, 150K miles, $20K reserve) - bob

---

## 9. API Testing

A **Postman collection** is available at `Docs/Initial_Planning/CourseAssets/postman/Carsties.postman_collection.json` for testing all API endpoints.

### API Documentation

Each API service (Auction, Search, Bidding) generates an OpenAPI document via the built-in `Microsoft.AspNetCore.OpenApi` package (`/openapi/v1.json`) and serves an interactive **Scalar** reference UI (`/scalar`). The **Gateway** hosts an aggregated Scalar page covering all service documents. Authenticated endpoints are documented with an OAuth2/Bearer security scheme; clicking Authorize runs the real IdentityServer login (authorization code + PKCE via the `scalar` client) and the obtained JWT is automatically attached to all "try it" requests. The Notification Service is excluded (SignalR hub only, no REST API).

---

## 10. Development Environment

### Recommended VS Code Extensions

```
bradlc.vscode-tailwindcss
ckolkman.vscode-postgres
dsznajder.es7-react-js-snippets
formulahendry.auto-close-tag
github.vscode-github-actions
mongodb.mongodb-vscode
ms-azuretools.vscode-docker
ms-dotnettools.csdevkit
ms-dotnettools.csharp
ms-dotnettools.vscode-dotnet-runtime
ms-dotnettools.vscodeintellicode-csharp
ms-kubernetes-tools.vscode-kubernetes-tools
patcx.vscode-nuget-gallery
PKief.material-icon-theme
redhat.vscode-yaml
zxh404.vscode-proto3
```

---

## 11. Coding Conventions

- Private fields use underscore prefix with camelCase (e.g., `_myField`)
- EditorConfig enforced naming rules for C# private members
