"use client";

import { useEffect } from "react";

/**
 * Root error boundary (Task 18, Docs/Requirements.md §13.2). Only triggers
 * when the ROOT layout itself (`app/layout.tsx`) throws while rendering --
 * every other error in the tree is caught by the nearer `app/error.tsx`
 * first (Next's `error.js` docs: "It does not wrap the layout.js ... above
 * it in the same segment. To handle errors in the root layout, use
 * global-error.js").
 *
 * Per those same docs, this file REPLACES the root layout when active, so
 * it must render its own `<html>`/`<body>` -- none of `app/layout.tsx`'s
 * providers (ThemeProvider, NotificationProvider, the Inter font, the
 * Tailwind-compiled `globals.css`) are guaranteed to be available here,
 * since the very thing that would normally wire them up is what may have
 * just failed. That's why this page uses plain inline styles (a small,
 * hand-picked subset of the Teal Breeze palette, Docs/DesignGuide.md §2)
 * instead of Tailwind utility classes or Flowbite components -- it stays
 * renderable even if the app's own CSS/JS pipeline is the thing that broke.
 */
export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    // Dev observability only -- never surfaced to the user (§13.2).
    console.error(error);
  }, [error]);

  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          minHeight: "100vh",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontFamily:
            "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif",
          backgroundColor: "#f8fafc", // Tailwind slate-50
          color: "#334155", // slate-700
        }}
      >
        <div style={{ maxWidth: "28rem", padding: "2rem", textAlign: "center" }}>
          <h1 style={{ margin: "0 0 0.5rem", fontSize: "1.25rem", fontWeight: 600, color: "#0f172a" }}>
            Something went wrong
          </h1>
          <p style={{ margin: "0 0 1.5rem", fontSize: "0.875rem", color: "#64748b" }}>
            ApexAutoBid hit an unexpected error. Please try again.
          </p>
          <button
            type="button"
            onClick={() => reset()}
            style={{
              backgroundColor: "#1f898e", // primary-600 (Docs/DesignGuide.md §2 -- Elm)
              color: "#ffffff",
              border: "none",
              borderRadius: "0.5rem",
              padding: "0.5rem 1.25rem",
              fontSize: "0.875rem",
              fontWeight: 500,
              cursor: "pointer",
            }}
          >
            Try again
          </button>
        </div>
      </body>
    </html>
  );
}
