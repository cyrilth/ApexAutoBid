import toast from "react-hot-toast";

/**
 * Single, consistent entry point for every toast the app shows (Task 10,
 * Docs/DesignGuide.md §6 -- "Toasts (bids, win/sell results, errors) --
 * react-hot-toast (not Flowbite Toast) -- top-right, success/error
 * variants"). Later tasks (the SignalR notification handlers in the Task
 * 8.2/8.3 follow-up run, Task 19's ProblemDetails toasts) should import
 * from here rather than `react-hot-toast` directly, so every toast in the
 * app shares one visual language instead of each call site inventing its
 * own colors/icons.
 *
 * `<Toaster>` itself is mounted once in `app/layout.tsx`, with its
 * `toastOptions` carrying the shared base style (rounded, slate border) --
 * these helpers only add the per-variant color accents on top of that base.
 */

const successStyle = {
  background: "var(--color-accent-leaf)",
  color: "#065f46", // emerald-800 -- matches BidStatusBadge's "Accepted"/Sold treatment (Docs/DesignGuide.md §2).
  border: "1px solid var(--color-accent-mint)",
};

const errorStyle = {
  background: "#fef2f2", // red-50
  color: "#b91c1c", // red-700
  border: "1px solid #fecaca", // red-200
};

const warningStyle = {
  background: "#fffbeb", // amber-50
  color: "#92400e", // amber-800
  border: "1px solid #fde68a", // amber-200
};

/** Green success toast -- e.g. a bid Accepted, an auction won/sold. */
export function toastSuccess(message: string): void {
  toast.success(message, {
    style: successStyle,
    iconTheme: { primary: "#065f46", secondary: "var(--color-accent-leaf)" },
  });
}

/** Red error toast -- e.g. a bid TooLow/rejected, a ProblemDetails failure. */
export function toastError(message: string): void {
  toast.error(message, {
    style: errorStyle,
    iconTheme: { primary: "#b91c1c", secondary: "#fef2f2" },
  });
}

/**
 * Amber "recorded, but ..." toast -- react-hot-toast has no built-in
 * warning variant, so this is a plain `toast()` call styled to match the
 * amber treatment used everywhere else for this severity (ending-soon
 * badges, AcceptedBelowReserve, email-verification notices).
 */
export function toastWarning(message: string): void {
  toast(message, { icon: "⚠️", style: warningStyle });
}

/**
 * Formats an `ActionResult` failure (the `{ title, detail? }` envelope
 * shared by `lib/auction-actions.ts` and `lib/bid-actions.ts`) as a single
 * error toast -- "title: detail" when a detail is present, just the title
 * otherwise.
 */
export function toastActionError(error: { title: string; detail?: string }): void {
  toastError(error.detail ? `${error.title}: ${error.detail}` : error.title);
}

/**
 * Neutral toast, no color accent beyond `<Toaster>`'s shared base style --
 * the least visually loud of the four variants (Phase 7 Task 8.3). Used for
 * platform-wide SignalR broadcasts that aren't the result of anything the
 * viewer did (e.g. "AuctionCreated"), where a green/red/amber treatment
 * would misleadingly imply the toast is about the viewer's own action.
 * Docs/DesignGuide.md §8's "noticeable without being noisy" applies here
 * just as much as it does to the in-page background pulse.
 */
export function toastInfo(message: string): void {
  toast(message);
}
