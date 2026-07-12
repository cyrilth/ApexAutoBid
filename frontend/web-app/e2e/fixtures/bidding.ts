import type { Page } from "@playwright/test";
import { throttleMutation } from "./mutation-throttle";
import { expect } from "./test";

/**
 * Fills `BidPanel`'s "Your bid" field and asserts it actually landed, rather than a bare
 * `.fill(String(amount))`. `BidPanel`'s amount field is `react-hook-form`'s uncontrolled
 * `register("amount", { ... })`, seeded via `useForm`'s `defaultValues: { amount: minBid }` --
 * on a fresh page load that default (e.g. `"1"` for a brand-new, bid-free auction) lands in the
 * DOM asynchronously as React hydrates. Filling immediately after `page.goto`/navigation raced
 * that hydration in local spiking (confirmed non-deterministically: a bare `.fill("250")`
 * sometimes left the pre-hydration default's digit(s) in place, submitting `"1250"` instead of
 * `"250"`) -- waiting for the field to hold ANY value first (i.e. hydration has run) before
 * typing over it makes this deterministic.
 */
export async function fillBidAmount(page: Page, amount: number): Promise<void> {
  const input = page.getByLabel("Your bid");
  await expect(input).not.toHaveValue("");
  await input.fill(String(amount));
  await expect(input).toHaveValue(String(amount));
}

/**
 * Fills the bid amount (`fillBidAmount`) and submits -- pacing the submit itself against
 * GatewayService's "strict" rate-limit policy (`POST /api/bids` is one of the routes it covers
 * -- see `./mutation-throttle.ts`'s remarks) rather than leaving each spec to remember to.
 */
export async function submitBid(page: Page, amount: number): Promise<void> {
  await fillBidAmount(page, amount);
  await throttleMutation();
  await page.getByRole("button", { name: "Place bid" }).click();
}

/**
 * Matches `formatCurrency(amount)` (e.g. `"$250"`) as a whole amount, not merely a substring of
 * a bigger one (e.g. `"$2500"`) -- `BidHistory`'s `<li>` renders the amount immediately
 * adjacent to its `BidStatusBadge` text with no separator (`"...$250Accepted"`), so a trailing
 * `\b` word-boundary assertion does NOT work here: both `"0"` and `"A"` are word characters, so
 * there's no boundary between them for `\b` to match on. A negative lookahead for a following
 * digit sidesteps that (confirmed against this exact live-rendered shape).
 */
export function bidAmountPattern(amount: number): RegExp {
  return new RegExp(`\\$${amount.toLocaleString("en-US")}(?!\\d)`);
}
