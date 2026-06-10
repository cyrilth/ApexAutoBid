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
Auction Service ──publishes──► AuctionCreated ──► Search, Bidding, Notification
Auction Service ──publishes──► AuctionUpdated ──► Search
Auction Service ──publishes──► AuctionDeleted ──► Search
Bidding Service ──publishes──► BidPlaced      ──► Auction, Search, Notification
Bidding Service ──publishes──► AuctionFinished──► Auction, Search, Notification
```

### 3.2 Synchronous (gRPC)

The **Bidding Service** calls the **Auction Service** via gRPC as a fallback when the `AuctionCreated` event has not yet been consumed (e.g., a bid is placed before the event arrives). **Polly** retry policies handle transient failures.

```
Bidding Service ──gRPC──► Auction Service (GetAuction)
```

### 3.3 Real-time (SignalR)

The **Notification Service** exposes a SignalR hub at `/notifications` that the Next.js client connects to for real-time push notifications (new bids, auction results).

### 3.4 Gateway Routing

The **Gateway** uses **YARP Reverse Proxy** to route client requests to backend services. It handles JWT bearer token validation so that individual services can trust authenticated requests forwarded through the gateway.

```
Client ──► Gateway ──► /api/auctions/*    ──► Auction Service
                   ──► /api/search*       ──► Search Service
                   ──► /api/bids/*        ──► Bidding Service
                   ──► /notifications     ──► Notification Service
                   ──► /openapi/{svc}     ──► service OpenAPI documents (aggregated docs)
```

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

- Issues JWT access tokens via OAuth2/OpenID Connect
- Manages user registration and authentication
- PostgreSQL stores user accounts and identity data
- Uses **Polly** for resilient database connections during startup

### 5.2 Auth Flow

```
1. User ──► Next.js App (next-auth) ──► Identity Service (login)
2. Identity Service ──► issues JWT token ──► Next.js App (stores in session)
3. Next.js App ──► Gateway (JWT in Authorization header)
4. Gateway ──► validates JWT ──► forwards to backend service
5. Backend service ──► reads claims (username, etc.) from validated token
```

### 5.3 Endpoint Authorization

| Access Level | Description |
|-------------|-------------|
| Auth | Requires valid JWT. User identity extracted from claims. |
| Anon | No authentication required. Publicly accessible. |

Resource-level authorization (e.g., only the seller can update/delete their auction) is enforced within individual services.

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
│   └── Initial_Planning/
│
├── .editorconfig
├── .gitignore
├── ApexAutoBid.sln
└── README.md
```

---

## 8. Deployment Architecture

### 8.1 Local Development (Docker Compose)

All services, databases, and infrastructure run in Docker Compose for local development.

```
docker-compose.yml
├── postgres          (port 5432)
├── mongodb           (port 27017)
├── rabbitmq          (port 5672, mgmt 15672)
├── auction-svc       (port 7001)
├── search-svc        (port 7002)
├── bid-svc           (port 7003)
├── identity-svc      (port 5000)
├── gateway-svc       (port 6001)
├── notification-svc  (port 7004)
├── web-app           (port 3000)
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
│  │  postgres ─ mongodb                │  │
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

### 8.3 CI/CD Pipeline

Each service has its own GitHub Actions workflow:

```
Push to main (backend/AuctionService/**) ──► Build Docker image ──► Push to Docker Hub
Push to main (backend/SearchService/**)  ──► Build Docker image ──► Push to Docker Hub
...etc for each service
```

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
| AuctionCreated | Id, CreatedAt, UpdatedAt, AuctionEnd, Seller, Winner, Make, Model, Year, Color, Mileage, ImageUrl, Status, ReservePrice, SoldAmount?, CurrentHighBid? |
| AuctionUpdated | Id, Make, Model, Color, Mileage, Year |
| AuctionDeleted | Id |
| BidPlaced | Id, AuctionId, Bidder, BidTime, Amount, BidStatus |
| AuctionFinished | ItemSold, AuctionId, Winner?, Seller, Amount? |

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
