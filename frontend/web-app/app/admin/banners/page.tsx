import type { Metadata } from "next";
import { BannersTable } from "@/components/admin/BannersTable";
import { getAllBanners } from "@/lib/admin-banners-service";

export const metadata: Metadata = { title: "Admin banners | ApexAutoBid" };

/** Banners page (Task 8.5): CRUD table for platform banner messages. */
export default async function AdminBannersPage() {
  let banners;
  let loadError = false;
  try {
    banners = await getAllBanners();
  } catch {
    loadError = true;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold text-slate-900">Banners</h1>
        <p className="text-sm text-slate-500">
          Site-wide announcements shown on the home page and/or a specific auction.
        </p>
      </div>

      {loadError || !banners ? (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-8 text-center">
          <p className="text-sm font-medium text-red-700">
            We couldn&apos;t load banners right now. Please try again shortly.
          </p>
        </div>
      ) : (
        <BannersTable banners={banners} />
      )}
    </div>
  );
}
