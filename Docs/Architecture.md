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
| Retry | Polly | Search Service | Retry HTTP calls via `Microsoft.Extensions.Http.Polly` |
| Message Retry | MassTransit | All services | Automatic retry of failed message consumers |
| Outbox | MassTransit | All services | Ensures messages are published even if the broker is temporarily down |

---

## 7. Repository Structure

```
ApexAutoBid/
├── backend/
│   ├── AuctionService/
│   │   ├── Controllers/
│   │   ├── Data/
│   │   ├── DTOs/
│   │   ├── Entities/
│   │   ├── Consumers/
│   │   ├── Services/                  # gRPC service implementation
│   │   ├── RequestHelpers/
│   │   ├── Program.cs
│   │   ├── Dockerfile
│   │   └── AuctionService.csproj
│   │
│   ├── SearchService/
│   │   ├── Controllers/
│   │   ├── Data/
│   │   ├── Models/
│   │   ├── Consumers/
│   │   ├── RequestHelpers/
│   │   ├── Program.cs
│   │   ├── Dockerfile
│   │   └── SearchService.csproj
│   │
│   ├── BiddingService/
│   │   ├── Controllers/
│   │   ├── Models/
│   │   ├── Consumers/
│   │   ├── Services/                  # Background service for auction finish check
│   │   ├── Program.cs
│   │   ├── Dockerfile
│   │   └── BiddingService.csproj
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
│   ├── AuctionService.UnitTests/
│   │   └── AuctionService.UnitTests.csproj
│   └── AuctionService.IntegrationTests/
│       └── AuctionService.IntegrationTests.csproj
│
├── Docs/
│   ├── Requirements.md
│   ├── Architecture.md
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
└── web-app           (port 3000)
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
Push to main (src/AuctionService/**) ──► Build Docker image ──► Push to Docker Hub
Push to main (src/SearchService/**)  ──► Build Docker image ──► Push to Docker Hub
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

The `src/Contracts/` project contains the event contract classes shared across all services. Each event is a plain C# record/class with no dependencies, referenced by all services that publish or consume it.

| Contract | Properties |
|----------|-----------|
| AuctionCreated | Id, CreatedAt, UpdatedAt, AuctionEnd, Seller, Winner, Make, Model, Year, Color, Mileage, ImageUrl, Status, ReservePrice, SoldAmount?, CurrentHighBid? |
| AuctionUpdated | Id, Make, Model, Color, Mileage, Year |
| AuctionDeleted | Id |
| BidPlaced | Id, AuctionId, Bidder, BidTime, Amount, BidStatus |
| AuctionFinished | ItemSold, AuctionId, Winner?, Seller, Amount? |

These contracts are the **single source of truth** for event shapes, ensuring consistency across all publishers and consumers.
