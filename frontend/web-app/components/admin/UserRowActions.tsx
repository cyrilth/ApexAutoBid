"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import DatePicker from "react-datepicker";
import "react-datepicker/dist/react-datepicker.css";
import {
  Badge,
  Button,
  Checkbox,
  Label,
  Modal,
  ModalBody,
  ModalHeader,
  TextInput,
  ToggleSwitch,
} from "flowbite-react";
import {
  resendUserConfirmation,
  resetUserPassword,
  setUserLock,
  updateUserRoles,
} from "@/lib/admin-users-actions";
import { toastActionError, toastSuccess } from "@/lib/toast";
import type { AdminUserListItem } from "@/types/admin";

/** Every role IdentityService's SeedData currently creates -- `RolesUpdateRequestDto` rejects
 * any role the RoleManager doesn't recognize (`AdminUserService.UpdateRolesAsync`), so this
 * editor only ever offers roles known to exist. */
const KNOWN_ROLES = ["admin"] as const;

type ActiveModal = "resetPassword" | "roles" | "lock" | null;

interface UserRowActionsProps {
  user: AdminUserListItem;
}

/**
 * Per-row admin actions for the Users table (Task 8.3): reset password (temp password shown
 * once, or send a reset link), resend confirmation, edit roles, lock/unlock. One component per
 * row owns all four so `UsersTable` itself stays a thin list.
 */
export function UserRowActions({ user }: UserRowActionsProps) {
  const router = useRouter();
  const [activeModal, setActiveModal] = useState<ActiveModal>(null);
  const [isResending, setIsResending] = useState(false);

  async function handleResendConfirmation() {
    setIsResending(true);
    const result = await resendUserConfirmation(user.id);
    setIsResending(false);

    if (!result.success) {
      toastActionError(result.error);
      return;
    }
    toastSuccess(`Confirmation email resent to ${user.userName}.`);
  }

  return (
    <div className="flex flex-wrap gap-2">
      <Button size="xs" color="light" onClick={() => setActiveModal("resetPassword")}>
        Reset password
      </Button>
      {!user.emailConfirmed && (
        <Button size="xs" color="light" disabled={isResending} onClick={handleResendConfirmation}>
          {isResending ? "Sending…" : "Resend confirmation"}
        </Button>
      )}
      <Button size="xs" color="light" onClick={() => setActiveModal("roles")}>
        Edit roles
      </Button>
      <Button size="xs" color={user.lockedOut ? "success" : "failure"} onClick={() => setActiveModal("lock")}>
        {user.lockedOut ? "Unlock" : "Lock"}
      </Button>

      {activeModal === "resetPassword" && (
        <ResetPasswordModal user={user} onClose={() => setActiveModal(null)} onDone={() => router.refresh()} />
      )}
      {activeModal === "roles" && (
        <EditRolesModal user={user} onClose={() => setActiveModal(null)} onDone={() => router.refresh()} />
      )}
      {activeModal === "lock" && (
        <LockModal user={user} onClose={() => setActiveModal(null)} onDone={() => router.refresh()} />
      )}
    </div>
  );
}

// ── Reset password ───────────────────────────────────────────────────────────

function ResetPasswordModal({
  user,
  onClose,
  onDone,
}: {
  user: AdminUserListItem;
  onClose: () => void;
  onDone: () => void;
}) {
  const [sendResetLink, setSendResetLink] = useState(true);
  const [newPassword, setNewPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<{ title: string; detail?: string } | null>(null);
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);

  async function handleSubmit() {
    if (!sendResetLink && newPassword.trim().length < 6) {
      setError({ title: "Password too short", detail: "The new password must be at least 6 characters." });
      return;
    }

    setIsSubmitting(true);
    setError(null);

    const result = await resetUserPassword(user.id, {
      sendResetLink,
      newPassword: sendResetLink ? undefined : newPassword,
    });

    setIsSubmitting(false);

    if (!result.success) {
      setError(result.error);
      toastActionError(result.error);
      return;
    }

    if (result.data.linkSent) {
      toastSuccess(`Password reset link sent to ${user.userName}.`);
      onDone();
      onClose();
      return;
    }

    // Temp password: shown once in this modal instead of closing immediately, per Task 8.3.
    setTemporaryPassword(result.data.temporaryPassword ?? null);
    onDone();
  }

  return (
    <Modal show onClose={onClose}>
      <ModalHeader>Reset password -- {user.userName}</ModalHeader>
      <ModalBody>
        {temporaryPassword ? (
          <div className="space-y-4">
            <p className="text-sm text-slate-600">
              The account&apos;s new temporary password (shown once -- copy it now):
            </p>
            <p className="select-all rounded-lg border border-slate-300 bg-slate-50 px-3 py-2 font-mono text-sm">
              {temporaryPassword}
            </p>
            <Button type="button" color="primary" onClick={onClose}>
              Done
            </Button>
          </div>
        ) : (
          <div className="space-y-4">
            <div className="rounded-lg border border-slate-200 px-3 py-2">
              <ToggleSwitch
                checked={sendResetLink}
                label="Send a reset link by email"
                onChange={setSendResetLink}
                disabled={isSubmitting}
              />
            </div>

            {!sendResetLink && (
              <div>
                <Label htmlFor="newPassword">New temporary password</Label>
                <TextInput
                  id="newPassword"
                  type="text"
                  value={newPassword}
                  disabled={isSubmitting}
                  onChange={(event) => setNewPassword(event.currentTarget.value)}
                />
              </div>
            )}

            {error && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
                <p className="text-sm font-semibold text-red-700">{error.title}</p>
                {error.detail && <p className="text-sm text-red-600">{error.detail}</p>}
              </div>
            )}

            <div className="flex gap-3">
              <Button type="button" color="primary" disabled={isSubmitting} onClick={handleSubmit}>
                {isSubmitting ? "Working…" : sendResetLink ? "Send reset link" : "Set new password"}
              </Button>
              <Button type="button" color="light" disabled={isSubmitting} onClick={onClose}>
                Cancel
              </Button>
            </div>
          </div>
        )}
      </ModalBody>
    </Modal>
  );
}

