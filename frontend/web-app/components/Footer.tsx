import packageJson from "@/package.json";
import { getBackendVersion } from "@/lib/backend-version";

/**
 * Slim page footer (Task 16, Docs/Versioning.md §5 -- "Web app footer: shows
 * both versions"). A Server Component (async) so the backend version can be
 * fetched server-side with a real HTTP cache (`getBackendVersion`'s
 * `revalidate: 300`) rather than re-fetched client-side on every page.
 *
 * Rendered once in `app/layout.tsx`, so it's present under every route
 * (including each route's `error.tsx` boundary) -- `getBackendVersion`
 * degrading to `"unavailable"` instead of throwing is what keeps that safe;
 * this component does no additional try/catch of its own on top of that.
 */
export async function Footer() {
  const frontendVersion = packageJson.version;
  const backendVersion = await getBackendVersion();

  return (
    <footer className="border-t border-slate-200 bg-white">
      <div className="mx-auto w-full max-w-7xl px-4 py-4 sm:px-6">
        <p className="text-center text-xs text-slate-400">
          ApexAutoBid &middot; web v{frontendVersion} &middot; api v{backendVersion}
        </p>
      </div>
    </footer>
  );
}
