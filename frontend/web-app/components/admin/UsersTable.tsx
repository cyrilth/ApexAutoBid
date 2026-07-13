"use client";

import type { FormEvent } from "react";
import { useRouter } from "next/navigation";
import { Badge, Pagination, Table, TableBody, TableCell, TableHead, TableHeadCell, TableRow, TextInput } from "flowbite-react";
import { EmptyState } from "@/components/EmptyState";
import { UserRowActions } from "@/components/admin/UserRowActions";
import type { AdminUserListResult } from "@/types/admin";

interface UsersTableProps {
  result: AdminUserListResult;
  search: string;
  pageSize: number;
}

function buildHref(overrides: { search?: string; page?: number }): string {
  const params = new URLSearchParams();
  if (overrides.search) params.set("search", overrides.search);
  if (overrides.page && overrides.page > 1) params.set("page", String(overrides.page));
  const qs = params.toString();
  return qs ? `/admin/users?${qs}` : "/admin/users";
}

/**
 * Users table (Task 8.3): a search box (driven by the URL, same shareable-state convention as
 * `components/AuctionToolbar.tsx`) plus a paged `Table` of results, each row's actions handled
 * by `UserRowActions`.
 */
export function UsersTable({ result, search, pageSize }: UsersTableProps) {
  const router = useRouter();
  const totalPages = Math.max(1, Math.ceil(result.totalCount / pageSize));

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const value = (data.get("search") as string)?.trim() ?? "";
    router.push(buildHref({ search: value, page: 1 }));
  }

  return (
    <div className="space-y-4">
      <form
        onSubmit={handleSearchSubmit}
        className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-4"
      >
        <div className="min-w-[220px] flex-1">
          <TextInput name="search" placeholder="Search by username or email" defaultValue={search} />
        </div>
        <button
          type="submit"
          className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-medium text-white hover:bg-primary-700 focus:outline-none focus:ring-4 focus:ring-primary-400"
        >
          Search
        </button>
        {search && (
          <button
            type="button"
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
            onClick={() => router.push("/admin/users")}
          >
            Clear
          </button>
        )}
      </form>

      {result.items.length === 0 ? (
        <EmptyState message="No users match your search." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeadCell>Username</TableHeadCell>
                <TableHeadCell>Email</TableHeadCell>
                <TableHeadCell>Status</TableHeadCell>
                <TableHeadCell>Roles</TableHeadCell>
                <TableHeadCell>Actions</TableHeadCell>
              </TableRow>
            </TableHead>
            <TableBody className="divide-y">
              {result.items.map((user) => (
                <TableRow key={user.id} className="bg-white">
                  <TableCell className="font-medium text-slate-900">{user.userName}</TableCell>
                  <TableCell>{user.email ?? "--"}</TableCell>
                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      <Badge color={user.emailConfirmed ? "sold" : "amber"}>
                        {user.emailConfirmed ? "Confirmed" : "Unconfirmed"}
                      </Badge>
                      {user.lockedOut && <Badge color="failure">Locked</Badge>}
                    </div>
                  </TableCell>
                  <TableCell>
                    {user.roles.length > 0 ? (
                      <div className="flex flex-wrap gap-1">
                        {user.roles.map((role) => (
                          <Badge key={role} color="primary">
                            {role}
                          </Badge>
                        ))}
                      </div>
                    ) : (
                      <span className="text-sm text-slate-400">--</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <UserRowActions user={user} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {totalPages > 1 && (
        <div className="flex justify-center">
          <Pagination
            currentPage={result.page}
            totalPages={totalPages}
            onPageChange={(page) => router.push(buildHref({ search, page }))}
            showIcons
          />
        </div>
      )}
    </div>
  );
}
