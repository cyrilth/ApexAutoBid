"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Controller, useForm } from "react-hook-form";
import DatePicker from "react-datepicker";
import "react-datepicker/dist/react-datepicker.css";
import { Button, HelperText, Label, TextInput } from "flowbite-react";
import { AuctionImageManager, imagesToPayload, type ManagedImage } from "@/components/AuctionImageManager";
import { createAuction, updateAuction } from "@/lib/auction-actions";
import { toastActionError } from "@/lib/toast";
import { parseTimeSpanMs } from "@/lib/timespan";
import type { AuctionDetail } from "@/types/auction";
import type { DurationLimits } from "@/types/admin";

interface AuctionFormValues {
  make: string;
  model: string;
  color: string;
  year: number;
  mileage: number;
  reservePrice: number;
  auctionEnd: Date | null;
  images: ManagedImage[];
  /** Admin-only "create auction for any seller" fields (Phase 11 Task 3.1/8.4) -- always
   * present on the form's values, but only ever sent to the backend (and only ever rendered)
   * when `isAdmin` is true; ignored server-side for every other caller regardless. */
  seller: string;
  sellerEmail: string;
}

interface AuctionFormProps {
  mode: "create" | "edit";
  /** Required (and only used) in edit mode -- the auction being edited. */
  auction?: AuctionDetail;
  /** Shows the seller-assignment fields (create mode only) and exempts the AuctionEnd picker
   * from the platform's duration bounds, mirroring the backend's own admin exemption (Phase 11
   * Task 3.1/3.4). `false`/omitted for every non-admin caller. */
  isAdmin?: boolean;
  /** The platform's currently-effective auction duration bounds (`GET api/auctions/duration-limits`,
   * anonymous) -- constrains the create form's `AuctionEnd` picker (Phase 11 Task 3.8). Ignored
   * for admins, who are exempt from these bounds server-side. */
  durationLimits?: DurationLimits;
}

const CURRENT_YEAR = new Date().getFullYear();

/**
 * Seeds the image manager from an existing gallery on edit. These entries
 * are tagged `source: "url"` (not "upload") because `AuctionImage` (the
 * `GET api/auctions/{id}` response shape) doesn't carry the original
 * upload-url object key -- only `url`/`thumbnailUrl`/`sortOrder` -- so
 * "Generate thumbnail" (6.4, which needs that key) isn't offered for
 * pre-existing images, only for ones uploaded in this editing session.
 */
function initialImagesFrom(auction?: AuctionDetail): ManagedImage[] {
  if (!auction) return [];
  return auction.images.map((image) => ({
    clientId: crypto.randomUUID(),
    url: image.url,
    thumbnailUrl: image.thumbnailUrl,
    source: "url",
    status: "ready",
  }));
}

/**
 * Shared create/edit auction form (Phase 7 Task 6). `react-hook-form` (6.1)
 * drives every field, including the image gallery (via `Controller` wrapping
 * `AuctionImageManager`) and the `react-datepicker` end-date field (6.2,
 * create only -- `UpdateAuctionDto` has no `auctionEnd`, so regular sellers
 * can't change it once set; see `types/auction-form.ts`).
 */
