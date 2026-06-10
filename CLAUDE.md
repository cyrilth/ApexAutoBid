# Project Instructions

## Project Context

ApexAutoBid is a real-time online car auction platform built with a microservices architecture. Users can browse auctions, place bids, and receive live updates. The project is structured in 10 phases — track progress in `Docs/Tasks.md`.

### Key Documentation

- `Docs/Architecture.md` — System architecture, diagrams, data flow
- `Docs/Tasks.md` — Task tracker with checkable boxes per phase (source of truth for progress)
- `Docs/AgentGuide.md` — How to use sub-agents for development
- `Docs/Requirements.md` — Functional and non-functional requirements

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend Services | .NET 10 (C#) |
| Frontend | Next.js 16.x (App Router, TypeScript) |
| Identity | Duende IdentityServer + ASP.NET Core Identity (email verification + Google login) |
| Email (dev) | Mailpit (SMTP catcher container) |
| Bot Protection | Cloudflare Turnstile (register page) + ASP.NET Core rate limiting (gateway + identity) |
| API Gateway | YARP Reverse Proxy |
| Messaging | RabbitMQ via MassTransit (Outbox pattern) |
| Real-time | SignalR |
| API Docs | OpenAPI (Microsoft.AspNetCore.OpenApi) + Scalar |
| Relational DB | PostgreSQL (EF Core, Npgsql) |
| Document DB | MongoDB (MongoDB.Entities) |
| Mapping | Mapster |
| Resilience | Polly |
| CSS | Tailwind CSS + Flowbite React |
| State | Zustand |
| Forms | react-hook-form |
| Containers | Docker / Docker Compose |
| Orchestration | Kubernetes |
| CI/CD | GitHub Actions |

## Project Structure

```
ApexAutoBid/
├── backend/              # .NET microservices
│   ├── AuctionService/   # Clean Architecture (4 projects)
│   │   ├── AuctionService.Domain/
│   │   ├── AuctionService.Application/
│   │   ├── AuctionService.Infrastructure/
│   │   └── AuctionService.API/
│   ├── SearchService/    # Clean Architecture (4 projects)
│   │   ├── SearchService.Domain/
│   │   ├── SearchService.Application/
│   │   ├── SearchService.Infrastructure/
│   │   └── SearchService.API/
│   ├── BiddingService/   # Clean Architecture (4 projects)
│   │   ├── BiddingService.Domain/
│   │   ├── BiddingService.Application/
│   │   ├── BiddingService.Infrastructure/
│   │   └── BiddingService.API/
│   ├── IdentityService/  # Flat (single project)
│   ├── GatewayService/   # Flat (single project)
│   ├── NotificationService/ # Flat (single project)
│   └── Contracts/        # Shared event contracts
├── frontend/
│   └── web-app/          # Next.js app
├── tests/                # Unit and integration tests
├── docker/               # Docker Compose files
├── k8s/                  # Kubernetes manifests (Phase 9)
├── .github/workflows/    # CI/CD pipelines (Phase 10)
├── Docs/                 # Documentation
└── .claude/agents/       # Sub-agent definitions
```

## Agent Usage

This project uses 7 custom sub-agents in `.claude/agents/`. See `Docs/AgentGuide.md` for full details.

| Agent | When to use |
|-------|-------------|
| `task-manager` | Look up tasks from `Docs/Tasks.md`, check progress, mark tasks complete |
| `dotnet-service-builder` | Scaffold .NET services, entities, DTOs, controllers, consumers, tests |
| `frontend-builder` | Build Next.js components, pages, hooks, auth, state management |
| `test-runner` | Run `dotnet test` and report pass/fail summary |
| `playwright-tester` | Run Playwright e2e tests and report results |
| `docker-validator` | Validate Dockerfiles and docker-compose configs |
| `code-reviewer` | Review code for security, OWASP, .NET best practices (read-only) |

### Task-Driven Workflow

1. User says "What's next?" or "Complete Task X"
2. `task-manager` reads `Docs/Tasks.md` and identifies the task + recommended agent
3. Delegate to the recommended agent to do the work
4. `task-manager` checks off the task in `Docs/Tasks.md`
5. `code-reviewer` is used on-demand between tasks, at phase end, or before commits

### Agent Rules

- Always use the agent recommended by `task-manager` for a given task
- Use `code-reviewer` after completing implementation tasks, not during
- Use `test-runner` or `playwright-tester` after writing test code to verify it passes
- Docker files are *written* by the builder agents (`dotnet-service-builder` / `frontend-builder`) or the main conversation (compose files); `docker-validator` is read-only and only builds/validates them
- Agents cannot talk to each other — the main conversation coordinates

## Git Rules

- NEVER commit, amend, or push without explicit user permission
- NEVER use `git add .` or `git add -A` — always stage specific files by name
- NEVER force push or use destructive git commands (reset --hard, checkout --, clean -f)
- NEVER skip hooks (--no-verify) or bypass signing
- Always ask before committing, even if the user says "complete this task" — completing a task does not imply permission to commit

## Code Conventions

- Backend: follow existing patterns in each service (check before creating new files)
- **Clean Architecture dependency rule:** `API → Application → Domain`, `Infrastructure → Application → Domain`. Never reference API or Infrastructure from Domain or Application.
- **Domain has zero external NuGet dependencies** — only pure C# types (entities, enums, interfaces)
- Frontend: Server Components by default, `"use client"` only when needed
- DTOs at API boundaries — never expose entities directly
- MassTransit consumers must be idempotent
- Use structured logging with `ILogger`
- Secrets: dev-only credentials are committed by design (`appsettings.Development.json`, `docker-compose.yml` environment blocks, `k8s/dev-secrets.yaml`); production/CI credentials are NEVER committed (GitHub repository secrets, Kubernetes Secrets applied directly to the cluster). External provider credentials (Google OAuth, production SMTP, production Turnstile keys) are NEVER committed in any environment — environment variables only (dev Turnstile uses Cloudflare's committable always-pass test keys). `appsettings.json` holds only non-sensitive defaults. See `Docs/Requirements.md` §6
