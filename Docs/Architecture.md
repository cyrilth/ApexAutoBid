# ApexAutoBid - Architecture

## 1. Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend Services | .NET | 10 |
| Client App | Next.js (App Router) | 16.x |
| Identity Provider | Duende IdentityServer + ASP.NET Core Identity | Latest |
| API Gateway | YARP Reverse Proxy | Latest |
| Message Broker | RabbitMQ (via MassTransit) | Latest |
| Real-time | SignalR | (bundled with .NET 10) |
| API Documentation | OpenAPI (Microsoft.AspNetCore.OpenApi) + Scalar | Latest |
| Bot Protection | Cloudflare Turnstile + ASP.NET Core rate limiting | Latest |
| Object Storage | MinIO (S3-compatible — auction images) | Latest |
| Relational DB | PostgreSQL | Latest |
| Document DB | MongoDB | Latest |
| Containerization | Docker / Docker Compose | Latest |
| Orchestration | Kubernetes | Latest |
| CI/CD | GitHub Actions | - |
| Reverse Proxy | Nginx | Latest |

---

## 2. System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Client Platforms                           │
│                                                                     │
│   ┌──────────────┐          ┌──────────────────────┐                │
│   │  Web Browser  │          │  Mobile (Future)     │                │
│   └──────┬───────┘          │  iOS / Android       │                │
│          │                  └──────────────────────┘                │
└──────────┼──────────────────────────────────────────────────────────┘
           │
┌──────────▼──────────────────────────────────────────────────────────┐
│                         Docker Host                                 │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │                      INGRESS (Nginx)                        │    │
│  └────────────────────────┬────────────────────────────────────┘    │
│                           │                                         │
│  ┌────────────────────────▼────────────────────────────────────┐    │
│  │               BFF / Client App (Next.js 16)                 │    │
│  │           next-auth · SignalR client · zustand               │    │
│  └────────────────────────┬────────────────────────────────────┘    │
│                           │                                         │
│  ┌────────────────────────▼────────────────────────────────────┐    │
│  │                  GATEWAY (YARP + JWT Auth)                   │    │
│  └───┬──────────┬──────────┬──────────┬──────────┬─────────────┘    │
│      │          │          │          │          │                   │
│  ┌───▼───┐  ┌───▼───┐  ┌──▼────┐  ┌──▼────┐  ┌─▼──────────┐       │
│  │Auction│  │Search │  │Bidding│  │Ident- │  │Notification│       │
│  │  Svc  │  │  Svc  │  │  Svc  │  │ity Svc│  │    Svc     │       │
│  │       │  │       │  │       │  │       │  │  (SignalR)  │       │
│  └───┬───┘  └───┬───┘  └──┬──┬─┘  └───┬───┘  └─────┬──────┘       │
│      │          │          │  │        │             │               │
│      │          │          │  │gRPC    │             │               │
│      ├◄─────────────────────┘  │        │             │               │
│      │          │          │   │        │             │               │
│  ┌───▼───┐  ┌───▼───┐  ┌──▼────┐  ┌───▼───┐        │               │
│  │Postgres│  │MongoDB│  │MongoDB│  │Postgres│        │               │
│  │(auct.) │  │(srch.)│  │(bids) │  │(ident.)│        │               │
│  └───────┘  └───────┘  └───────┘  └───────┘        │               │
│                                                      │               │
│  ┌──────────────────────────────────────────────────┐│               │
│  │              EVENT BUS (RabbitMQ)                 ││               │
│  │                                                   ││               │
│  │  Auction Svc ◄──► Search Svc ◄──► Bidding Svc   ││               │
│  │       ▲               ▲               ▲          ││               │
│  │       └───────────────┼───────────────┘          ││               │
│  │                       ▼                          ││               │
│  │              Notification Svc ◄──────────────────┘│               │
│  └───────────────────────────────────────────────────┘               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 3. Service Communication

### 3.1 Asynchronous (Event Bus)

All services communicate asynchronously via **RabbitMQ** using **MassTransit** publish/subscribe.

