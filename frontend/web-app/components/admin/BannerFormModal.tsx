"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Controller, useForm, useWatch } from "react-hook-form";
import DatePicker from "react-datepicker";
import "react-datepicker/dist/react-datepicker.css";
import { Button, Label, Modal, ModalBody, ModalHeader, Select, Textarea, TextInput } from "flowbite-react";
import { createBanner, updateBanner } from "@/lib/admin-banners-actions";
import { toastActionError, toastSuccess } from "@/lib/toast";
import type { Banner, BannerScope } from "@/types/admin";

interface BannerFormValues {
  message: string;
  scope: BannerScope;
  auctionId: string;
  activeFrom: Date | null;
  activeUntil: Date | null;
}

interface BannerFormModalProps {
  /** Present in edit mode; omitted for create. */
  banner?: Banner;
  /** Renders the trigger button/link -- lets `BannersTable` reuse this for both the page's
   * "New banner" button and each row's "Edit" action. */
  trigger: (open: () => void) => ReactNode;
}

function defaultValuesFor(banner?: Banner): BannerFormValues {
  return {
    message: banner?.message ?? "",
    scope: banner?.scope ?? "Global",
    auctionId: banner?.auctionId ?? "",
    activeFrom: banner ? new Date(banner.activeFrom) : new Date(),
    activeUntil: banner ? new Date(banner.activeUntil) : null,
  };
}

/**
 * Create/edit banner modal (Task 8.5) -- one component handles both modes, matching
 * `components/AuctionForm.tsx`'s shared create/edit convention. `UpdateBannerDto` is a full
 * replace (not partial), so edit mode submits every field exactly like create.
 */
export function BannerFormModal({ banner, trigger }: BannerFormModalProps) {
  const router = useRouter();
  const [show, setShow] = useState(false);
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<BannerFormValues>({ defaultValues: defaultValuesFor(banner) });

  // `useWatch` (not `useForm`'s own `watch()`) -- the latter returns a plain function the React
  // Compiler can't safely memoize (`react-hooks/incompatible-library`); `useWatch` is
  // react-hook-form's own hook-based, compiler-friendly equivalent for a single reactive field.
  const scope = useWatch({ control, name: "scope" });

  function closeModal() {
    if (isSubmitting) return;
    setShow(false);
    setSubmitError(null);
    reset(defaultValuesFor(banner));
  }

  async function onSubmit(values: BannerFormValues) {
    setSubmitError(null);

    if (!values.activeFrom || !values.activeUntil) {
      setSubmitError({ title: "Dates required", detail: "Choose both an active-from and active-until date." });
      return;
    }
    if (values.activeUntil.getTime() <= values.activeFrom.getTime()) {
      setSubmitError({ title: "Invalid date range", detail: "Active until must be after active from." });
      return;
    }
    if (values.scope === "Auction" && !values.auctionId.trim()) {
      setSubmitError({ title: "Auction id required", detail: "Enter the auction this banner applies to." });
      return;
    }

    const payload = {
      message: values.message,
      scope: values.scope,
      auctionId: values.scope === "Auction" ? values.auctionId.trim() : undefined,
      activeFrom: values.activeFrom.toISOString(),
      activeUntil: values.activeUntil.toISOString(),
    };

    const result = banner ? await updateBanner(banner.id, payload) : await createBanner(payload);

    if (!result.success) {
      setSubmitError(result.error);
      toastActionError(result.error);
      return;
    }

    toastSuccess(banner ? "Banner updated." : "Banner published.");
    setShow(false);
    router.refresh();
  }

  return (
    <>
      {trigger(() => setShow(true))}

      <Modal show={show} onClose={closeModal}>
        <ModalHeader>{banner ? "Edit banner" : "New banner"}</ModalHeader>
        <ModalBody>
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
            <div>
              <Label htmlFor="message">Message</Label>
              <Textarea
                id="message"
                rows={3}
                color={errors.message ? "failure" : undefined}
                disabled={isSubmitting}
                {...register("message", { required: "Message is required." })}
              />
              {errors.message && <p className="mt-1 text-sm text-red-600">{errors.message.message}</p>}
            </div>

            <div>
              <Label htmlFor="scope">Scope</Label>
              <Select id="scope" disabled={isSubmitting} {...register("scope")}>
                <option value="Global">Global -- everywhere</option>
                <option value="HomePage">Home page only</option>
                <option value="Auction">Specific auction</option>
              </Select>
            </div>

            {scope === "Auction" && (
              <div>
                <Label htmlFor="auctionId">Auction id</Label>
                <TextInput
                  id="auctionId"
                  placeholder="Auction GUID"
                  disabled={isSubmitting}
                  {...register("auctionId")}
                />
              </div>
            )}

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <Label htmlFor="activeFrom">Active from</Label>
                <Controller
                  control={control}
                  name="activeFrom"
                  rules={{ required: "Required." }}
                  render={({ field }) => (
                    <DatePicker
                      selected={field.value}
                      onChange={field.onChange}
                      showTimeSelect
                      timeIntervals={15}
                      dateFormat="MMM d, yyyy h:mm aa"
                      placeholderText="Active from date and time"
                      wrapperClassName="w-full"
                      customInput={<TextInput id="activeFrom" />}
                    />
                  )}
                />
              </div>
              <div>
                <Label htmlFor="activeUntil">Active until</Label>
                <Controller
                  control={control}
                  name="activeUntil"
                  rules={{ required: "Required." }}
                  render={({ field }) => (
                    <DatePicker
                      selected={field.value}
                      onChange={field.onChange}
                      showTimeSelect
                      timeIntervals={15}
                      dateFormat="MMM d, yyyy h:mm aa"
                      placeholderText="Active until date and time"
                      wrapperClassName="w-full"
                      customInput={<TextInput id="activeUntil" />}
                    />
                  )}
                />
              </div>
            </div>

            {submitError && (
              <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
                <p className="text-sm font-semibold text-red-700">{submitError.title}</p>
                {submitError.detail && <p className="text-sm text-red-600">{submitError.detail}</p>}
              </div>
            )}

            <div className="flex gap-3">
              <Button type="submit" color="primary" disabled={isSubmitting}>
                {isSubmitting ? "Saving…" : banner ? "Save changes" : "Publish banner"}
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
