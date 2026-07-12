import { Button } from "flowbite-react";
import Link from "next/link";
import { auth } from "@/auth";
import { signInWithIdentityServer, signOutFederated } from "@/lib/auth-actions";

// Temporary verification harness for Phase 7 Task 2 (theme tokens + Flowbite
// mapping) and Task 3 (next-auth + IdentityServer). Moved off "/" when Task 4
// turned the home page into the real auction listing -- kept reachable here
// until real nav/login UI exists in later Phase 7 tasks.
export default async function SessionPage() {
  const session = await auth();

  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 py-10 space-y-8">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-bold text-primary-700">ApexAutoBid</h1>
        <Link href="/" className="text-sm font-medium text-primary-700 hover:underline">
          &larr; Back to auctions
        </Link>
      </div>

      <Button color="primary">Place Bid</Button>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-primary-600 text-white rounded-lg p-4 text-sm font-semibold">
          primary-600
        </div>
        <div className="bg-accent-mint text-slate-900 rounded-lg p-4 text-sm font-semibold">
          accent-mint
        </div>
        <div className="bg-accent-leaf text-slate-900 rounded-lg p-4 text-sm font-semibold">
          accent-leaf
        </div>
      </div>

      <div className="rounded-lg border border-slate-200 bg-white p-4 space-y-3 max-w-md">
        <h2 className="text-xl font-semibold text-slate-900">
          Auth.js + IdentityServer
        </h2>

        {session?.user ? (
          <>
            <p className="text-sm text-slate-600">
              Signed in as{" "}
              <span className="font-semibold">
                {session.user.username ?? session.user.name ?? session.user.email}
              </span>
              {session.user.role && ` (${session.user.role})`}
            </p>
            {session.error === "RefreshTokenError" && (
              <p className="text-sm text-red-600">
                Session refresh failed — please sign in again.
              </p>
            )}
            <form action={signOutFederated}>
              <Button type="submit" color="primary">
                Sign out
              </Button>
            </form>
          </>
        ) : (
          <form action={signInWithIdentityServer}>
            <Button type="submit" color="primary">
              Sign in
            </Button>
          </form>
        )}
      </div>
    </div>
  );
}
