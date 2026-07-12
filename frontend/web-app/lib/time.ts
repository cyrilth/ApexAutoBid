/**
 * Minimal static "time remaining" text for the listing page (Task 4). The
 * live `react-countdown` timer is Task 5.2 on the auction detail page --
 * this is deliberately simple, computed once per render.
 */
export function formatTimeRemaining(auctionEnd: string, now: Date = new Date()): string {
  const diffMs = new Date(auctionEnd).getTime() - now.getTime();
  if (diffMs <= 0) return "Ended";

  const totalMinutes = Math.floor(diffMs / 60_000);
  const days = Math.floor(totalMinutes / (60 * 24));
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
  const minutes = totalMinutes % 60;

  if (days > 0) return `${days}d ${hours}h left`;
  if (hours > 0) return `${hours}h ${minutes}m left`;
  return `${minutes}m left`;
}
