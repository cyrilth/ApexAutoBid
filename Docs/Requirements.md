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
- AWSSDK.S3 (presigned image-upload URLs against MinIO/any S3-compatible storage)
- SixLabors.ImageSharp (thumbnail generation; free for this project under the Six Labors Split License — OSS/small-revenue tier)
- Microsoft.AspNetCore.OpenApi + Scalar.AspNetCore (API documentation)

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| POST | api/auctions | Create auction | Auth |
| POST | api/auctions/upload-url | Get a presigned image-upload URL | Auth (verified email) |
| POST | api/auctions/thumbnail | Generate a thumbnail for an uploaded image | Auth (verified email) |
| PUT | api/auctions/{id} | Update auction | Auth |
| DELETE | api/auctions/{id} | Delete auction | Auth |
| GET | api/auctions | Get all auctions | Anon |
| GET | api/auctions?date= | Get auctions updated from a given date | Anon |
| GET | api/auctions/{id} | Get auction by id | Anon |
| POST | api/admin/auctions/{id}/end | End an auction now | Admin |
| POST | api/admin/auctions/{id}/cancel | Cancel/invalidate an auction | Admin |
| GET/POST | api/admin/banners | List/create banner messages | Admin |
| PUT/DELETE | api/admin/banners/{id} | Update/delete a banner message | Admin |
| GET | api/banners | Active banners (home/auction scope) | Anon |
| GET | api/admin/auctions/stats | Auction counts by status | Admin |
| GET | api/auctions/duration-limits | Current min/max auction duration | Anon |
| GET/PUT | api/admin/settings/duration | Read/set min/max auction duration (stored in DB) | Admin |

**Commands:**
- **CreateAuction** - Creates an Item. Emits `AuctionCreated`. Requires the `email_verified` claim — returns 403 otherwise (see §3.4 Email Verification). `Seller` comes from the username claim; an explicit `Seller` value in the request is honored **only** for callers in the `admin` role (admins can create auctions for any user, including themselves)
- **Auction duration:** `AuctionEnd` is chosen by the seller; on create/update it must lie between the platform's min/max duration bounds. Resolution order: **admin-set values in the database** (PlatformSettings, editable from the admin dashboard — §10.2) → **environment variables** (`Auction__MinDuration` / `Auction__MaxDuration`, TimeSpan format) → **defaults (1 hour – 90 days)**. Dev/Docker set `Auction__MinDuration=00:01:00` so a 1-minute auction can be created for local testing of the full lifecycle. Admins are exempt from the bounds and may shorten or extend `AuctionEnd` on a live auction (propagated via `AuctionUpdated.AuctionEnd`). The current bounds are publicly readable via `GET api/auctions/duration-limits` so the create form can constrain its datepicker
- **UpdateAuction** - Updates an Auction. Emits `AuctionUpdated`
- **DeleteAuction** - Deletes an Auction (only if no bids or reserve not met). Emits `AuctionDeleted`

**Queries:**
- **GetAuctionById** - Returns `AuctionDto`
- **GetAuctions** - Returns list of `AuctionDto`
- **GetAuctionsFromDate** - Returns auctions updated from a given date

**Events Emitted:** `AuctionCreated`, `AuctionUpdated`, `AuctionDeleted`, `AuctionCancelled`, `BannerPublished`
**Events Consumed:** `BidService.BidPlaced`, `BidService.AuctionFinished`, `BidService.BidRemoved`

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
| ThumbnailUrl? | string |
| Auction | Auction (FK) |
| AuctionId | Guid |

**Status Enum:** Live, Finished, ReserveNotMet, Cancelled

**DTOs:** AuctionDto, CreateAuctionDto (Make, Model, Color, Mileage, Year, ReservePrice, ImageUrl, ThumbnailUrl?, AuctionEnd), UpdateAuctionDto (Make?, Model?, Color?, Mileage?, Year?)

**Post-Sale Contact Exchange:**

So the winner and seller can contact each other (e.g., by email) after a sale, `GET api/auctions/{id}` conditionally includes contact fields once the auction is sold (`Status = Finished` with a winner):
- `WinnerEmail` is included **only** when the caller's username claim equals `Seller`
- `SellerEmail` is included **only** when the caller's username claim equals `Winner`
- All other callers (including anonymous) receive neither field

