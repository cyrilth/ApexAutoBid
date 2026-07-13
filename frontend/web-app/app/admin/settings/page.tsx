import type { Metadata } from "next";
import { DurationSettingsForm } from "@/components/admin/DurationSettingsForm";
import { getDurationSettings } from "@/lib/admin-settings-service";

export const metadata: Metadata = { title: "Admin settings | ApexAutoBid" };

/** Settings page (Task 8.7): platform min/max auction duration. */
export default async function AdminSettingsPage() {
  let settings;
  let loadError = false;
  try {
    settings = await getDurationSettings();
  } catch {
    loadError = true;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-slate-900">Settings</h1>
        <p className="text-sm text-slate-500">
          The platform-wide minimum and maximum auction duration -- takes effect immediately.
        </p>
      </div>

      {loadError || !settings ? (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-8 text-center">
          <p className="text-sm font-medium text-red-700">
            We couldn&apos;t load the settings right now. Please try again shortly.
          </p>
        </div>
      ) : (
        <DurationSettingsForm initial={settings} />
      )}
    </div>
  );
}
