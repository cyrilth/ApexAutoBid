"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Controller, useForm } from "react-hook-form";
import DatePicker from "react-datepicker";
import "react-datepicker/dist/react-datepicker.css";
import { Button, HelperText, Label, TextInput } from "flowbite-react";
import { AuctionImageManager, imagesToPayload, type ManagedImage } from "@/components/AuctionImageManager";
import { createAuction, updateAuction } from "@/lib/auction-actions";
import type { AuctionDetail } from "@/types/auction";

interface AuctionFormValues {
  make: string;
  model: string;
  color: string;
  year: number;
  mileage: number;
  reservePrice: number;
  auctionEnd: Date | null;
  images: ManagedImage[];
}

interface AuctionFormProps {
  mode: "create" | "edit";
  /** Required (and only used) in edit mode -- the auction being edited. */
  auction?: AuctionDetail;
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
export function AuctionForm({ mode, auction }: AuctionFormProps) {
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
    },
  });

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
      });

      if (!result.success) {
        setSubmitError(result.error);
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

      {mode === "create" ? (
        <div>
          <Label htmlFor="auctionEnd">Auction end</Label>
          <Controller
            control={control}
            name="auctionEnd"
            rules={{
              required: "Choose when the auction should close.",
              validate: (value) => !value || value.getTime() > Date.now() || "Auction end must be in the future.",
            }}
            render={({ field }) => (
              <DatePicker
                selected={field.value}
                onChange={field.onChange}
                onBlur={field.onBlur}
                disabled={isSubmitting}
                showTimeSelect
                timeIntervals={15}
                minDate={new Date()}
                dateFormat="MMM d, yyyy h:mm aa"
                placeholderText="Select a date and time"
                wrapperClassName="w-full"
                customInput={<TextInput id="auctionEnd" color={errors.auctionEnd ? "failure" : undefined} />}
              />
            )}
          />
          {errors.auctionEnd && <p className="mt-1 text-sm text-red-600">{errors.auctionEnd.message}</p>}
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