```
Auction Service ──publishes──► AuctionCreated   ──► Search, Bidding, Notification
Auction Service ──publishes──► AuctionUpdated   ──► Search, Bidding
Auction Service ──publishes──► AuctionDeleted   ──► Search
Auction Service ──publishes──► AuctionCancelled ──► Search, Bidding, Notification
Auction Service ──publishes──► BannerPublished  ──► Notification
Bidding Service ──publishes──► BidPlaced        ──► Auction, Search, Notification
Bidding Service ──publishes──► BidRemoved       ──► Auction, Search
Bidding Service ──publishes──► AuctionFinished  ──► Auction, Search, Notification
```

### 3.2 Synchronous (gRPC)

The **Bidding Service** calls the **Auction Service** via gRPC as a fallback when the `AuctionCreated` event has not yet been consumed (e.g., a bid is placed before the event arrives). **Polly** retry policies handle transient failures.

```
Bidding Service ──gRPC──► Auction Service (GetAuction)
```

### 3.3 Real-time (SignalR)

The **Notification Service** exposes a SignalR hub at `/notifications` that the Next.js client connects to for real-time push notifications (new bids, auction results). Anonymous connections receive broadcasts only; authenticated clients connect with their JWT (`access_token` query parameter) and are mapped to their username via an `IUserIdProvider`, enabling targeted messages — when an auction finishes, the winner receives `AuctionWon` and the seller receives `AuctionSellerResult` via `Clients.User(...)`.

Post-sale contact exchange does **not** go through SignalR: after a sale, the Auction Service's `GET api/auctions/{id}` conditionally reveals `WinnerEmail` to the seller and `SellerEmail` to the winner (emails flow `email` claim → `Bid.BidderEmail` → `AuctionFinished.WinnerEmail` → Auction record, and are never stored in the search index or pushed over the hub).

### 3.4 Image Upload (Presigned URLs)

Auction images go directly from the browser to object storage — image bytes never pass through the services. Each auction carries an ordered gallery of 1–10 images (`ItemImage` child entities; `SortOrder = 0` is the primary image), each capped at 5 MB by default (`Images__MaxPerAuction` / `Images__MaxSizeMB`). The upload flow runs once per image:

```
1. Client ──► POST api/auctions/upload-url (JWT, content type + size) ──► Auction Service
2. Auction Service ──► validates content type + declared size ──► returns presigned PUT URL (5 min, signs Content-Length) + object URL
3. Client ──► PUT image bytes ──► MinIO (auction-images bucket)
4. Client ──► submits create/edit form with the ordered Images list (first = primary)
5. Auction Service ──► HEAD-verifies actual object sizes on create/update ──► rejects (400) oversized objects (does not delete — can't prove the caller owns the referenced key)
```

