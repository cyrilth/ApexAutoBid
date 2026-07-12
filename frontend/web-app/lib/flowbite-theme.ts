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
});
