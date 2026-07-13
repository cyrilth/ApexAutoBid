"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import {
  Button,
  Modal,
  ModalBody,
  ModalHeader,
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeadCell,
  TableRow,
  TextInput,
} from "flowbite-react";
import Link from "next/link";
import { AuctionStatusBadge } from "@/components/AuctionStatusBadge";
import { cancelAuction, endAuctionNow } from "@/lib/admin-auctions-actions";
import { formatCurrency } from "@/lib/format";
import { toastActionError, toastSuccess } from "@/lib/toast";
import type { AuctionItem } from "@/types/auction";

interface AdminAuctionsTableProps {
  auctions: AuctionItem[];
  totalPages: number;
  currentPage: number;
  searchTerm: string;
  seller: string;
}

function buildHref(overrides: { searchTerm?: string; seller?: string; page?: number }): string {
  const params = new URLSearchParams();
  if (overrides.searchTerm) params.set("searchTerm", overrides.searchTerm);
  if (overrides.seller) params.set("seller", overrides.seller);
  if (overrides.page && overrides.page > 1) params.set("page", String(overrides.page));
  const qs = params.toString();
  return qs ? `/admin/auctions?${qs}` : "/admin/auctions";
}

/**
 * Admin auctions table (Task 8.4): search by make/model/seller (against the same `GET
 * api/search` the public listing page uses), each row carrying "End now"/"Cancel" moderation
 * actions with a confirm dialog.
 */
export function AdminAuctionsTable({
  auctions,
  totalPages,
  currentPage,
  searchTerm,
  seller,
}: AdminAuctionsTableProps) {
  const router = useRouter();
  const [confirming, setConfirming] = useState<{ action: "end" | "cancel"; auction: AuctionItem } | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    router.push(
      buildHref({
        searchTerm: (data.get("searchTerm") as string)?.trim(),
        seller: (data.get("seller") as string)?.trim(),
        page: 1,
      })
    );
  }

  async function handleConfirm() {
    if (!confirming) return;
    setIsSubmitting(true);

    const result =
      confirming.action === "end"
        ? await endAuctionNow(confirming.auction.id)
        : await cancelAuction(confirming.auction.id);

    setIsSubmitting(false);
    setConfirming(null);

    if (!result.success) {
      toastActionError(result.error);
      return;
    }

    toastSuccess(
      confirming.action === "end"
        ? "Auction ended -- it will finalize shortly."
        : "Auction cancelled."
    );
    router.refresh();
  }

  return (
    <div className="space-y-4">
      <form
        onSubmit={handleSearchSubmit}
        className="flex flex-wrap items-end gap-3 rounded-lg border border-slate-200 bg-white p-4"
      >
        <div className="min-w-[200px] flex-1">
          <TextInput name="searchTerm" placeholder="Make or model" defaultValue={searchTerm} />
        </div>
        <div className="w-40">
          <TextInput name="seller" placeholder="Seller username" defaultValue={seller} />
        </div>
        <button
          type="submit"
          className="rounded-lg bg-primary-600 px-4 py-2 text-sm font-medium text-white hover:bg-primary-700 focus:outline-none focus:ring-4 focus:ring-primary-400"
        >
          Search
        </button>
        {(searchTerm || seller) && (
          <button
            type="button"
            className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100"
            onClick={() => router.push("/admin/auctions")}
          >
            Clear
          </button>
        )}
      </form>

      {auctions.length === 0 ? (
        <div className="rounded-lg border border-slate-200 bg-white py-16 text-center text-sm text-slate-500">
          No auctions match your search.
        </div>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeadCell>Vehicle</TableHeadCell>
                <TableHeadCell>Seller</TableHeadCell>
                <TableHeadCell>Status</TableHeadCell>
                <TableHeadCell>High bid</TableHeadCell>
                <TableHeadCell>Ends</TableHeadCell>
                <TableHeadCell>Actions</TableHeadCell>
              </TableRow>
            </TableHead>
            <TableBody className="divide-y">
              {auctions.map((auction) => {
                const isModerable = auction.status === "Live";
                return (
                  <TableRow key={auction.id} className="bg-white">
                    <TableCell>
                      <Link href={`/auctions/${auction.id}`} className="font-medium text-primary-700 hover:underline">
                        {auction.year} {auction.make} {auction.model}
                      </Link>
                    </TableCell>
                    <TableCell>{auction.seller}</TableCell>
                    <TableCell>
                      <AuctionStatusBadge item={auction} />
                    </TableCell>
                    <TableCell>
                      {auction.currentHighBid != null ? formatCurrency(auction.currentHighBid) : "--"}
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-sm text-slate-600">
                      {new Date(auction.auctionEnd).toLocaleString()}
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-2">
                        <Button
                          size="xs"
                          color="light"
                          disabled={!isModerable}
                          onClick={() => setConfirming({ action: "end", auction })}
                        >
                          End now
                        </Button>
                        <Button
                          size="xs"
                          color="failure"
                          disabled={!isModerable}
                          onClick={() => setConfirming({ action: "cancel", auction })}
                        >
                          Cancel
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}

      {totalPages > 1 && (
        <div className="flex justify-center">
          <Pagination
            currentPage={currentPage}
            totalPages={totalPages}
            onPageChange={(page) => router.push(buildHref({ searchTerm, seller, page }))}
            showIcons
          />
        </div>
      )}

      <Modal
        show={confirming !== null}
        onClose={() => {
          if (!isSubmitting) setConfirming(null);
        }}
        size="md"
        popup
      >
        <ModalHeader />
        <ModalBody>
          <div className="text-center">
            <h3 className="mb-2 text-lg font-semibold text-slate-900">
              {confirming?.action === "end" ? "End this auction now?" : "Cancel this auction?"}
            </h3>
            <p className="mb-4 text-sm text-slate-600">
              {confirming && (
                <>
                  {confirming.auction.year} {confirming.auction.make} {confirming.auction.model} --{" "}
                  {confirming.action === "end"
                    ? "closes immediately and finalizes on its normal schedule."
                    : "this cannot be undone."}
                </>
              )}
            </p>
            <div className="flex justify-center gap-3">
              <Button
                color={confirming?.action === "cancel" ? "failure" : "primary"}
                disabled={isSubmitting}
                onClick={handleConfirm}
              >
                {isSubmitting ? "Working…" : "Yes, confirm"}
              </Button>
              <Button color="light" disabled={isSubmitting} onClick={() => setConfirming(null)}>
                Cancel
              </Button>
            </div>
          </div>
        </ModalBody>
      </Modal>
    </div>
  );
}