The Auction Service signs uploads with a dedicated MinIO access key scoped to `PutObject` + `DeleteObject` on the bucket only (least privilege — the bucket is anonymous read, so no read grant is needed; `DeleteObject` is reserved for object-lifecycle cleanup — e.g. removing an auction's images when the auction is deleted — not for deleting client-referenced objects during create/update). An optional follow-up call (`POST api/auctions/thumbnail`) has the Auction Service fetch an uploaded object via the public read path, resize it with ImageSharp (max 400px, WebP), and store it under `thumbs/` — listings, search results, and social link previews use the **primary image's** thumbnail, the detail page the full gallery. Events and the search index carry only the primary image; the full gallery is served by `GET api/auctions/{id}`.

### 3.5 Gateway Routing

The **Gateway** uses **YARP Reverse Proxy** to route client requests to backend services. It handles JWT bearer token validation so that individual services can trust authenticated requests forwarded through the gateway.

```
Client ──► Gateway ──► /api/auctions*        ──► Auction Service
                   ──► /api/banners*         ──► Auction Service
                   ──► /api/search*          ──► Search Service
                   ──► /api/bids*            ──► Bidding Service
                   ──► /api/admin/users*     ──► Identity Service
                   ──► /api/admin/auctions*  ──► Auction Service
                   ──► /api/admin/banners*   ──► Auction Service
                   ──► /api/admin/settings*  ──► Auction Service
                   ──► /api/admin/bids*      ──► Bidding Service
                   ──► /notifications        ──► Notification Service
                   ──► /openapi/{svc}        ──► service OpenAPI documents (aggregated docs)
                   ──► /api/version          ──► handled by the gateway itself (platform version — see Docs/Versioning.md)
```

Admin routes are scoped by resource segment so every endpoint — including each service's stats endpoint (`/api/admin/users/stats`, `/api/admin/auctions/stats`, `/api/admin/bids/stats`) — maps to exactly one service; a path shared across services (e.g., a single `/api/admin/stats`) would be unroutable.

---

## 4. Data Architecture

### 4.1 Database Per Service

Each service owns its data store. No service directly accesses another service's database.

| Service | Database | Engine | Rationale |
|---------|----------|--------|-----------|
| Auction Service | auctions | PostgreSQL | Relational data with strong consistency for auction/item records |
| Identity Service | identity | PostgreSQL | ASP.NET Core Identity requires relational store |
| Search Service | search | MongoDB | Flexible document model for search indexing, full-text search |
| Bidding Service | bids | MongoDB | High-write throughput for bid entries, simple document model |

### 4.2 Data Synchronization

Services maintain local projections of data they need from other services, synchronized via events:

- **Search Service** maintains a full copy of auction data (from `AuctionCreated`, `AuctionUpdated`, `AuctionDeleted`) plus bid/finish state (from `BidPlaced`, `AuctionFinished`)
- **Bidding Service** maintains a minimal local `Auction` record (from `AuctionCreated`) with just AuctionEnd, Seller, ReservePrice, and Finished status
- **Auction Service** updates CurrentHighBid, Winner, SoldAmount, and Status from `BidPlaced` and `AuctionFinished` events

---

## 5. Authentication & Authorization

### 5.1 Identity Provider

**Duende IdentityServer** with **ASP.NET Core Identity** serves as the central Security Token Service (STS).

- Issues JWT access tokens via OAuth2/OpenID Connect (claims: `username`, `email`, `email_verified`, `role` — the `admin` role gates `api/admin/*` and the admin dashboard)
- Manages user registration and authentication, with **email verification** (`RequireConfirmedEmail`): confirmation emails go to Mailpit in dev, a real SMTP provider in production; the Auction and Bidding Services require the `email_verified` claim for creating auctions and placing bids
- **Google external login** on the login/register pages (client ID/secret via environment variables only — never committed; disabled when absent). Google-verified emails count as confirmed.
- PostgreSQL stores user accounts and identity data
- Uses **Polly** for resilient database connections during startup

### 5.2 Auth Flow

```
1. User ──► Next.js App (next-auth) ──► Identity Service (login)
2. Identity Service ──► issues JWT token ──► Next.js App (stores in session)
3. Next.js App ──► Gateway (JWT in Authorization header)
4. Gateway ──► validates JWT ──► forwards to backend service
5. Backend service ──► reads claims (username, email, email_verified, role) from validated token
```

### 5.3 Endpoint Authorization

| Access Level | Description |
|-------------|-------------|
| Auth | Requires valid JWT. User identity extracted from claims. |
| Anon | No authentication required. Publicly accessible. |
| Admin | Requires valid JWT with the `admin` role claim (`api/admin/*` endpoints). Enforced at the gateway and again in each service. |

Resource-level authorization (e.g., only the seller can update/delete their auction) is enforced within individual services.

### 5.4 Bot Protection & Rate Limiting

| Layer | Mechanism | Protects |
|-------|-----------|----------|
| Identity Service | Cloudflare Turnstile (server-side `siteverify`) on the register page | Bot signups, confirmation-email abuse |
| Identity Service | ASP.NET Core Identity account lockout + rate limiting on login/register/token | Credential stuffing, registration floods |
| Gateway | `Microsoft.AspNetCore.RateLimiting` — per-IP general policy, stricter on mutating endpoints (429 on excess) | API abuse, bid spam, scraping |

Dev/Docker use Cloudflare's official always-pass Turnstile test keys (committable); production keys come from environment variables. Authenticated actions need no captcha — lockout, rate limits, and the verified-email requirement cover them.

---

## 6. Resilience Patterns

| Pattern | Library | Used By | Purpose |
|---------|---------|---------|---------|
| Retry | Polly | Bidding Service | Retry gRPC/HTTP calls to Auction Service on transient failure |
| Retry | Polly | Identity Service | Retry database connections during startup |
| Retry | Polly | Search Service | Retry HTTP calls via `Microsoft.Extensions.Http.Resilience` (Polly v8) |
| Message Retry | MassTransit | All MassTransit services | Automatic retry of failed message consumers |
| Outbox | MassTransit | Auction, Search, Bidding | Ensures published messages are not lost if the broker is temporarily down (requires a database — not applicable to Notification, Identity, or Gateway) |

---

## 7. Repository Structure

### Clean Architecture Layer Rules

AuctionService, SearchService, and BiddingService use Clean Architecture with 4 projects each. Dependency direction is strictly enforced:

```
API → Application → Domain
Infrastructure → Application → Domain
```

- **Domain** has zero external NuGet dependencies. Contains entities, enums, value objects, and domain interfaces.
- **Application** depends only on Domain. Contains DTOs, Mapster mapping configs (`IRegister`), MassTransit consumers, application services, and RequestHelpers. NuGet: Mapster, MassTransit, Contracts project ref.
- **Infrastructure** depends only on Application (and transitively Domain). Contains DbContext, migrations, repository implementations, gRPC clients/servers, HTTP clients. NuGet: EF Core, Npgsql, MongoDB.Entities, Grpc.Net.Client, Polly.
- **API** depends on Application and Infrastructure. Contains controllers, Program.cs, Dockerfile, middleware. NuGet: Microsoft.AspNetCore.Authentication.JwtBearer.

IdentityService, GatewayService, and NotificationService remain flat (single project) due to minimal domain logic.

```
ApexAutoBid/
├── backend/
│   ├── AuctionService/
│   │   ├── AuctionService.Domain/
│   │   │   ├── Entities/
│   │   │   ├── Enums/
│   │   │   └── Interfaces/
│   │   ├── AuctionService.Application/
│   │   │   ├── DTOs/
│   │   │   ├── Consumers/
│   │   │   ├── Mappings/
│   │   │   ├── Services/
│   │   │   └── RequestHelpers/
│   │   ├── AuctionService.Infrastructure/
│   │   │   └── Data/                  # DbContext, migrations, repos
│   │   └── AuctionService.API/
│   │       ├── Controllers/
│   │       ├── Middleware/
│   │       ├── Services/              # gRPC service implementation
│   │       ├── Program.cs
│   │       └── Dockerfile
│   │
│   ├── SearchService/
│   │   ├── SearchService.Domain/
│   │   │   ├── Entities/
│   │   │   └── Interfaces/
│   │   ├── SearchService.Application/
│   │   │   ├── DTOs/
│   │   │   ├── Consumers/
│   │   │   ├── Mappings/
│   │   │   ├── Services/
│   │   │   └── RequestHelpers/
│   │   ├── SearchService.Infrastructure/
│   │   │   └── Data/                  # MongoDB connection, repos
│   │   └── SearchService.API/
│   │       ├── Controllers/
│   │       ├── Program.cs
│   │       └── Dockerfile
│   │
│   ├── BiddingService/
│   │   ├── BiddingService.Domain/
│   │   │   ├── Entities/
│   │   │   ├── Enums/
│   │   │   └── Interfaces/
│   │   ├── BiddingService.Application/
│   │   │   ├── DTOs/
│   │   │   ├── Consumers/
│   │   │   ├── Mappings/
│   │   │   └── Services/
│   │   ├── BiddingService.Infrastructure/
│   │   │   └── Data/                  # MongoDB connection, repos
│   │   └── BiddingService.API/
│   │       ├── Controllers/
│   │       ├── Services/              # Background service for auction finish check
│   │       ├── Program.cs
│   │       └── Dockerfile
│   │
│   ├── IdentityService/
│   │   ├── Data/
│   │   ├── Models/
│   │   ├── Pages/                     # Razor Pages for login/register UI
│   │   ├── Services/
│   │   ├── Config.cs                  # IdentityServer client/scope config
│   │   ├── Program.cs
│   │   ├── Dockerfile
│   │   └── IdentityService.csproj
│   │
│   ├── GatewayService/
│   │   ├── appsettings.json           # YARP route configuration
│   │   ├── Program.cs
│   │   ├── Dockerfile
│   │   └── GatewayService.csproj
│   │
│   ├── NotificationService/
│   │   ├── Consumers/
│   │   ├── Hubs/                      # SignalR hub
│   │   ├── Program.cs
│   │   ├── Dockerfile
│   │   └── NotificationService.csproj
│   │
│   └── Contracts/                     # Shared event contracts (NuGet or project ref)
│       ├── AuctionCreated.cs
│       ├── AuctionUpdated.cs
│       ├── AuctionDeleted.cs
│       ├── BidPlaced.cs
│       ├── AuctionFinished.cs
│       └── Contracts.csproj
│
├── frontend/
│   └── web-app/
│       ├── app/                       # Next.js App Router
│       │   ├── layout.tsx
│       │   ├── page.tsx
│       │   ├── auctions/
│       │   ├── session/
│       │   └── api/auth/              # next-auth route handlers
│       ├── components/
│       ├── hooks/
│       ├── lib/
│       ├── types/
│       ├── public/
│       ├── next.config.ts
│       ├── tailwind.config.ts
│       ├── package.json
│       ├── Dockerfile
│       └── tsconfig.json
│
├── docker/
│   └── docker-compose.yml             # Local dev: all services + infra
│
├── k8s/
│   ├── dev-secrets.yaml
│   ├── postgres-depl.yaml
│   ├── mongo-depl.yaml
│   ├── rabbit-depl.yaml
│   ├── auction-depl.yaml
│   ├── search-depl.yaml
│   ├── bid-depl.yaml
│   ├── identity-depl.yaml
│   ├── gateway-depl.yaml
│   ├── notification-depl.yaml
│   ├── webapp-depl.yaml
│   └── ingress-svc.yaml
│
├── .github/
│   └── workflows/
│       ├── ci.yml                        # PR validation: build + tests + frontend lint/build
│       ├── deploy-auction.yml
│       ├── deploy-search.yml
│       ├── deploy-bid.yml
│       ├── deploy-identity.yml
│       ├── deploy-gateway.yml
│       ├── deploy-notification.yml
│       └── deploy-webapp.yml
│
├── tests/
│   ├── AuctionService.UnitTests/         # References AuctionService.Application
│   │   └── AuctionService.UnitTests.csproj
│   ├── AuctionService.IntegrationTests/  # References AuctionService.API
│   │   └── AuctionService.IntegrationTests.csproj
│   ├── SearchService.UnitTests/
│   ├── SearchService.IntegrationTests/
│   ├── BiddingService.UnitTests/
│   ├── BiddingService.IntegrationTests/
│   ├── IdentityService.UnitTests/
│   ├── IdentityService.IntegrationTests/
│   ├── GatewayService.IntegrationTests/
│   └── NotificationService.IntegrationTests/
│
├── Docs/
│   ├── Requirements.md
│   ├── Architecture.md
│   ├── Tasks.md
│   ├── AgentGuide.md
│   └── postman/                       # Postman collection (created in Phase 8)
│
├── .editorconfig
├── .gitignore
├── ApexAutoBid.slnx
└── README.md
```

---

## 8. Deployment Architecture

### 8.1 Local Development (Docker Compose)

All services, databases, and infrastructure run in Docker Compose for local development.

```
docker-compose.yml
├── postgres          (host port 5434 → container 5432; 5434 avoids clashing with pre-existing local Postgres instances on 5432/5433)
├── mongodb           (port 27017)
├── rabbitmq          (port 5672, mgmt 15672)
├── auction-svc       (port 7001)
├── search-svc        (port 7002)
├── bid-svc           (port 7003)
├── identity-svc      (port 5000)
├── gateway-svc       (port 6001)
├── notification-svc  (port 7004)
├── web-app           (port 3000)
├── mailpit           (SMTP 1025, web UI 8025 — dev email catcher)
├── minio             (S3 API 9000, console 9001 — auction images; mc init container seeds the bucket)
└── nginx             (ports 80/443, SSL via acme-companion)
```

### 8.2 Kubernetes (Production)

```
┌──────────────────────────────────────────┐
│            Kubernetes Cluster            │
│                                          │
│  ┌────────────────────────────────────┐  │
│  │        Ingress Controller          │  │
│  └──────────────┬─────────────────────┘  │
│                 │                         │
│  ┌──────────────▼─────────────────────┐  │
│  │      Services (ClusterIP)          │  │
│  │                                    │  │
│  │  webapp ─ gateway ─ identity       │  │
│  │  auction ─ search ─ bid            │  │
│  │  notification ─ rabbitmq           │  │
│  │  postgres ─ mongodb ─ minio        │  │
│  └────────────────────────────────────┘  │
│                                          │
│  ┌────────────────────────────────────┐  │
│  │     Secrets (Opaque)               │  │
│  │  postgres-secret · mongo-secret    │  │
│  │  rabbit-secret · auction-secret    │  │
│  │  search-secret · bid-secret        │  │
│  │  identity-secret · webapp-secret   │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

**Secrets per environment:** local development uses `appsettings.Development.json` per service and `docker-compose.yml` environment blocks (dev-only values, committed by design); local Kubernetes uses the committed `k8s/dev-secrets.yaml`; production secrets and CI credentials (GitHub repository secrets) are never committed — see `Requirements.md` §6 for the full strategy.

### 8.3 CI/CD Pipeline

**PR validation (`ci.yml`):** every pull request to `develop` or `main` (and every push to `develop`) builds the backend solution, runs all `dotnet test` projects, and lints/builds the Next.js app. `main` is branch-protected — merging requires a pull request with a green CI run.

**Deploy:** each service has its own GitHub Actions workflow, triggered on push to `main` and filtered by service path:

```
Push to main (backend/AuctionService/**) ──► Build image ──► Push :<sha> + :latest ──► kubectl set image (:<sha>)
Push to main (backend/SearchService/**)  ──► Build image ──► Push :<sha> + :latest ──► kubectl set image (:<sha>)
...etc for each service
```

Deployments reference immutable commit-SHA tags, so a rollback is just re-deploying a previous SHA; `latest` exists for local convenience only. The deploy job authenticates with a production kubeconfig stored in GitHub repository secrets. Services apply pending EF Core migrations at startup (`Database.Migrate()`), so rolling out a new image also upgrades the schema.

### 8.4 Docker Build Strategy

**Backend services (.NET):** Standard `dotnet publish` Dockerfile.

**Frontend (Next.js):** Multi-stage build:
```
Stage 1 (deps)    ──► Install production dependencies
Stage 2 (builder) ──► Build Next.js app (standalone output)
Stage 3 (runner)  ──► Copy standalone output, run as non-root user on port 3000
```

---

## 9. Shared Contracts

The `backend/Contracts/` project contains the event contract classes shared across all services. Each event is a plain C# record/class with no dependencies, referenced by all services that publish or consume it.

| Contract | Properties |
|----------|-----------|
| AuctionCreated | Id, CreatedAt, UpdatedAt, AuctionEnd, Seller, Winner, Make, Model, Year, Color, Mileage, ImageUrl (primary), ThumbnailUrl? (primary), Status, ReservePrice, SoldAmount?, CurrentHighBid? |
| AuctionUpdated | Id, Make, Model, Color, Mileage, Year, ImageUrl (primary), ThumbnailUrl? (primary), AuctionEnd? |
| AuctionDeleted | Id |
| BidPlaced | Id, AuctionId, Bidder, BidTime, Amount, BidStatus |
| AuctionFinished | ItemSold, AuctionId, Winner?, WinnerEmail?, Seller, Amount? |
| AuctionCancelled | AuctionId, Seller |
| BidRemoved | BidId, AuctionId, CurrentHighBid? |
| BannerPublished | Id, Message, Scope, AuctionId?, ActiveFrom, ActiveUntil |

These contracts are the **single source of truth** for event shapes, ensuring consistency across all publishers and consumers.

---

## 10. API Documentation

Each API-exposing service generates an OpenAPI document using the built-in `Microsoft.AspNetCore.OpenApi` package and serves an interactive **Scalar** reference UI (`Scalar.AspNetCore`):

| Service | OpenAPI Document | Scalar UI |
|---------|------------------|-----------|
| Auction Service | `/openapi/v1.json` | `/scalar` |
| Search Service | `/openapi/v1.json` | `/scalar` |
| Bidding Service | `/openapi/v1.json` | `/scalar` |
| Gateway | aggregates all of the above via YARP | `/scalar` (one page, all documents) |

- JWT-protected endpoints are documented with an OpenAPI **security scheme** added via a document transformer (the built-in generator does not auto-detect `[Authorize]`).
- The **Gateway** proxies each service's OpenAPI document through YARP and hosts a single aggregated Scalar page (`AddDocument` per service).
- The docs UI authenticates against **IdentityServer** using the **OAuth2 authorization code flow + PKCE** (dedicated `scalar` public client): clicking Authorize runs the real IdentityServer login, and the obtained JWT is automatically attached to all "try it" requests. The token endpoint allows CORS from the docs origins for the browser-based code exchange.
- The Notification Service is excluded (SignalR hub only, no REST API).
