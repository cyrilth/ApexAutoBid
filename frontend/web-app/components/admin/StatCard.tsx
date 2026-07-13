import type { ReactNode } from "react";

interface StatCardProps {
  label: string;
  /** The big number (Docs/DesignGuide.md §7 -- "dashboard stats cards show one number large with a label beneath"). */
  value: ReactNode;
  /** Small secondary detail below the label, e.g. a status breakdown. */
  detail?: ReactNode;
}

/** One dashboard stat card -- a big number with a label beneath (Task 8.2). */
export function StatCard({ label, value, detail }: StatCardProps) {
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4">
      <p className="text-3xl font-bold text-slate-900">{value}</p>
      <p className="text-sm text-slate-500">{label}</p>
      {detail && <div className="mt-2 text-sm text-slate-600">{detail}</div>}
    </div>
  );
}

/** Same card, but for a stats source that failed to load -- Task 8.2's "graceful per-card error state". */
export function StatCardError({ label }: { label: string }) {
  return (
    <div className="rounded-lg border border-red-200 bg-red-50 p-4">
      <p className="text-lg font-semibold text-red-700">Unavailable</p>
      <p className="text-sm text-red-600">{label}</p>
    </div>
  );
}
