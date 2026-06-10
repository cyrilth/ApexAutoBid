# ApexAutoBid - Sub-Agent Guide

## Overview

This project uses 7 custom Claude Code sub-agents defined in `.claude/agents/`. Sub-agents are specialized AI assistants that run in their own context window, keeping verbose output (test results, build logs) out of your main conversation.

---

## Available Agents

| Agent | Model | Access | Purpose |
|-------|-------|--------|---------|
| `task-manager` | Sonnet | Read/Edit | Look up tasks, map to agents, track progress |
| `dotnet-service-builder` | Sonnet | Read/Write | Scaffold .NET microservices |
| `frontend-builder` | Sonnet | Read/Write | Build Next.js components and pages |
| `test-runner` | Haiku | Read-only | Run `dotnet test`, report results |
| `playwright-tester` | Haiku | Read-only | Run Playwright e2e tests |
| `docker-validator` | Haiku | Read-only | Validate Dockerfiles and Compose |
| `code-reviewer` | Sonnet | Read-only | Review code for security and quality |

---

## How to Use Agents

### Automatic Delegation

Claude reads each agent's `description` field and automatically delegates matching tasks. Just describe what you need:

```
Scaffold the Search Service with MongoDB and MassTransit
```

Claude will recognize this matches `dotnet-service-builder` and delegate automatically.

### Explicit Delegation

You can request a specific agent by name:

```
Use the code-reviewer agent to review my recent changes
Use the test-runner agent to run the Auction Service unit tests
Use the frontend-builder agent to create the auction listing page
```

### Parallel Delegation

Ask Claude to run multiple agents at once for independent tasks:

```
In parallel:
- Use dotnet-service-builder to scaffold the Bidding Service
- Use frontend-builder to create the bid placement component
```

Both agents work simultaneously and return results to your main conversation.

---

## Session Behavior

### Same Session (Sub-Agents)

All 7 agents are **sub-agents** — they run within your current Claude Code session. This means:

- They share the same terminal and filesystem
- They return results back to the main conversation
- They cannot spawn other sub-agents (no nesting)
- They are short-lived — created for a task, return results, then done

### Resuming a Sub-Agent

If a sub-agent's work was incomplete, you can resume it with full context preserved:

```
Continue the dotnet-service-builder agent's work on the Bidding Service
```

Claude tracks agent IDs internally and resumes from where it left off.

### Separate Sessions (Agent Teams)

