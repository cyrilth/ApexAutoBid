/**
 * Conversions between .NET's `TimeSpan` wire format (System.Text.Json's built-in "c"/constant
 * format, `.NET 8+ -- "[-][d.]hh:mm:ss[.fffffff]"`, e.g. `"01:00:00"` or `"90.00:00:00"`) and
 * plain milliseconds, so the frontend can do date arithmetic (constraining the create-auction
 * form's `AuctionEnd` picker, Docs/Tasks.md Phase 11 Task 3.8/8.7) without a TimeSpan library.
 *
 * Used by `types/admin.ts`'s `DurationLimits`/`DurationSettings` (both carry `minDuration`/
 * `maxDuration` as these strings verbatim).
 */

const TIMESPAN_PATTERN = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/;

/** Parses a `TimeSpan` wire string into milliseconds. Returns `null` if it doesn't match the expected shape. */
export function parseTimeSpanMs(value: string): number | null {
  const match = TIMESPAN_PATTERN.exec(value.trim());
  if (!match) return null;

  const [, negative, daysPart, hoursPart, minutesPart, secondsPart, fractionPart] = match;
  const days = daysPart ? Number(daysPart) : 0;
  const hours = Number(hoursPart);
  const minutes = Number(minutesPart);
  const seconds = Number(secondsPart);
  // Fractional seconds can carry up to 7 digits (100ns "ticks") -- only the first 3 (milliseconds) matter here.
  const millis = fractionPart ? Number(fractionPart.slice(0, 3).padEnd(3, "0")) : 0;

  const totalMs =
    (((days * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000 + millis;

  return negative ? -totalMs : totalMs;
}

/** Formats a non-negative millisecond duration back into a `TimeSpan` wire string (`"d.hh:mm:ss"`). */
export function formatMsAsTimeSpan(ms: number): string {
  const totalSeconds = Math.round(Math.abs(ms) / 1000);
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  const pad = (value: number) => String(value).padStart(2, "0");
  const sign = ms < 0 ? "-" : "";
  return `${sign}${days}.${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
}

/** Splits a millisecond duration into whole days/hours/minutes for a friendlier form UI than raw `TimeSpan` text. */
export function msToParts(ms: number): { days: number; hours: number; minutes: number } {
  const totalMinutes = Math.round(Math.abs(ms) / 60_000);
  const days = Math.floor(totalMinutes / (24 * 60));
  const hours = Math.floor((totalMinutes % (24 * 60)) / 60);
  const minutes = totalMinutes % 60;
  return { days, hours, minutes };
}

/** Inverse of `msToParts` -- combines days/hours/minutes back into milliseconds. */
export function partsToMs(parts: { days: number; hours: number; minutes: number }): number {
  return (((parts.days * 24 + parts.hours) * 60 + parts.minutes)) * 60_000;
}
