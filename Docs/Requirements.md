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
- AutoMapper.Extensions.Microsoft.DependancyInjection
- Microsoft.AspNetCore.Authentication.JwtBearer
- Microsoft.EntityFrameworkCore.Design
- Npgsql.EntityFrameworkCore.PostgreSQL
- MassTransit.RabbitMQ

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| POST | api/auctions | Create auction | Auth |
| PUT | api/auctions/:id | Update auction | Auth |
| DELETE | api/auctions/:id | Delete auction | Auth |
| GET | api/auctions | Get all auctions | Anon |
| GET | api/auctions/:id | Get auction by id | Anon |

**Commands:**
- **CreateAuction** - Creates an Item. Emits `AuctionCreated`
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
| Seller | string |
| Amount? | int |

**Models:**

**Auction.cs**

| Property | Type | Default |
|----------|------|---------|
| Id | Guid | |
| ReservePrice | int | 0 |
| Seller | string | (from claim) |
| Winner? | string | |
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
- AutoMapper.Extensions.Microsoft.DependancyInjection
- Microsoft.Extensions.Http.Polly
- MongoDB.Entities
- MassTransit.RabbitMQ

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | api/search?query | Paged list of auctions (searchTerm, pageSize, pageNumber, seller, winner, orderBy, filterBy) | Anon |

**Events Consumed:** `AuctionService.AuctionCreated`, `AuctionService.AuctionUpdated`, `AuctionService.AuctionDeleted`, `BidService.AuctionFinished`, `BidService.BidPlaced`

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
- AutoMapper.Extensions.Microsoft.DependancyInjection
- Microsoft.Extensions.Http.Polly
- MongoDB.Entities
- MassTransit.RabbitMQ
- Google.Protobuf
- Grpc.Net.Client
- Grpc.Tools
- Microsoft.AspNetCore.Authentication.JwtBearer
- Polly

**API Endpoints:**

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| POST | api/bids | Place bid (auctionId, amount) | Auth |
| GET | api/bids/{auctionId} | Get bids for auction | Anon |

**Events Emitted:** `BidPlaced`, `AuctionFinished`
**Events Consumed:** `AuctionService.AuctionCreated`

**gRPC:** The Bidding Service uses gRPC to directly call the Auction Service as a fallback mechanism when event data is not yet available (e.g., fetching auction details if the `AuctionCreated` event hasn't been consumed yet). Uses Polly for retry/resilience.

**Resilience:** Uses **Polly** via `Microsoft.Extensions.Http.Polly` for retry policies on HTTP/gRPC calls to handle transient failures.

**Models:**

**Bid.cs:** ID, AuctionId, BidTime (UtcNow), Amount, BidStatus

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

**Resilience:** Uses **Polly** for retry policies on database connections during startup.

---

### 3.5 Gateway Service

**Infrastructure:** .NET Web, YARP Reverse Proxy

**NuGet Packages:**
- Microsoft.AspNetCore.Authentication.JwtBearer
- Yarp.ReverseProxy

---

### 3.6 Notification Service

**Infrastructure:** .NET Web, RabbitMQ (MassTransit), SignalR

**NuGet Packages:**
- MassTransit.RabbitMQ

**SignalR Hub:** `/notifications` (Anon)

**Events Consumed:** `AuctionService.AuctionCreated`, `BidService.AuctionFinished`, `BidService.BidPlaced`

**Consumed Event Payloads:**

*AuctionCreated:* (same as AuctionCreated payload in Section 3.1)

*BidService.AuctionFinished:*

| Property | Type |
|----------|------|
| ItemSold | bool |
| AuctionId | string |
| Winner? | string |
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

### Docker Compose (Local Development)
- All services containerized
- PostgreSQL and MongoDB as database containers
- RabbitMQ as message broker
- Nginx reverse proxy with SSL (via acme-companion)

### Kubernetes (Production)
- Secrets management for database credentials, RabbitMQ, and app secrets
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
