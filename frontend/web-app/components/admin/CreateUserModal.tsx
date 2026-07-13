"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import {
  Button,
  HelperText,
  Label,
  Modal,
  ModalBody,
  ModalHeader,
  TextInput,
  ToggleSwitch,
} from "flowbite-react";
import { createUser } from "@/lib/admin-users-actions";
import { toastActionError, toastSuccess } from "@/lib/toast";

interface CreateUserFormValues {
  userName: string;
  email: string;
  password: string;
}

/**
 * Create-user modal (Task 8.3) -- `react-hook-form` drives the fields; the "pre-confirmed"
 * toggle is plain component state (a single boolean, not worth a registered field). Refreshes
 * the Users page's server data (`router.refresh()`) on success so the new user shows up in the
 * list immediately, matching the rest of the admin area's "refresh state after actions" pattern.
 */
export function CreateUserModal() {
  const router = useRouter();
  const [show, setShow] = useState(false);
  const [preConfirmed, setPreConfirmed] = useState(false);
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateUserFormValues>({ defaultValues: { userName: "", email: "", password: "" } });

  function closeModal() {
    if (isSubmitting) return;
    setShow(false);
    setSubmitError(null);
    setPreConfirmed(false);
    reset();
  }

  async function onSubmit(values: CreateUserFormValues) {
    setSubmitError(null);

    const result = await createUser({ ...values, preConfirmed });

    if (!result.success) {
      setSubmitError(result.error);
      toastActionError(result.error);
      return;
    }

    toastSuccess(`User '${result.data.userName}' created.`);
    closeModal();
    router.refresh();
  }

  return (
    <>
      <Button type="button" color="primary" onClick={() => setShow(true)}>
        Create user
      </Button>

      <Modal show={show} onClose={closeModal}>
        <ModalHeader>Create user</ModalHeader>
        <ModalBody>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
            <div>
              <Label htmlFor="userName">Username</Label>
              <TextInput
                id="userName"
                color={errors.userName ? "failure" : undefined}
                disabled={isSubmitting}
                {...register("userName", { required: "Username is required." })}
              />
              {errors.userName && <p className="mt-1 text-sm text-red-600">{errors.userName.message}</p>}
            </div>

            <div>
              <Label htmlFor="email">Email</Label>
              <TextInput
                id="email"
                type="email"
                color={errors.email ? "failure" : undefined}
                disabled={isSubmitting}
                {...register("email", { required: "Email is required." })}
              />
              {errors.email && <p className="mt-1 text-sm text-red-600">{errors.email.message}</p>}
            </div>

            <div>
              <Label htmlFor="password">Password</Label>
              <TextInput
                id="password"
                type="password"
                color={errors.password ? "failure" : undefined}
                disabled={isSubmitting}
                {...register("password", {
                  required: "Password is required.",
                  minLength: { value: 6, message: "Password must be at least 6 characters." },
                })}
              />
              {errors.password && <p className="mt-1 text-sm text-red-600">{errors.password.message}</p>}
            </div>

            <div className="rounded-lg border border-slate-200 px-3 py-2">
              <ToggleSwitch
                checked={preConfirmed}
                label="Pre-confirmed"
                onChange={setPreConfirmed}
                disabled={isSubmitting}
              />
              <HelperText>Skip email verification -- no confirmation email is sent.</HelperText>
            </div>

            {submitError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
                <p className="text-sm font-semibold text-red-700">{submitError.title}</p>
                {submitError.detail && <p className="text-sm text-red-600">{submitError.detail}</p>}
              </div>
            )}

            <div className="flex gap-3">
              <Button type="submit" color="primary" disabled={isSubmitting}>
                {isSubmitting ? "Creating…" : "Create user"}
              </Button>
              <Button type="button" color="light" disabled={isSubmitting} onClick={closeModal}>
                Cancel
              </Button>
            </div>
          </form>
        </ModalBody>
      </Modal>
    </>
  );
}