Sub-agents **cannot** run across separate Claude Code sessions. If you need multiple agents working independently in parallel sessions (e.g., one terminal building backend, another building frontend), that requires [Agent Teams](https://code.claude.com/docs/en/agent-teams) — a different feature where each agent runs in its own Claude Code session.

For our project, sub-agents within a single session are sufficient. Use separate terminal windows with `claude` if you want true parallel sessions, but those sessions won't share context.

---

## Agent Communication

### Sub-Agents Do NOT Communicate Directly

Sub-agents cannot talk to each other. Each one:

- Gets its own isolated context window
- Receives only the task prompt from the main conversation
- Returns results only to the main conversation
- Has no awareness of other sub-agents running

### The Main Conversation is the Coordinator

Claude in your main conversation acts as the **orchestrator**:

```
You (main conversation)
  ├── delegates to → dotnet-service-builder (returns result)
  ├── delegates to → code-reviewer (returns result)
  └── synthesizes results and decides next steps
```

**Example: Build → Review → Test workflow**

```
1. You: "Scaffold the Bidding Service"
   → Claude delegates to dotnet-service-builder
   → Agent returns: "Created project with controllers, entities, DTOs..."

2. You: "Now review the code"
   → Claude delegates to code-reviewer (passes context about what was built)
   → Agent returns: "Found 2 warnings: missing input validation on..."

3. You: "Fix those issues and run the tests"
   → Claude fixes the code directly (main conversation)
   → Claude delegates to test-runner
   → Agent returns: "12 tests passed, 0 failed"
```

### Chaining Agents

You can ask Claude to chain agents sequentially:

```
Use dotnet-service-builder to scaffold the Search Service,
then use code-reviewer to review it,
then use test-runner to run the tests
```

Claude runs each agent in order, passing relevant context between them.

### Sharing Context Between Agents

Since agents don't talk directly, context flows through two mechanisms:

1. **The filesystem** — One agent writes files, the next agent reads them. The `dotnet-service-builder` creates a service, then `code-reviewer` reads those files to review them.

2. **The main conversation** — Claude summarizes one agent's output and includes relevant details when invoking the next agent.

---

## Task-Driven Development

The `task-manager` agent connects `Docs/Tasks.md` to the other agents. It reads the task file, figures out which agent should handle the work, and checks off tasks when done.

### Completing a Task by Number

```
Complete Task 2.1
```

What happens:
1. Claude delegates to `task-manager`
2. `task-manager` reads `Docs/Tasks.md`, finds Phase 1 → Task 2 → Sub-task 2.1
3. Returns: task description, recommended agent (`dotnet-service-builder`), specific prompt, and any pre-requisites
4. Claude delegates to `dotnet-service-builder` with the specific prompt
5. Agent does the work and returns results
6. Claude delegates back to `task-manager` to check off the task in `Tasks.md`

### Checking Progress

```
What's the status of Phase 1?
What's next?
How much is done overall?
```

`task-manager` reads `Tasks.md` and reports completed vs remaining tasks.

### Task References

You can refer to tasks in different ways:

| You say | Meaning |
|---------|---------|
| `Complete Task 2.1` | Phase 1, Task 2, Sub-task 1 |
| `Complete Phase 3, Task 5` | Phase 3, Task 5 |
| `What's next?` | First unchecked task in the current phase |
| `What's left in Phase 2?` | All unchecked tasks in Phase 2 |
| `Mark Task 4 done` | Check off Task 4 without doing work |

### Agent Examples in Task-Driven Flow

Each agent plays a specific role in the task-driven workflow. Below are concrete examples showing when and how each agent gets invoked.

---

#### `task-manager` — Lookup, Coordinate, Track

The orchestrator. Always the first and last agent in a task flow.

```
You: What's next?
Claude: → task-manager reads Tasks.md
         → "Phase 1, Task 3: Create the Auction Service project with NuGet packages"
         → Recommended agent: dotnet-service-builder
         → Pre-requisites: Task 1 and Task 2 must be complete

You: What's left in Phase 2?
Claude: → task-manager reads Tasks.md
         → "7/11 remaining: Tasks 5, 6, 7, 8, 9, 10, 11"
         → Next up: Task 5 (GET api/search endpoint)

You: Mark Phase 1, Task 9 done
Claude: → task-manager edits Tasks.md
         → Changes "- [ ] 9." to "- [x] 9."
         → "Task 9 (Add seed data) marked complete. Next: Task 10"
```

---

#### `dotnet-service-builder` — Scaffold Backend Code

Handles all .NET service creation: projects, entities, DTOs, controllers, consumers, EF Core, MassTransit, tests.

```
You: Complete Phase 1, Task 4
Claude: → task-manager looks up Task 4
         → "Define entities: Auction, Item, Status enum"
         → Recommended agent: dotnet-service-builder

       → dotnet-service-builder
         → Creates backend/AuctionService/AuctionService.Domain/Entities/Auction.cs
         → Creates backend/AuctionService/AuctionService.Domain/Entities/Item.cs
         → Creates backend/AuctionService/AuctionService.Domain/Enums/Status.cs
         → Returns: "Created 2 entities and 1 enum in the Domain layer"

       → task-manager checks off 4, 4.1, 4.2, 4.3 in Tasks.md

You: Complete Phase 2, Task 4
Claude: → task-manager looks up Phase 2, Task 4
         → "Implement event consumers: AuctionCreated, AuctionUpdated, ..."
         → Recommended agent: dotnet-service-builder

       → dotnet-service-builder
         → Creates 5 consumer classes in backend/SearchService/SearchService.Application/Consumers/
         → Each implements IConsumer<T> with MassTransit
         → Returns: "Created 5 consumers for AuctionCreated, AuctionUpdated,
           AuctionDeleted, BidPlaced, AuctionFinished"

       → task-manager checks off 4, 4.1, 4.2, 4.3, 4.4, 4.5

You: Complete Phase 5, Task 6
Claude: → task-manager looks up Phase 5, Task 6
         → "Implement gRPC client to call Auction Service"
         → Recommended agent: dotnet-service-builder

       → dotnet-service-builder
         → Creates protos/auctions.proto
         → Creates BiddingService.Infrastructure/Grpc/GrpcAuctionClient.cs
         → Registers gRPC client in BiddingService.API/Program.cs
         → Returns: "gRPC client configured with Polly retry"
```

---

#### `frontend-builder` — Build Next.js UI

Handles all frontend work: components, pages, hooks, state, auth, styling.

```
You: Complete Phase 7, Task 4
Claude: → task-manager looks up Phase 7, Task 4
         → "Implement auction listing page"
         → Sub-tasks: fetch data, pagination/filtering, search
         → Recommended agent: frontend-builder

       → frontend-builder
         → Creates app/auctions/page.tsx (Server Component, fetches from Gateway)
         → Creates components/AuctionCard.tsx
         → Creates components/Filters.tsx (live, endingSoon, finished)
         → Creates components/SearchBar.tsx
         → Creates hooks/useAuctionStore.ts (Zustand)
         → Returns: "Auction listing page with pagination, filtering,
           sorting, and search. Uses Flowbite React cards with Tailwind."

       → task-manager checks off 4, 4.1, 4.2, 4.3

You: Complete Phase 7, Task 8
Claude: → task-manager looks up Phase 7, Task 8
         → "Implement bid placement UI"
         → Sub-tasks: bid input/submit, real-time SignalR updates
         → Recommended agent: frontend-builder

       → frontend-builder
         → Creates components/BidForm.tsx (react-hook-form, "use client")
         → Creates hooks/useSignalR.ts (connects to /notifications hub)
         → Integrates real-time bid updates into auction detail page
         → Returns: "Bid form with validation, SignalR connection for
           live bid updates. Toast notification on bid success/failure."

       → task-manager checks off 8, 8.1, 8.2
```

---

#### `test-runner` — Run .NET Tests

Runs `dotnet test` and returns a concise pass/fail summary. Used after writing test code or for end-to-end verification tasks.

```
You: Complete Phase 1, Task 14
Claude: → task-manager looks up Task 14
         → "Write unit tests (AuctionService.UnitTests)"
         → Recommended agent: dotnet-service-builder (to write the tests)

       → dotnet-service-builder
         → Creates tests/AuctionService.UnitTests/ project
         → Writes 7 test methods
         → Returns: "Created 7 unit tests for Create/Update/Delete"

       → test-runner (automatically invoked to verify)
         → Runs: dotnet test tests/AuctionService.UnitTests/
         → Returns: "7 passed, 0 failed, 0 skipped"

       → task-manager checks off 14, 14.1 through 14.7

You: Run all Phase 1 tests
Claude: → test-runner
         → Runs: dotnet test (scoped to AuctionService test projects)
         → Returns: "Unit: 7 passed. Integration: 3 passed. Total: 10/10"

You: Complete Phase 2, Task 11
Claude: → task-manager looks up Phase 2, Task 11
         → "Verify end-to-end: create auction → appears in Search Service"
         → Recommended agent: test-runner

       → test-runner
         → Runs integration test suite
         → Returns: "E2E verified: auction created in AuctionService,
           event consumed, item appears in SearchService MongoDB"
```

---

#### `playwright-tester` — Run Frontend E2E Tests

Runs Playwright tests and returns pass/fail with failure details. Used for Phase 7 e2e tests and full-stack verification. **Read-only** — it cannot write files, so `frontend-builder` writes the test files and this agent runs them.

```
You: Complete Phase 7, Task 14
Claude: → task-manager looks up Phase 7, Task 14
         → "Set up Playwright test project"
         → Recommended agent: frontend-builder

       → frontend-builder
         → Runs: npm init playwright@latest (in frontend/web-app/)
         → Creates playwright.config.ts
         → Returns: "Playwright configured with chromium,
           base URL pointing to Gateway"

       → playwright-tester
         → Installs browsers: npx playwright install
         → Runs: npx playwright test --list
         → Returns: "Setup verified, test runner working"

       → task-manager checks off Task 14

You: Complete Phase 7, Task 15.6
Claude: → task-manager looks up Phase 7, Task 15.6
         → "Auth — login flow via IdentityServer"
         → Recommended agent: frontend-builder (write), playwright-tester (run)

       → frontend-builder
         → Writes tests/auth.spec.ts with login flow test

       → playwright-tester
         → Runs: npx playwright test tests/auth.spec.ts
         → Returns: "1 passed: login redirects to IdentityServer,
           enters credentials, redirects back with session"

       → task-manager checks off 15.6

You: Run all Playwright tests
Claude: → playwright-tester
         → Runs: npx playwright test (from frontend/web-app/)
         → Returns: "15 passed, 0 failed, 0 skipped (32s)"

You: Complete Phase 8, Task 7
Claude: → task-manager looks up Phase 8, Task 7
         → "Test all user flows end-to-end in containerized environment"
         → Recommended agent: playwright-tester

       → playwright-tester
         → Runs full suite against Docker Compose stack
         → Returns: "15 passed. Full user flow verified: browse → login →
           create auction → place bid → real-time update received"
```

---

#### `docker-validator` — Validate Docker Setup

Validates Dockerfiles and docker-compose configuration. **Read-only** — it cannot write files. Builder agents (`dotnet-service-builder`, `frontend-builder`) create Dockerfiles, the main conversation creates compose files, and this agent builds and verifies them.

```
You: Complete Phase 1, Task 13
Claude: → task-manager looks up Task 13
         → "Dockerize the Auction Service"
         → Recommended agent: dotnet-service-builder (create), docker-validator (verify)

       → dotnet-service-builder
         → Creates backend/AuctionService/AuctionService.API/Dockerfile
           (multi-stage build, multi-project restore)

       → docker-validator
         → Runs: docker build -t auction-service
           -f backend/AuctionService/AuctionService.API/Dockerfile .
         → Returns: "Build successful. Image size: 215MB.
           Non-root user configured."

       → task-manager checks off Task 13

You: Complete Phase 8, Task 1
Claude: → task-manager looks up Phase 8, Task 1
         → "Create docker/docker-compose.yml"
         → Recommended agent: main conversation (create), docker-validator (validate)

       → (main conversation creates docker/docker-compose.yml with all services)

       → docker-validator
         → Runs: docker compose -f docker/docker-compose.yml config
         → Returns: "Compose file valid. 11 services defined:
           postgres, mongodb, rabbitmq, auction-svc, search-svc,
           bidding-svc, identity-svc, gateway-svc, notification-svc,
           web-app, nginx"

       → task-manager checks off 1, 1.1, 1.2, 1.3

You: Complete Phase 8, Task 5
Claude: → task-manager looks up Phase 8, Task 5
         → "Verify full stack runs with docker compose up"
         → Recommended agent: docker-validator

       → docker-validator
         → Runs: docker compose up -d
         → Checks: docker compose ps (all containers healthy)
         → Checks: curl gateway health endpoint
         → Returns: "All 10 services running. Health checks passing.
           Gateway responding on port 6001."

       → task-manager checks off Task 5
```

---

#### `code-reviewer` — Review Code Quality

Read-only agent for security and quality checks. **Not tied to any task in Tasks.md** — use it on-demand between tasks, at the end of a phase, or before committing.

```
You: Review the code for Phase 1
Claude: → code-reviewer
         → Runs: git diff main..HEAD -- backend/AuctionService/
         → Reads all changed files
         → Returns:
           "Critical (0): None
            Warning (2):
            - AuctionsController.cs:45 — PUT endpoint missing
              ownership check before update
            - Program.cs:23 — Connection string should use
              environment variable, not hardcoded value
            Suggestion (1):
            - DbInitializer.cs:12 — Consider using async seed method"

You: (fixes the warnings)

You: Review again
Claude: → code-reviewer
         → Returns: "Critical (0), Warning (0), Suggestion (1).
           All previous warnings resolved."

You: Review the frontend code before I commit
Claude: → code-reviewer
         → Reads frontend/web-app/ changes
         → Returns:
           "Critical (1):
            - lib/fetchWrapper.ts:8 — API base URL hardcoded,
              should use NEXT_PUBLIC_API_URL env var
            Warning (1):
            - components/BidForm.tsx:32 — bid amount not validated
              as positive number before submission"

You: Complete Phase 5 then review it
Claude: → (completes all Phase 5 tasks via dotnet-service-builder)
       → code-reviewer
         → Reviews all Bidding Service code
         → Returns: "2 warnings found in bid validation logic..."
```

**When to use `code-reviewer` in your workflow:**
- After completing implementation tasks, before moving to the next phase
- Before running `test-runner` — catch obvious issues before wasting test cycles
- Before committing — final quality gate
- After fixing bugs — verify the fix doesn't introduce new issues

---

### Full Example Session

```
You: What's next?
Claude: → task-manager → "Phase 1, Task 1: Create solution structure"

You: Complete it
Claude: → dotnet-service-builder → creates solution structure
Claude: → task-manager → checks off Task 1 in Tasks.md

You: Next
Claude: → task-manager → "Phase 1, Task 2: Create Contracts project"
         → "Sub-tasks: 2.1 AuctionCreated, 2.2 AuctionUpdated, ..."

You: Complete Task 2
Claude: → dotnet-service-builder → creates all contracts
Claude: → task-manager → checks off Task 2 and all sub-tasks

You: What's the status of Phase 1?
Claude: → task-manager → "2/15 complete (13%)"

You: Review what we have so far
Claude: → code-reviewer → "0 critical, 0 warnings. Code looks clean."
```

---

## Development Workflows

### Workflow 1: Build a New Backend Service

```
1. "Use dotnet-service-builder to scaffold the Search Service"
2. "Use code-reviewer to review the scaffolded code"
3. "Use test-runner to run the Search Service tests"
4. "Use docker-validator to check the Dockerfile"
```

### Workflow 2: Build a Frontend Feature

```
1. "Use frontend-builder to create the auction detail page"
2. "Use playwright-tester to run the e2e tests for auction detail"
```

### Workflow 3: Full Stack Feature

```
1. "Use dotnet-service-builder to add the PlaceBid endpoint to Bidding Service"
2. "Use frontend-builder to create the bid placement UI"
3. "Run both test-runner and playwright-tester in parallel"
4. "Use code-reviewer to review all changes"
```

### Workflow 4: Pre-Commit Check

```
Use code-reviewer to review my changes, then run test-runner
```

### Workflow 5: Docker Validation After Changes

```
Use docker-validator to verify the full stack builds and runs
```

---

## Tips

- **Use Haiku agents for quick checks** — `test-runner`, `playwright-tester`, and `docker-validator` use Haiku (faster, cheaper). They're meant for running commands and parsing output, not deep reasoning.

- **Use Sonnet agents for creation and analysis** — `dotnet-service-builder`, `frontend-builder`, and `code-reviewer` use Sonnet for better code generation and review quality.

- **Prefer agents over main conversation for noisy tasks** — Running `dotnet test` or `docker compose build` produces hundreds of lines of output. Delegating to an agent keeps your main context clean.

- **Be specific when delegating** — Instead of "run the tests", say "use test-runner to run the Auction Service unit tests". Specificity helps Claude pick the right agent and scope the work.

- **Check agent output** — Agent results are summaries. If something looks wrong, ask Claude to show the full details or resume the agent for more investigation.

---

## Managing Agents

### View all agents
```
/agents
```

### Edit an agent
```
/agents → select agent → edit
```

### Agent files location
```
.claude/agents/
├── task-manager.md
├── dotnet-service-builder.md
├── frontend-builder.md
├── test-runner.md
├── playwright-tester.md
├── docker-validator.md
└── code-reviewer.md
```