// ── Edit roles ────────────────────────────────────────────────────────────────

function EditRolesModal({
  user,
  onClose,
  onDone,
}: {
  user: AdminUserListItem;
  onClose: () => void;
  onDone: () => void;
}) {
  const [selected, setSelected] = useState<string[]>(user.roles);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<{ title: string; detail?: string } | null>(null);

  function toggleRole(role: string, checked: boolean) {
    setSelected((prev) => (checked ? [...prev, role] : prev.filter((r) => r !== role)));
  }

  async function handleSubmit() {
    setIsSubmitting(true);
    setError(null);

    const result = await updateUserRoles(user.id, selected);
    setIsSubmitting(false);

    if (!result.success) {
      setError(result.error);
      toastActionError(result.error);
      return;
    }

    toastSuccess(`Roles updated for ${user.userName}.`);
    onDone();
    onClose();
  }

  return (
    <Modal show onClose={onClose}>
      <ModalHeader>Edit roles -- {user.userName}</ModalHeader>
      <ModalBody>
        <div className="space-y-4">
          <div className="space-y-2">
            {KNOWN_ROLES.map((role) => (
              <div key={role} className="flex items-center gap-2">
                <Checkbox
                  id={`role-${role}`}
                  checked={selected.includes(role)}
                  disabled={isSubmitting}
                  onChange={(event) => toggleRole(role, event.currentTarget.checked)}
                />
                <Label htmlFor={`role-${role}`}>{role}</Label>
              </div>
            ))}
          </div>

          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
              <p className="text-sm font-semibold text-red-700">{error.title}</p>
              {error.detail && <p className="text-sm text-red-600">{error.detail}</p>}
            </div>
          )}

          <div className="flex gap-3">
            <Button type="button" color="primary" disabled={isSubmitting} onClick={handleSubmit}>
              {isSubmitting ? "Saving…" : "Save roles"}
            </Button>
            <Button type="button" color="light" disabled={isSubmitting} onClick={onClose}>
              Cancel
            </Button>
          </div>
        </div>
      </ModalBody>
    </Modal>
  );
}

// ── Lock / unlock ─────────────────────────────────────────────────────────────

function LockModal({
  user,
  onClose,
  onDone,
}: {
  user: AdminUserListItem;
  onClose: () => void;
  onDone: () => void;
}) {
  const locking = !user.lockedOut;
  const [lockoutEnd, setLockoutEnd] = useState<Date | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<{ title: string; detail?: string } | null>(null);

  async function handleSubmit() {
    setIsSubmitting(true);
    setError(null);

    const result = await setUserLock(user.id, locking, locking ? lockoutEnd?.toISOString() : undefined);
    setIsSubmitting(false);

    if (!result.success) {
      setError(result.error);
      toastActionError(result.error);
      return;
    }

    toastSuccess(locking ? `${user.userName} locked.` : `${user.userName} unlocked.`);
    onDone();
    onClose();
  }

  return (
    <Modal show onClose={onClose} size="md" popup>
      <ModalHeader />
      <ModalBody>
        <div className="space-y-4 text-center">
          <h3 className="text-lg font-semibold text-slate-900">
            {locking ? "Lock" : "Unlock"} <span className="font-normal">{user.userName}</span>?
          </h3>

          {locking && (
            <div className="text-left">
              <Label htmlFor="lockoutEnd">Lockout end (optional -- defaults to indefinite)</Label>
              <DatePicker
                selected={lockoutEnd}
                onChange={setLockoutEnd}
                showTimeSelect
                timeIntervals={15}
                minDate={new Date()}
                dateFormat="MMM d, yyyy h:mm aa"
                placeholderText="Indefinite"
                wrapperClassName="w-full"
                customInput={<TextInput id="lockoutEnd" />}
              />
            </div>
          )}

          {user.lockedOut && user.lockoutEnd && !locking && (
            <p className="text-sm text-slate-500">
              Currently locked until <Badge color="slate">{new Date(user.lockoutEnd).toLocaleString()}</Badge>
            </p>
          )}

          {error && (
            <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-left">
              <p className="text-sm font-semibold text-red-700">{error.title}</p>
              {error.detail && <p className="text-sm text-red-600">{error.detail}</p>}
            </div>
          )}

          <div className="flex justify-center gap-3">
            <Button color={locking ? "failure" : "success"} disabled={isSubmitting} onClick={handleSubmit}>
              {isSubmitting ? "Working…" : locking ? "Lock account" : "Unlock account"}
            </Button>
            <Button color="light" disabled={isSubmitting} onClick={onClose}>
              Cancel
            </Button>
          </div>
        </div>
      </ModalBody>
    </Modal>
  );
}
