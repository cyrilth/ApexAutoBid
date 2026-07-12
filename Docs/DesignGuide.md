# ApexAutoBid — UI Design Guide

Visual design conventions for the Next.js frontend (`frontend/web-app`). The `frontend-builder` agent must follow this guide for every UI task so pages built across separate sessions look like one app. Functional requirements live in `Requirements.md` §5 and §10.4; this document only covers how things look.

## 1. Framework

- **Tailwind CSS** for all styling — utility classes only, no custom CSS files beyond `globals.css` (theme tokens, font setup).
- **Flowbite React** for UI primitives — don't hand-roll components Flowbite already provides (see §6).
- **Light mode only.** Dark mode is explicitly out of scope for v1 — do not add `dark:` variants. (Revisit after Phase 11 if desired; Flowbite supports class-based dark mode, so it can be retrofitted.)

## 2. Color Palette

Brand palette: **Teal Breeze** (generated with [colormagic.app](https://colormagic.app/palette/2aa2d3ccc4ae48c4260b9402)) — `#1f898e`, `#4ccdc4`, `#78e2b8`, `#a3f0b4`, `#e4f5c2`.

### Theme tokens (`@theme` in `app/globals.css`)

The project uses **Tailwind CSS v4**, which has no `tailwind.config.ts` — theme tokens are declared as CSS custom properties inside an `@theme { … }` block in `app/globals.css`. Define a `primary` scale anchored on the two teals (600 = `#1f898e` Elm, 400 = `#4ccdc4` Puerto Rico) and `accent` tokens for the palette's greens. Place the block *after* the `@import "flowbite-react/plugin/tailwindcss"` line so these `--color-primary-*` values override Flowbite's default blue scale:

```css
@theme {
  --color-primary-50:  #f1fafa;
  --color-primary-100: #dbf2f1;
  --color-primary-200: #bce5e4;
  --color-primary-300: #8ed7d3;
  --color-primary-400: #4ccdc4; /* Puerto Rico — highlights, focus rings, live indicators */
  --color-primary-500: #2ba8a6;
  --color-primary-600: #1f898e; /* Elm — brand anchor: links, active nav, primary buttons */
  --color-primary-700: #1c6e74; /* button hover, link text on white (AA-safe) */
  --color-primary-800: #1c585e;
  --color-primary-900: #1b4a4f;
  --color-primary-950: #0a2f34;

  --color-accent-mint:  #78e2b8; /* Riptide — success/sold accents */
  --color-accent-leaf:  #a3f0b4; /* Madang — success backgrounds, subtle highlights */
  --color-accent-cream: #e4f5c2; /* Tusk — hero/section background tint */
}
```

These generate the usual utilities (`bg-primary-600`, `text-primary-700`, `ring-primary-400`, `bg-accent-mint`, …).

**Flowbite React primary mapping:** Flowbite's inputs already reference the `primary-*` scale by name, so the `@theme` override brings them in-brand automatically. Flowbite `Button`, however, keys its `color` map by literal names and has no `primary` entry — add one via `createTheme(...)` in `lib/flowbite-theme.ts` and supply it through `<ThemeProvider theme={…}>` in `app/layout.tsx`, so `<Button color="primary">` renders `bg-primary-600` with `hover:bg-primary-700` and `focus:ring-primary-400`.

### Usage rules

- **Neutrals:** Tailwind `slate` (page background `slate-50`, cards `white`, body text `slate-700`, headings `slate-900`, borders `slate-200`).
- **Primary buttons / CTAs** (Place Bid, Create Auction): `primary-600` background, white text, `primary-700` hover. Secondary buttons: white with `slate-300` border.
- **Links and active nav:** `primary-700` on white backgrounds (`primary-600` is ~4.2:1 contrast on white — fine for large/bold text and UI chrome, use 700 for body-size link text).
- **Warm colors aren't in the palette** — borrow Tailwind defaults where semantics demand them: `amber` for warnings/ending-soon, `red` for errors/destructive actions. Don't invent additional hues beyond these.

### Semantic / status colors

Badges use the tint-background + dark-text pattern (e.g., `bg-primary-100 text-primary-800`).

| Meaning | Color |
|---|---|
| Auction **Live** | `primary` teal badge |
| Auction **Ending soon** (also countdown < 6h) | `amber` |
| Auction **Finished / ReserveNotMet** (not sold) | `slate` |
| Auction **Sold** | green — `accent-leaf` background, `emerald-800` text |
| Auction **Cancelled** | `red` |
| Bid **Accepted** | green (as Sold) |
| Bid **AcceptedBelowReserve** | `amber` |
| Bid **TooLow / rejected** | `red` |
| Success / error / warning toasts & alerts | green / `red` / `amber` |

## 3. Typography

- **Font:** [Inter](https://fonts.google.com/specimen/Inter) via `next/font/google` (self-hosted by Next.js, no runtime request) — applied on `<body>`; Tailwind `font-sans` maps to it. No second typeface.
- **Scale:** Tailwind defaults. Page titles `text-3xl font-bold`, section headings `text-xl font-semibold`, card titles `text-lg font-semibold`, body `text-base`, metadata/captions `text-sm text-slate-500`.
- **Numbers:** prices and bids use the `numberWithCommas` helper, `font-semibold`, prefixed `$`. Current high bid on the detail page is the visually loudest number (`text-2xl`+).

## 4. Layout

- **Page shell:** sticky top navbar (Flowbite `Navbar` — logo, search, nav links, auth/user menu) + content + slim footer. Content in a centered `max-w-7xl mx-auto px-4 sm:px-6` container.
- **Auction grid:** `grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6`.
- **Auction detail:** two columns on `lg` (image + specs left, live bid panel + history right), stacked on mobile.
- **Admin area (`/admin`):** Flowbite `Sidebar` layout (Dashboard, Users, Auctions, Banners, Settings) with content to the right; stats cards in a 3-up grid.
- **Forms** (create/edit auction, admin forms): single column, `max-w-2xl`, labels above inputs.
- Mobile-first: every page must be usable at 375px wide.

## 5. Imagery

- **Card thumbnails:** fixed `aspect-[4/3]` box, `object-cover`, via `next/image`. Use `ThumbnailUrl` when present (400px-wide WebP from the ImageSharp pipeline), falling back to `ImageUrl` (`Requirements.md` §3.1).
- **Detail page:** full `ImageUrl` in a `aspect-video` (16:9) container, `object-cover`, rounded corners (`rounded-lg`).
- **Missing image placeholder:** `bg-slate-100` box with a centered slate car icon — never a broken-image glyph. Place one shared `<CarImage>` component in `components/` and use it everywhere an auction image renders.

## 6. Component Conventions (Flowbite React)

| Job | Use |
|---|---|
| Auction listing card | `Card` (custom body: image, make/model/year, status badge, high bid, countdown) |
| Status / bid-result labels | `Badge` |
| Buttons | `Button` (`color="primary"`; `color="failure"` for destructive) |
| Delete / cancel / end-auction confirmations | `Modal` (destructive confirm button red) |
| Toasts (bids, win/sell results, errors) | **react-hot-toast** (not Flowbite Toast) — top-right, success/error variants |
| Platform banners (`GET api/banners`) | Flowbite `Banner` strip above page content |
| Admin tables (users, auctions, banners) | `Table` with `Pagination` |
| Form inputs | `Label` + `TextInput`/`Select`/`Textarea`; react-datepicker styled to match `TextInput` |
| Inline validation errors | `text-sm text-red-600` below the field (react-hook-form errors) |
| Loading | `Spinner` for in-component loads; skeleton cards (`animate-pulse`) for the auction grid |
| Empty states | centered icon + one-line message + relevant CTA (e.g., "No auctions match — clear filters") |

Build on Flowbite first; only create custom components for domain widgets (auction card, bid panel, countdown, stats card) and put shared ones in `components/`.

## 7. Page Inventory

| Page | Layout notes |
|---|---|
| `/` Listings (home) | Filter/sort/search toolbar above the auction grid; active banners on top; skeleton cards while loading |
| `/auctions/{id}` Detail | Two-column (§4); countdown prominent near the bid panel; bid history as a vertical list, newest first, live-prepended via SignalR; share buttons row under the specs; post-sale contact block (sold auctions, seller/winner only) in an `accent-cream` callout |
| `/auctions/create`, `/auctions/{id}/edit` | Single-column form (§4); image upload with preview + optional "Generate thumbnail" step |
| `/admin/*` | Sidebar layout (§4); dashboard stats cards show one number large with a label beneath |
| IdentityServer pages (login/register — separate Razor app) | Not Tailwind; apply `#1f898e` to its buttons/links via its own CSS for brand continuity, keep the rest default |

## 8. Motion & Feedback

- Use transitions sparingly: `transition-colors` on hover states, `hover:shadow-md` on cards. No entrance animations.
- Real-time updates (new bid, high-bid change) get a brief background-tint pulse (`accent-leaf` fading out) so live changes are noticeable without being noisy.
- Every mutating action gives feedback: button disabled+spinner while pending, then success/error toast.

## 9. Accessibility

- Keep visible focus rings (`focus:ring-primary-400`); never `outline-none` without a replacement.
- Text contrast ≥ 4.5:1 (body) / 3:1 (large text, UI chrome) — see §2 link rule.
- Status is never conveyed by color alone — badges always carry text labels.
- All auction images get `alt` text: `"{Year} {Make} {Model}"`.
- Icon-only buttons get `aria-label`; modals trap focus (Flowbite handles this).
