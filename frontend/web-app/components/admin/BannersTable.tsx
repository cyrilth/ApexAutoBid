"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import {
  Badge,
  Button,
  Modal,
  ModalBody,
  ModalHeader,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeadCell,
  TableRow,
} from "flowbite-react";
import { BannerFormModal } from "@/components/admin/BannerFormModal";
import { EmptyState } from "@/components/EmptyState";
import { deleteBanner } from "@/lib/admin-banners-actions";
import { toastActionError, toastSuccess } from "@/lib/toast";
import type { Banner } from "@/types/admin";

function isCurrentlyActive(banner: Banner): boolean {
  const now = Date.now();
  return new Date(banner.activeFrom).getTime() <= now && now <= new Date(banner.activeUntil).getTime();
}

/** Admin banners table (Task 8.5): full CRUD -- create/edit via `BannerFormModal`, delete with a confirm dialog. */
export function BannersTable({ banners }: { banners: Banner[] }) {
  const router = useRouter();
  const [deleting, setDeleting] = useState<Banner | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleDelete() {
    if (!deleting) return;
    setIsSubmitting(true);
    const result = await deleteBanner(deleting.id);
    setIsSubmitting(false);
    setDeleting(null);

    if (!result.success) {
      toastActionError(result.error);
      return;
    }
    toastSuccess("Banner deleted.");
    router.refresh();
  }

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <BannerFormModal
          trigger={(open) => (
            <Button type="button" color="primary" onClick={open}>
              New banner
            </Button>
          )}
        />
      </div>

      {banners.length === 0 ? (
        <EmptyState message="No banners yet." />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeadCell>Message</TableHeadCell>
                <TableHeadCell>Scope</TableHeadCell>
                <TableHeadCell>Active window</TableHeadCell>
                <TableHeadCell>Status</TableHeadCell>
                <TableHeadCell>Created by</TableHeadCell>
                <TableHeadCell>Actions</TableHeadCell>
              </TableRow>
            </TableHead>
            <TableBody className="divide-y">
              {banners.map((banner) => (
                <TableRow key={banner.id} className="bg-white">
                  <TableCell className="max-w-xs truncate">{banner.message}</TableCell>
                  <TableCell>
                    {banner.scope}
                    {banner.scope === "Auction" && banner.auctionId && (
                      <div className="text-xs text-slate-400">{banner.auctionId}</div>
                    )}
                  </TableCell>
                  <TableCell className="whitespace-nowrap text-sm text-slate-600">
                    {new Date(banner.activeFrom).toLocaleString()} &ndash;{" "}
                    {new Date(banner.activeUntil).toLocaleString()}
                  </TableCell>
                  <TableCell>
                    <Badge color={isCurrentlyActive(banner) ? "sold" : "slate"}>
                      {isCurrentlyActive(banner) ? "Active" : "Inactive"}
                    </Badge>
                  </TableCell>
                  <TableCell>{banner.createdBy}</TableCell>
                  <TableCell>
                    <div className="flex gap-2">
                      <BannerFormModal
                        banner={banner}
                        trigger={(open) => (
                          <Button size="xs" color="light" onClick={open}>
                            Edit
                          </Button>
                        )}
                      />
                      <Button size="xs" color="failure" onClick={() => setDeleting(banner)}>
                        Delete
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <Modal
        show={deleting !== null}
        onClose={() => {
          if (!isSubmitting) setDeleting(null);
        }}
        size="md"
        popup
      >
        <ModalHeader />
        <ModalBody>
          <div className="text-center">
            <h3 className="mb-2 text-lg font-semibold text-slate-900">Delete this banner?</h3>
            <p className="mb-4 text-sm text-slate-600">{deleting?.message}</p>
            <div className="flex justify-center gap-3">
              <Button color="failure" disabled={isSubmitting} onClick={handleDelete}>
                {isSubmitting ? "Deleting…" : "Yes, delete it"}
              </Button>
              <Button color="light" disabled={isSubmitting} onClick={() => setDeleting(null)}>
                Cancel
              </Button>
            </div>
          </div>
        </ModalBody>
      </Modal>
    </div>
  );
}