Email flow: `SellerEmail` is captured from the JWT `email` claim at auction creation; `WinnerEmail` arrives on the `AuctionFinished` event (the Bidding Service stores each bidder's email with the bid). Emails are never included in `AuctionCreated`/`AuctionUpdated` events, search results, or SignalR messages.

**Image Upload (presigned URLs):**

Users upload their auction/vehicle photos directly to object storage — image bytes never flow through the services:

1. The client calls `POST api/auctions/upload-url` (authenticated, verified email) with the desired content type
2. The Auction Service validates the content type against a whitelist (`image/jpeg`, `image/png`, `image/webp`) and returns a **presigned PUT URL** (AWSSDK.S3 against MinIO) plus the final public object URL
3. The browser PUTs the file straight to the `auction-images` bucket using the presigned URL
4. The client submits the create/edit form with the returned object URL as `ImageUrl`

Constraints: object keys are server-generated GUIDs (no overwrites, no user-controlled paths); presigned URLs expire after **5 minutes** and sign the content type; the Auction Service uses a dedicated MinIO access key whose policy allows `PutObject` on `auction-images/*` only (least privilege — reads, including the thumbnail generator fetching originals, go through the bucket's anonymous read access). Anonymous access to the bucket remains read-only. The plain URL input stays available as a fallback for externally hosted images.

**Thumbnail Generation:**

After uploading, the user can optionally generate a thumbnail: `POST api/auctions/thumbnail` takes the uploaded object key, and the Auction Service downloads the original from the bucket, resizes it with **SixLabors.ImageSharp** (max 400px wide, WebP output), uploads it as `thumbs/{key}.webp`, and returns the thumbnail URL. The auction stores it as `ThumbnailUrl?` (nullable — auctions without one fall back to `ImageUrl`). Listing/search results and social link previews use the thumbnail when present; the detail page shows the full image. Only object keys inside `auction-images` are accepted (no arbitrary URL fetching — prevents SSRF).

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
| ThumbnailUrl? | string |
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
| AuctionEnd? | DateTime |

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

**Events Consumed:** `AuctionService.AuctionCreated`, `AuctionService.AuctionUpdated`, `AuctionService.AuctionDeleted`, `AuctionService.AuctionCancelled` (sets status Cancelled), `BidService.AuctionFinished`, `BidService.BidPlaced`, `BidService.BidRemoved` (refreshes CurrentHighBid)

> Privacy: the `WinnerEmail` field on `AuctionFinished` is **ignored** by this service — emails are never stored in or served from the search index.

**Model (Item.cs):** Mirrors AuctionDto fields (Id, CreatedAt, UpdatedAt, AuctionEnd, Seller, Winner, Make, Model, Year, Color, Mileage, ImageUrl, ThumbnailUrl?, Status, ReservePrice, SoldAmount?, CurrentHighBid?)

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
| ThumbnailUrl? | string |
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
| AuctionEnd? | DateTime |

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
| DELETE | api/admin/bids/{id} | Remove a bid (recalculates high bid) | Admin |
| GET | api/admin/bids/stats | Total bid count | Admin |

**Bid Validation Rules (POST api/bids):**
- The caller must have the `email_verified` claim — otherwise 403 (see §3.4 Email Verification)
- The auction must exist locally or be fetchable via the gRPC fallback — otherwise 404
- **The seller cannot bid on their own auction** (bidder claim == auction `Seller`) — returns 400 Bad Request
- Bids placed after `AuctionEnd` are saved with status `Finished` and do not count
- Otherwise the bid is saved with a status: higher than current high bid and ≥ reserve price → `Accepted`; higher than current high bid but below reserve → `AcceptedBelowReserve`; not higher than current high bid → `TooLow`

**Events Emitted:** `BidPlaced`, `AuctionFinished`, `BidRemoved`
**Events Consumed:** `AuctionService.AuctionCreated`, `AuctionService.AuctionUpdated` (applies `AuctionEnd?` changes to local records), `AuctionService.AuctionCancelled` (marks the local auction finished — no further bids, no `AuctionFinished` emitted)

**gRPC:** The Bidding Service uses gRPC to directly call the Auction Service as a fallback mechanism when event data is not yet available (e.g., fetching auction details if the `AuctionCreated` event hasn't been consumed yet). Uses Polly for retry/resilience.

**Resilience:** Uses **Polly v8** via `Microsoft.Extensions.Http.Resilience` for retry policies on HTTP/gRPC calls to handle transient failures.

**Background finalization:** the expired-auction check interval is configurable via `Bidding__FinalizationIntervalSeconds` (default **10**). Dev/Docker keep it at 10s so short test auctions (e.g., the 1-minute minimum in dev — §3.1) finalize promptly after ending.

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

**Token Claims:** access tokens include `username` (used as `Seller`/`Winner`/`Bidder` identity across services), `email` (captured by the Auction and Bidding Services for post-sale contact exchange — see §3.1), `email_verified` (true once the user confirms their email or signs in with Google), and `role` (ASP.NET Identity roles; `admin` unlocks the admin endpoints and dashboard — see §10).

**Admin API:** the Identity Service exposes admin-only user-management endpoints (list/search, create, reset password, resend confirmation, assign roles, lock/unlock, user stats) — full list in §10. Documented via OpenAPI + Scalar like the other services.

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

**Version Endpoint:** `GET api/version` (Anon) — handled by the gateway itself, not proxied. Returns the platform version from the gateway's assembly metadata, e.g. `{ "version": "1.2.0" }`. All services share the platform version (see `Docs/Versioning.md`), so this is the backend version; the web app footer displays it alongside the frontend version.

---

### 3.6 Notification Service

**Infrastructure:** .NET Web, RabbitMQ (MassTransit), SignalR

**NuGet Packages:**
- MassTransit.RabbitMQ
- Microsoft.AspNetCore.Authentication.JwtBearer (for targeted notifications)

**SignalR Hub:** `/notifications` — anonymous connections allowed (receive broadcasts only). Authenticated clients pass their JWT via the `access_token` query parameter; a username-based `IUserIdProvider` maps the connection to the `username` claim so messages can be targeted with `Clients.User(...)`.

**Events Consumed:** `AuctionService.AuctionCreated`, `AuctionService.AuctionCancelled`, `AuctionService.BannerPublished`, `BidService.AuctionFinished`, `BidService.BidPlaced`

**Notification Targeting:**

| Event | Broadcast (all clients) | Targeted (authenticated) |
|-------|------------------------|--------------------------|
| AuctionCreated | `AuctionCreated` | — |
| BidPlaced | `BidPlaced` | — |
| AuctionFinished | `AuctionFinished` | `AuctionWon` → winner (when ItemSold); `AuctionSellerResult` → seller (sold or reserve not met, with amount) |
| AuctionCancelled | `AuctionCancelled` | `AuctionCancelled` → seller |
| BannerPublished | `BannerPublished` (clients show/refresh banners live) | — |

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
- Auction image upload on the create/edit form — direct-to-storage via presigned PUT URL, with a plain URL input as fallback; after uploading, an optional "Generate thumbnail" step calls `POST api/auctions/thumbnail`
- Social sharing on the auction detail page: share-intent buttons for **Facebook** (`facebook.com/sharer`), **X/Twitter** (`twitter.com/intent/tweet`), and **WhatsApp** (`wa.me`), plus a native **Share** button using the Web Share API (`navigator.share`) — on mobile this opens the OS share sheet, which is how **Instagram** is reached (Instagram has no web share URL; links can only be shared into it via the native sheet)
- Link previews: the auction detail page emits **Open Graph + Twitter Card metadata** via Next.js `generateMetadata` (server-rendered) — `og:title` (year make model), `og:description` (status, current high bid / sold amount, auction end), `og:image` (thumbnail, falling back to the full image), `twitter:card` = `summary_large_image` — so pasting an auction link into WhatsApp, iMessage, Slack, Facebook, X, etc. renders a rich preview. Requires absolute, publicly reachable image URLs in production
- Place bids on auctions (authenticated)
- Real-time bid notifications via SignalR
- Targeted real-time notifications: "You won" toast for the auction winner and a result toast for the seller (requires connecting to the hub with the access token when logged in)
- Post-sale contact exchange: on a sold auction's detail page, the seller sees the winner's email and the winner sees the seller's email (no one else sees either)
- Countdown timers for auction end times
- Currency formatting with comma separators
- Banner messages on the home page and auction pages (live-updated via SignalR when an admin publishes)
- Page footer shows the frontend version (from `package.json`) and the backend version (fetched from `GET api/version` — see `Docs/Versioning.md`)
- Admin area at `/admin` (role-gated): dashboard with platform statistics, user management, auction/bid moderation, banner management — see §10

---

## 5. Event Bus Communication

All inter-service communication uses **RabbitMQ** via **MassTransit** (publish/subscribe pattern).

Additionally, the Bidding Service uses **gRPC** for direct service-to-service communication with the Auction Service (as a fallback when event data is unavailable).

### Event Contracts

| Event | Publisher | Consumers |
|-------|-----------|-----------|
| AuctionCreated | Auction Service | Search, Bidding, Notification |
| AuctionUpdated | Auction Service | Search, Bidding |
| AuctionDeleted | Auction Service | Search |
| AuctionCancelled | Auction Service | Search, Bidding, Notification |
| BannerPublished | Auction Service | Notification |
| BidPlaced | Bidding Service | Auction, Search, Notification |
| BidRemoved | Bidding Service | Auction, Search |
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
| CI/CD | GitHub repository secrets (Docker Hub credentials, production kubeconfig) | No |
| Production Kubernetes | Kubernetes Secrets applied directly to the cloud cluster (`kubectl apply` from a local, untracked manifest) | No |

Rule: any value that grants access to a non-local resource (cloud DB, Docker Hub, production cluster, real signing keys) must never appear in the repo. `appsettings.json` (non-Development) holds only non-sensitive defaults.

External provider credentials are real credentials in **every** environment and follow the never-committed rule even in dev: the Google OAuth client ID/secret, production SMTP credentials, production Cloudflare Turnstile keys, and production object-storage (S3-compatible) keys are supplied via environment variables only. Dev email needs no credentials at all (Mailpit), dev Turnstile uses Cloudflare's official always-pass test keys (published for this purpose — safe to commit), and dev MinIO uses the default `minioadmin` credentials (local-only — committed by design).

### Docker Compose (Local Development)
- All services containerized
- PostgreSQL and MongoDB as database containers
- RabbitMQ as message broker
- Mailpit as dev email catcher (SMTP :1025, web UI :8025)
- MinIO as S3-compatible image storage (API :9000, console :9001) with an `mc` init container that creates the `auction-images` bucket and uploads the seed images
- Nginx reverse proxy with SSL (via acme-companion)

### Kubernetes (Production)
- Secrets management for database credentials, RabbitMQ, and app secrets (see Secrets & Configuration Management above — production values applied directly to the cluster, never committed)
- ClusterIP services for internal communication
- Ingress controllers for external access

### CI/CD
- PR validation workflow (`ci.yml`): pull requests to `develop` and `main` (and pushes to `develop`) build the backend solution, run all `dotnet test` projects, and lint/build the Next.js app; `main` is branch-protected and requires a pull request with a green CI run before merge
- Per-service deploy workflows: triggered on push to `main` for relevant service paths — build the Docker image, tag it with the commit SHA and `latest`, push both tags to Docker Hub, then roll the cluster to the SHA tag (`kubectl set image`) using the production kubeconfig from GitHub repository secrets
- Image tags: deployments reference immutable commit-SHA tags (rollback = re-deploy a previous SHA); `latest` is for local convenience only
- Database migrations: services apply pending EF Core migrations at startup (`Database.Migrate()`), so rolling out a new image also upgrades the schema — no separate migration step in the pipeline

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

All sample data is for development and demos. Each service seeds its own store on startup (`DbInitializer`, idempotent — skips if data exists); the Search Service syncs from the Auction Service via its HTTP polling fallback and needs no seed of its own.

### 8.1 Users (Identity Service)

| Username | Email | Password | Roles | Notes |
|----------|-------|----------|-------|-------|
| bob | bob@apexautobid.local | Pass123$ | — | Email confirmed |
| alice | alice@apexautobid.local | Pass123$ | — | Email confirmed |
| tom | tom@apexautobid.local | Pass123$ | — | Email confirmed |
| admin | admin@apexautobid.local | Pass123$ | admin | Email confirmed; unlocks the admin dashboard (§10) |

The shared dev password is committed by design (dev-only credentials — see §6). All seed users are pre-confirmed so they can create auctions and bid immediately.

### 8.2 Auctions (Auction Service)

`AuctionEnd` is computed relative to seed time (`UtcNow` + offset) so the data never goes stale. Image URLs point to the MinIO bucket (see §8.4): `{PublicBaseUrl}/auction-images/{key}`.

| # | Car | Color / Year / Mileage | Reserve | Seller | AuctionEnd | Status | Image key |
|---|-----|------------------------|---------|--------|------------|--------|-----------|
| 1 | Ford GT | White / 2020 / 50K | $20,000 | bob | +10 days | Live | ford-gt.jpg |
| 2 | Bugatti Veyron | Black / 2018 / 15K | $90,000 | alice | +60 days | Live | bugatti-veyron.jpg |
| 3 | Ford Mustang | Black / 2023 / 65K | none | bob | +4 days | Live | ford-mustang.jpg |
| 4 | Mercedes SLK | Silver / 2020 / 15K | $50,000 | tom | −1 day | ReserveNotMet | mercedes-slk.jpg |
| 5 | BMW X1 | White / 2017 / 90K | $20,000 | alice | +20 days | Live | bmw-x1.jpg |
| 6 | Ferrari Spider | Red / 2015 / 50K | $20,000 | bob | +45 days | Live | ferrari-spider.jpg |
| 7 | Ferrari F-430 | Red / 2022 / 5K | $150,000 | alice | +13 days | Live | ferrari-f430.jpg |
| 8 | Audi R8 | White / 2021 / 10K | none | bob | +30 days | Live | audi-r8.jpg |
| 9 | Audi TT | Black / 2020 / 25K | $20,000 | tom | +6 hours | Live | audi-tt.jpg |
| 10 | Ford Model T | Rust / 1938 / 150K | $20,000 | bob | −2 days | Finished (sold) | ford-model-t.jpg |

State variety by design: #9 exercises the `endingSoon` filter, #4 the ended-but-unsold state, and #10 the sold state — Winner `alice`, SoldAmount $25,000, `WinnerEmail` alice@apexautobid.local — so the post-sale contact exchange is demonstrable out of the box (bob sees alice's email on auction #10, alice sees bob's). #1 carries `CurrentHighBid` $18,000 from the seed bids below.

### 8.3 Bids (Bidding Service)

The Bidding Service seeds its local auction records (mirroring §8.2) plus the following bid history (every bid carries `BidderEmail` per §3.3):

| Auction | Bidder | Amount | Status |
|---------|--------|--------|--------|
| #1 Ford GT | alice | $15,000 | AcceptedBelowReserve |
| #1 Ford GT | tom | $18,000 | AcceptedBelowReserve |
| #4 Mercedes SLK | bob | $40,000 | AcceptedBelowReserve |
| #4 Mercedes SLK | alice | $45,000 | AcceptedBelowReserve |
| #10 Ford Model T | tom | $22,000 | Accepted |
| #10 Ford Model T | alice | $25,000 | Accepted |

These bids explain the auction states: #1's current high bid is $18,000 (below its $20,000 reserve), #4 ended with a $45,000 high bid under its $50,000 reserve (hence ReserveNotMet), and #10 sold to alice at $25,000.

### 8.4 Images (MinIO Object Storage)

- **MinIO** (S3-compatible) stores auction images: a single container in dev (S3 API on :9000, web console on :9001, dev credentials `minioadmin`/`minioadmin` — committed by design). In production, run MinIO on Kubernetes **or** point at any S3-compatible cloud service (AWS S3, Cloudflare R2, DigitalOcean Spaces, Backblaze B2) — only the endpoint and credential environment variables change.
- Bucket `auction-images` with anonymous **read** (download) policy — images are served directly by URL. Writes happen two ways only: the seed init container, and user uploads via short-lived presigned PUT URLs issued by the Auction Service (see §3.1 Image Upload).
- Least-privilege access: the Auction Service has a dedicated MinIO access key limited to `PutObject` on `auction-images/*` (dev key committed; production keys via environment variables). The `minioadmin` root credentials are used only by the dev console/init container, never by application code.
- 10 royalty-free sample car images are committed at `docker/seed-images/` and uploaded to the bucket at startup by an `mc` (MinIO client) init container.
- The Next.js image config (`remotePatterns`) must whitelist the image host (dev: `localhost:9000`; production: the storage host, from an environment variable).

---

## 9. API Testing

A **Postman collection** covering all API endpoints is created during Phase 8 (Task 6) and committed at `Docs/postman/ApexAutoBid.postman_collection.json`. Until then, the interactive Scalar docs (below) are the way to exercise the APIs manually.

### API Documentation

Each API service (Auction, Search, Bidding — plus the Identity Service's admin API once Phase 11 adds it) generates an OpenAPI document via the built-in `Microsoft.AspNetCore.OpenApi` package (`/openapi/v1.json`) and serves an interactive **Scalar** reference UI (`/scalar`). The **Gateway** hosts an aggregated Scalar page covering all service documents. Authenticated endpoints are documented with an OAuth2/Bearer security scheme; clicking Authorize runs the real IdentityServer login (authorization code + PKCE via the `scalar` client) and the obtained JWT is automatically attached to all "try it" requests. The Notification Service is excluded (SignalR hub only, no REST API).

---

## 10. Admin Dashboard

A role-gated admin area (`/admin` in the Next.js app) plus admin-only APIs across the services. Access requires the `admin` role claim; every admin endpoint returns 403 for non-admin callers. Built in **Phase 11** — no new service is added; each capability lives in the service that owns the data.

### 10.1 User Management (Identity Service)

| Method | Path | Description |
|--------|------|-------------|
| GET | api/admin/users | List/search users (paged) |
| POST | api/admin/users | Create a user (optionally pre-confirmed) |
| POST | api/admin/users/{id}/reset-password | Set a temporary password or send a reset link |
| POST | api/admin/users/{id}/resend-confirmation | Generate and send a new email-validation link |
| PUT | api/admin/users/{id}/roles | Assign/remove roles (e.g., grant `admin`) |
| PUT | api/admin/users/{id}/lock | Lock or unlock an account |
| GET | api/admin/users/stats | User counts (total, confirmed, locked) |

### 10.2 Auction & Bid Management

- **Create for any seller:** admins may pass an explicit `Seller` on `POST api/auctions` (including themselves) — see §3.1
- **End now:** `POST api/admin/auctions/{id}/end` sets `AuctionEnd = UtcNow` and emits `AuctionUpdated` (with `AuctionEnd`); the Bidding Service's background job then finalizes it through the normal flow (winner, `AuctionFinished`, notifications)
- **Cancel/invalidate:** `POST api/admin/auctions/{id}/cancel` sets status `Cancelled` and emits `AuctionCancelled` — Search marks the item cancelled, Bidding refuses further bids and never emits `AuctionFinished`, Notification broadcasts and informs the seller
- **Remove a bid:** `DELETE api/admin/bids/{id}` (Bidding Service) deletes the bid, recalculates the auction's high bid, and emits `BidRemoved (BidId, AuctionId, CurrentHighBid?)` so the Auction and Search Services refresh `CurrentHighBid`
- **Adjust duration:** admins may shorten/extend `AuctionEnd` on live auctions; regular sellers are bound by the platform duration limits
- **Duration limits:** the dashboard's Settings page edits the platform-wide min/max auction duration via `PUT api/admin/settings/duration`, persisted in a `PlatformSettings` table (Id, MinDuration, MaxDuration, UpdatedBy, UpdatedAt) in the Auction Service database. DB values override the environment-variable/config defaults (§3.1) and take effect immediately — no restart

### 10.3 Banner Messages (Auction Service)

Admins can publish banner messages shown on the home page, a specific auction page, or globally.

**Banner model:** Id, Message, Scope (`Global` | `HomePage` | `Auction`), AuctionId? (when Scope = Auction), ActiveFrom, ActiveUntil, CreatedBy

- Admin CRUD via `GET/POST api/admin/banners` and `PUT/DELETE api/admin/banners/{id}`; the public reads active banners via `GET api/banners?scope=&auctionId=` (Anon)
- Publishing emits `BannerPublished`, which the Notification Service broadcasts over SignalR so open pages show the banner without a refresh

### 10.4 Statistics

The admin dashboard landing page aggregates per-service stats endpoints (Admin-only, called through the gateway): `GET api/admin/users/stats` (Identity — user counts), `GET api/admin/auctions/stats` (Auction — auction counts by status), and `GET api/admin/bids/stats` (Bidding — total bid count). Stats paths are scoped by resource segment — a single shared `api/admin/stats` path would be unroutable at the gateway, since YARP would match only one service.

### 10.5 New Event Contracts

| Contract | Properties |
|----------|-----------|
| AuctionCancelled | AuctionId, Seller |
| BidRemoved | BidId, AuctionId, CurrentHighBid? |
| BannerPublished | Id, Message, Scope, AuctionId?, ActiveFrom, ActiveUntil |

`AuctionUpdated` additionally gains `AuctionEnd?` (see §3.1) so end-time changes propagate to the Bidding and Search Services.

---

## 11. Development Environment

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

## 12. Coding Conventions

- Private fields use underscore prefix with camelCase (e.g., `_myField`)
- EditorConfig enforced naming rules for C# private members

---

## 13. Cross-Cutting Concerns

### 13.1 Global Error Handling (Backend)

Every HTTP API service (Auction, Search, Bidding, Identity, Gateway) registers a global exception handler (`IExceptionHandler` + `AddProblemDetails()`) so that **all** error responses are RFC 7807 `application/problem+json`:

- Validation failures → **400** `ValidationProblemDetails` (standard ASP.NET Core model validation)
- Expected business outcomes (bid too low, auction finished, caller isn't the owner) are normal status-coded responses per each service's spec — never exceptions
- Unhandled exceptions → **500** ProblemDetails

**Development vs production:**

| | Development | Production |
|---|------------|------------|
| 500 `detail` | Exception type, message, and stack trace | Generic message ("An unexpected error occurred.") |
| Correlation | `traceId` included | `traceId` included — the full exception goes to structured logs (`ILogger`) only; correlate by `traceId` |

The gateway returns ProblemDetails for errors it generates itself (edge 401/403, 429 rate limiting); errors from proxied services pass through unchanged.

### 13.2 Global Error Handling (Frontend)

- App Router error boundaries: a root `global-error.tsx`, route-level `error.tsx` (friendly message + "Try again" reset button, styled per `Docs/DesignGuide.md`), and `not-found.tsx` for 404s
- API failures: parse the ProblemDetails body and surface its `title` as a red toast (react-hot-toast); never render `detail` or stack traces to users in production
- Development vs production: the Next.js dev overlay shows full errors during development; production builds show only the error-boundary UI (Next.js strips error details from production client bundles automatically). Server-side errors are logged to the server console

### 13.3 Transaction Auditing

Mutating operations leave an **append-only audit trail** in the owning service's own datastore. Each auditing service defines an `AuditEntry` record:

| Property | Type |
|----------|------|
| Id | Guid |
| Timestamp | DateTime (UTC) |
| Actor | string (username; `system` for background processes) |
| ActorIsAdmin | bool |
| Action | string (e.g., `AuctionCreated`, `BidRemoved`, `UserLocked`) |
| EntityType | string |
| EntityId | string |
| Data | string (JSON — payload summary or before/after snapshot) |

**Coverage:**

| Service | Audited operations |
|---------|--------------------|
| Auction | Auction create/update/delete; admin end/cancel; banner CRUD; duration settings changes |
| Bidding | Bid placement is already fully audited by the persisted bid history (bidder, time, amount, status); admin bid removal writes an `AuditEntry` capturing the removed bid |
| Identity | Admin user management: create user, password reset, resend confirmation, role changes, lock/unlock (failed logins are handled by ASP.NET Core Identity's lockout, §3.4) |
| Search / Notification | Read-only projections and push — no audit entries |

Rules: the audit write happens in the same unit of work as the mutation (same EF Core `SaveChanges`; for MongoDB, the same operation scope — best effort). Entries are append-only and are **not** exposed through any public API — they are inspected directly in the datastore; an admin read API is a possible later enhancement. `Data` never contains secrets or password material.
