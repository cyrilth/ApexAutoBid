/** Matches AuctionCard's layout so the loading grid doesn't jump around once real cards land. */
export function AuctionCardSkeleton() {
  return (
    <div className="animate-pulse overflow-hidden rounded-lg border border-slate-200 bg-white">
      <div className="aspect-[4/3] w-full bg-slate-200" />
      <div className="space-y-3 p-4">
        <div className="h-5 w-3/4 rounded bg-slate-200" />
        <div className="h-4 w-1/3 rounded bg-slate-200" />
        <div className="flex items-end justify-between pt-2">
          <div className="space-y-2">
            <div className="h-3 w-16 rounded bg-slate-200" />
            <div className="h-5 w-20 rounded bg-slate-200" />
          </div>
          <div className="h-3 w-14 rounded bg-slate-200" />
        </div>
      </div>
    </div>
  );
}
