import { Button } from "flowbite-react";

// Temporary verification harness for Phase 7 Task 2 (theme tokens + Flowbite
// mapping). Replaced with the real listings page in Phase 7 Task 4.
export default function Home() {
  return (
    <div className="max-w-7xl mx-auto px-4 sm:px-6 py-10 space-y-8">
      <h1 className="text-3xl font-bold text-primary-700">ApexAutoBid</h1>

      <Button color="primary">Place Bid</Button>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-primary-600 text-white rounded-lg p-4 text-sm font-semibold">
          primary-600
        </div>
        <div className="bg-accent-mint text-slate-900 rounded-lg p-4 text-sm font-semibold">
          accent-mint
        </div>
        <div className="bg-accent-leaf text-slate-900 rounded-lg p-4 text-sm font-semibold">
          accent-leaf
        </div>
      </div>
    </div>
  );
}
