import type { Metadata } from "next";
import { CreateUserModal } from "@/components/admin/CreateUserModal";
import { UsersTable } from "@/components/admin/UsersTable";
import { listUsers } from "@/lib/admin-users-service";

export const metadata: Metadata = { title: "Admin users | ApexAutoBid" };

const PAGE_SIZE = 20;

function first(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

interface AdminUsersPageProps {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}

/** Users page (Task 8.3): paged/searchable list + create-user modal + per-row admin actions. */
export default async function AdminUsersPage({ searchParams }: AdminUsersPageProps) {
  const params = await searchParams;
  const search = first(params.search)?.trim() ?? "";
  const pageRaw = Number(first(params.page));
  const page = Number.isInteger(pageRaw) && pageRaw > 0 ? pageRaw : 1;

  let result;
  let loadError = false;
  try {
    result = await listUsers({ search, page, pageSize: PAGE_SIZE });
  } catch {
    loadError = true;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold text-slate-900">Users</h1>
          <p className="text-sm text-slate-500">Search, create, and manage platform accounts.</p>
        </div>
        <CreateUserModal />
      </div>

      {loadError || !result ? (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-8 text-center">
          <p className="text-sm font-medium text-red-700">
            We couldn&apos;t load users right now. Please try again shortly.
          </p>
        </div>
      ) : (
        <UsersTable result={result} search={search} pageSize={PAGE_SIZE} />
      )}
    </div>
  );
}
