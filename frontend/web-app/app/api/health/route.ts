import { NextResponse } from "next/server";

/**
 * Liveness probe (Task 20, Docs/Requirements.md §13.4). Deliberately
 * dependency-free -- no auth, no Gateway/backend calls, no session read --
 * this only answers "is the Next.js server process up and serving
 * requests", the same scope as the backend services' own `/health/live`
 * (`backend/GatewayService/Program.cs`'s remarks: "never runs a check ...
 * reflects only 'is the process up'"). A real dependency check (Gateway
 * reachability, etc.) would belong on a separate `/api/health/ready`-style
 * route if one is ever needed -- not here.
 *
 * `Cache-Control: no-store` -- an orchestrator's liveness probe must hit
 * this handler on every poll, never a cached response from a shared/browser
 * cache.
 */
export async function GET() {
  return NextResponse.json({ status: "ok" }, { status: 200, headers: { "Cache-Control": "no-store" } });
}