export function AuctionForm({ mode, auction, isAdmin = false, durationLimits }: AuctionFormProps) {
  const router = useRouter();
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | null>(null);

  const {
    register,
    handleSubmit,
    control,
    formState: { errors, isSubmitting },
  } = useForm<AuctionFormValues>({
    defaultValues: {
      make: auction?.make ?? "",
      model: auction?.model ?? "",
      color: auction?.color ?? "",
      year: auction?.year ?? CURRENT_YEAR,
      mileage: auction?.mileage ?? 0,
      reservePrice: auction?.reservePrice ?? 0,
      auctionEnd: null,
      images: initialImagesFrom(auction),
      seller: "",
      sellerEmail: "",
    },
  });

  // Admins are exempt from the platform's duration bounds server-side (Task 3.1/3.4), so the
  // picker only constrains non-admin sellers -- an admin sees no min/max at all. Computed once
  // via `useState`'s lazy initializer (same "create on first render, never again" idiom
  // `components/BidStoreProvider.tsx` uses) rather than as a plain render-body `const` -- a
  // bare `new Date(Date.now() + ...)` assigned directly in the component body is an impure
  // call the React Compiler refuses to memoize (`react-hooks/purity`); a lazy initializer is
  // the sanctioned way to run an impure computation exactly once per mount instead.
  const minDurationMs = durationLimits ? parseTimeSpanMs(durationLimits.minDuration) : null;
  const maxDurationMs = durationLimits ? parseTimeSpanMs(durationLimits.maxDuration) : null;
  const [minEndDate] = useState<Date>(() =>
    !isAdmin && minDurationMs != null ? new Date(Date.now() + minDurationMs) : new Date()
  );
  const [maxEndDate] = useState<Date | undefined>(() =>
    !isAdmin && maxDurationMs != null ? new Date(Date.now() + maxDurationMs) : undefined
  );

  async function onSubmit(values: AuctionFormValues) {
    setSubmitError(null);
    const images = imagesToPayload(values.images);

    if (mode === "create") {
      // react-hook-form's Controller `rules.required` already guards this,
      // but auctionEnd's type is `Date | null` -- narrow it before building
      // the ISO string the backend expects.
      if (!values.auctionEnd) {
        setSubmitError({ title: "Auction end required", detail: "Choose when the auction should close." });
        return;
      }

      const result = await createAuction({
        make: values.make,
        model: values.model,
        color: values.color,
        year: Number(values.year),
        mileage: Number(values.mileage),
        reservePrice: Number(values.reservePrice),
        images,
        auctionEnd: values.auctionEnd.toISOString(),
        // Only ever sent (and only ever rendered as fields, see the admin-only block below)
        // when isAdmin -- the backend ignores these for every other caller regardless.
        ...(isAdmin && values.seller.trim() ? { seller: values.seller.trim(), sellerEmail: values.sellerEmail.trim() } : {}),
      });

      if (!result.success) {
        setSubmitError(result.error);
        // Red toast, title only (Task 19, Docs/Requirements.md §13.2) -- alongside (not instead
        // of) the inline panel below, which still shows `detail` for extra context next to the
        // submit button.
        toastActionError(result.error);
        return;
      }

      router.push(`/auctions/${result.data.id}`);
      return;
    }

    // Edit mode.
    if (!auction) return;

    const result = await updateAuction(auction.id, {
      make: values.make,
      model: values.model,
      color: values.color,
      year: Number(values.year),
      mileage: Number(values.mileage),
      images,
    });

    if (!result.success) {
      setSubmitError(result.error);
      // See the create-mode branch above -- same title-only toast + detail-bearing inline panel split.
      toastActionError(result.error);
      return;
    }

    router.push(`/auctions/${auction.id}`);
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-6" noValidate>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <Label htmlFor="make">Make</Label>
          <TextInput
            id="make"
            color={errors.make ? "failure" : undefined}
            disabled={isSubmitting}
            {...register("make", { required: "Make is required." })}
          />
          {errors.make && <p className="mt-1 text-sm text-red-600">{errors.make.message}</p>}
        </div>

        <div>
          <Label htmlFor="model">Model</Label>
          <TextInput
            id="model"
            color={errors.model ? "failure" : undefined}
            disabled={isSubmitting}
            {...register("model", { required: "Model is required." })}
          />
          {errors.model && <p className="mt-1 text-sm text-red-600">{errors.model.message}</p>}
        </div>

        <div>
          <Label htmlFor="color">Color</Label>
          <TextInput
            id="color"
            color={errors.color ? "failure" : undefined}
            disabled={isSubmitting}
            {...register("color", { required: "Color is required." })}
          />
          {errors.color && <p className="mt-1 text-sm text-red-600">{errors.color.message}</p>}
        </div>

        <div>
          <Label htmlFor="year">Year</Label>
          <TextInput
            id="year"
            type="number"
            color={errors.year ? "failure" : undefined}
            disabled={isSubmitting}
            {...register("year", {
              required: "Year is required.",
              valueAsNumber: true,
              min: { value: 1900, message: "Year must be 1900 or later." },
              max: { value: CURRENT_YEAR + 1, message: `Year can't be later than ${CURRENT_YEAR + 1}.` },
            })}
          />
          {errors.year && <p className="mt-1 text-sm text-red-600">{errors.year.message}</p>}
        </div>

        <div>
          <Label htmlFor="mileage">Mileage</Label>
          <TextInput
            id="mileage"
            type="number"
            color={errors.mileage ? "failure" : undefined}
            disabled={isSubmitting}
            {...register("mileage", {
              required: "Mileage is required.",
              valueAsNumber: true,
              min: { value: 0, message: "Mileage can't be negative." },
            })}
          />
          {errors.mileage && <p className="mt-1 text-sm text-red-600">{errors.mileage.message}</p>}
        </div>

        <div>
          <Label htmlFor="reservePrice">Reserve price</Label>
          <TextInput
            id="reservePrice"
            type="number"
            color={errors.reservePrice ? "failure" : undefined}
            disabled={isSubmitting}
            {...register("reservePrice", {
              valueAsNumber: true,
              min: { value: 0, message: "Reserve price can't be negative." },
            })}
          />
          <HelperText>Leave at 0 for no reserve.</HelperText>
          {errors.reservePrice && <p className="mt-1 text-sm text-red-600">{errors.reservePrice.message}</p>}
        </div>
      </div>

      {mode === "create" && isAdmin && (
        <div className="grid grid-cols-1 gap-4 rounded-lg border border-slate-200 bg-slate-50 p-4 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <p className="text-sm font-medium text-slate-900">Create for another seller (optional)</p>
            <HelperText>Leave blank to list this auction under your own account instead.</HelperText>
          </div>
          <div>
            <Label htmlFor="seller">Seller username</Label>
            <TextInput id="seller" disabled={isSubmitting} {...register("seller")} />
          </div>
          <div>
            <Label htmlFor="sellerEmail">Seller email</Label>
            <TextInput id="sellerEmail" type="email" disabled={isSubmitting} {...register("sellerEmail")} />
          </div>
        </div>
      )}

      {mode === "create" ? (
        <div>
          <Label htmlFor="auctionEnd">Auction end</Label>
          <Controller
            control={control}
            name="auctionEnd"
            rules={{
              required: "Choose when the auction should close.",
              validate: (value) =>
                !value ||
                (value.getTime() > Date.now() &&
                  (isAdmin || value.getTime() >= minEndDate.getTime()) &&
                  (isAdmin || !maxEndDate || value.getTime() <= maxEndDate.getTime())) ||
                "Auction end must be in the future and within the platform's allowed duration.",
            }}
            render={({ field }) => (
              <DatePicker
                selected={field.value}
                onChange={field.onChange}
                onBlur={field.onBlur}
                disabled={isSubmitting}
                showTimeSelect
                timeIntervals={15}
                minDate={minEndDate}
                maxDate={maxEndDate}
                dateFormat="MMM d, yyyy h:mm aa"
                placeholderText="Select a date and time"
                wrapperClassName="w-full"
                customInput={<TextInput id="auctionEnd" color={errors.auctionEnd ? "failure" : undefined} />}
              />
            )}
          />
          {errors.auctionEnd && <p className="mt-1 text-sm text-red-600">{errors.auctionEnd.message}</p>}
          {!isAdmin && durationLimits && (
            <HelperText>
              Must be between {minEndDate.toLocaleString()} and {maxEndDate?.toLocaleString()}.
            </HelperText>
          )}
        </div>
      ) : (
        auction && (
          <div>
            <Label>Auction end</Label>
            <p className="text-sm text-slate-600">
              {new Date(auction.auctionEnd).toLocaleString()} -- only an admin can change the end time once an
              auction is live.
            </p>
          </div>
        )
      )}

      <div>
        <Controller
          control={control}
          name="images"
          rules={{
            validate: (value) => {
              if (value.some((image) => image.status === "uploading")) {
                return "Wait for every image to finish uploading.";
              }
              const ready = value.filter((image) => image.status === "ready");
              if (ready.length < 1) return "Add at least 1 image.";
              if (ready.length > 10) return "A maximum of 10 images is allowed.";
              return true;
            },
          }}
          render={({ field }) => (
            <AuctionImageManager initialImages={field.value} onChange={field.onChange} disabled={isSubmitting} />
          )}
        />
        {errors.images && <p className="mt-1 text-sm text-red-600">{errors.images.message as string}</p>}
      </div>

      {submitError && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
          <p className="text-sm font-semibold text-red-700">{submitError.title}</p>
          {submitError.detail && <p className="text-sm text-red-600">{submitError.detail}</p>}
        </div>
      )}

      <div className="flex gap-3">
        <Button type="submit" color="primary" disabled={isSubmitting}>
          {isSubmitting ? "Saving…" : mode === "create" ? "Create auction" : "Save changes"}
        </Button>
        <Button type="button" color="light" disabled={isSubmitting} onClick={() => router.back()}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
