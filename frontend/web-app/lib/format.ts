/**
 * Comma-separated number formatting (e.g. 12000 -> "12,000"). Minimal local
 * helper introduced for Phase 7 Task 4 (auction listing) -- Task 11 formally
 * adds the app-wide `numberWithCommas` currency-formatting helper referenced
 * across the project conventions; this *is* that function, kept here so Task
 * 11 only needs to widen its usage, not duplicate the implementation.
 */
export function numberWithCommas(value: number): string {
  return value.toLocaleString("en-US");
}

/** `$` + `numberWithCommas` -- prices/bids are always shown this way (Docs/DesignGuide.md §3). */
export function formatCurrency(value: number): string {
  return `$${numberWithCommas(value)}`;
}
