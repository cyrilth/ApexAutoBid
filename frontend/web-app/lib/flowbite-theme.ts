import { createTheme } from "flowbite-react";

/**
 * Custom Flowbite React theme overrides for the Teal Breeze brand palette
 * (Docs/DesignGuide.md §2). Applied app-wide via <ThemeProvider> in
 * app/layout.tsx.
 *
 * Most Flowbite components (TextInput, Select, Textarea, ...) already
 * reference the Tailwind `primary-*` color scale in their default theme,
 * so overriding those CSS variables in app/globals.css is enough to bring
 * them in-brand. Button is the exception: its default color keys are named
 * literal colors ("blue", "gray", ...) rather than "primary", so we add an
 * explicit `primary` key here to support `<Button color="primary">`.
 *
 * Badge similarly keys its `color` map by literal names -- none of which
 * match the status vocabulary in Docs/DesignGuide.md §2 (Live/Ending
 * soon/Finished/Sold badges), so we add those as explicit keys too.
 */
export const customFlowbiteTheme = createTheme({
  button: {
    color: {
      primary:
        "bg-primary-600 text-white hover:bg-primary-700 focus:ring-primary-400",
    },
    outlineColor: {
      primary:
        "border border-primary-600 text-primary-700 hover:bg-primary-50 focus:ring-primary-400",
    },
  },
  badge: {
    root: {
      color: {
        // Auction Live.
        primary: "bg-primary-100 text-primary-800",
        // Auction Ending soon (Live with < 6h left) / warnings generally.
        amber: "bg-amber-100 text-amber-800",
        // Auction Finished / ReserveNotMet (not sold).
        slate: "bg-slate-100 text-slate-800",
        // Auction Sold -- accent-leaf background + emerald-800 text, distinct
        // from Flowbite's default "green" (which is a plain green-100/800).
        sold: "bg-accent-leaf text-emerald-800",
      },
    },
  },
});
