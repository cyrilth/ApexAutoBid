"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Button, Modal, ModalBody, ModalHeader } from "flowbite-react";
import { deleteAuction } from "@/lib/auction-actions";
import { toastActionError } from "@/lib/toast";

interface DeleteAuctionButtonProps {
  auctionId: string;
  /** "{year} {make} {model}" -- shown in the confirmation copy so the seller/admin can double-check they picked the right auction. */
  displayName: string;
}

/**
 * Delete button + confirmation modal for the auction detail page (Task 7).
 * A Client Component -- Flowbite `Modal`/`Button` need interactivity that a
 * Server Component can't provide -- rendered only when the server-computed
 * `canEdit` gate in app/auctions/[id]/page.tsx allows it. The DELETE endpoint
 * remains the real authority regardless (see lib/auction-actions.ts's
 * deleteAuction remarks on the ownership check it enforces).
 */
export function DeleteAuctionButton({ auctionId, displayName }: DeleteAuctionButtonProps) {
  const router = useRouter();
  const [showModal, setShowModal] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [error, setError] = useState<{ title: string; detail?: string } | null>(null);

  async function handleConfirmDelete() {
    setIsDeleting(true);
    setError(null);

    const result = await deleteAuction(auctionId);

    if (!result.success) {
      setError(result.error);
      // Red toast, title only (Task 19, Docs/Requirements.md §13.2) -- alongside the modal's
      // own inline error, which still shows `detail` for extra context.
      toastActionError(result.error);
      setIsDeleting(false);
      return;
    }

    // We're navigating away from this now-deleted auction's route entirely,
    // so a plain push to the listing page is enough -- router.refresh()
    // wouldn't apply here (nothing on this page to re-render, we're leaving
    // it), and the listing page's own no-store fetch (lib/auction-service.ts)
    // picks up the deletion on its next render rather than showing anything
    // cached.
    router.push("/");
  }

  function closeModal() {
    if (isDeleting) return; // don't let the confirmation dialog vanish mid-request
    setShowModal(false);
    setError(null);
  }

  return (
    <>
      <Button
        type="button"
        color="failure"
        size="xs"
        onClick={() => setShowModal(true)}
        aria-label={`Delete ${displayName}`}
      >
        Delete
      </Button>

      <Modal show={showModal} onClose={closeModal} size="md" popup>
        <ModalHeader />
        <ModalBody>
          <div className="text-center">
            <h3 className="mb-2 text-lg font-semibold text-slate-900">Delete this auction?</h3>
            <p className="mb-4 text-sm text-slate-600">
              You&apos;re about to permanently delete the <span className="font-medium">{displayName}</span> auction.
              This action can&apos;t be undone.
            </p>

            {error && (
              <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-left">
                <p className="text-sm font-semibold text-red-700">{error.title}</p>
                {error.detail && <p className="text-sm text-red-600">{error.detail}</p>}
              </div>
            )}

            <div className="flex justify-center gap-3">
              <Button color="failure" disabled={isDeleting} onClick={handleConfirmDelete}>
                {isDeleting ? "Deleting…" : "Yes, delete it"}
              </Button>
              <Button color="light" disabled={isDeleting} onClick={closeModal}>
                Cancel
              </Button>
            </div>
          </div>
        </ModalBody>
      </Modal>
    </>
  );
}
