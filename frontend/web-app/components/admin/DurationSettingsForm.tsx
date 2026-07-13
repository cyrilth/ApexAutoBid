"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { Button, HelperText, Label, TextInput } from "flowbite-react";
import { updateDurationSettings } from "@/lib/admin-settings-actions";
import { toastActionError, toastSuccess } from "@/lib/toast";
import { formatMsAsTimeSpan, msToParts, parseTimeSpanMs, partsToMs } from "@/lib/timespan";
import type { DurationSettings } from "@/types/admin";

interface DurationFormValues {
  minDays: number;
  minHours: number;
  minMinutes: number;
  maxDays: number;
  maxHours: number;
  maxMinutes: number;
}

function toFormValues(settings: DurationSettings): DurationFormValues {
  const min = msToParts(parseTimeSpanMs(settings.minDuration) ?? 0);
  const max = msToParts(parseTimeSpanMs(settings.maxDuration) ?? 0);
  return {
    minDays: min.days,
    minHours: min.hours,
    minMinutes: min.minutes,
    maxDays: max.days,
    maxHours: max.hours,
    maxMinutes: max.minutes,
  };
}

/**
 * Platform auction-duration settings form (Task 8.7) -- `PUT api/admin/settings/duration`
 * takes effect immediately. Days/hours/minutes number fields are friendlier than raw `TimeSpan`
 * text (`lib/timespan.ts` converts between the two); shows `updatedBy`/`updatedAt` from the
 * last save.
 */
export function DurationSettingsForm({ initial }: { initial: DurationSettings }) {
  const router = useRouter();
  const [settings, setSettings] = useState(initial);
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<DurationFormValues>({ defaultValues: toFormValues(initial) });

  async function onSubmit(values: DurationFormValues) {
    setSubmitError(null);

    const minMs = partsToMs({ days: values.minDays, hours: values.minHours, minutes: values.minMinutes });
    const maxMs = partsToMs({ days: values.maxDays, hours: values.maxHours, minutes: values.maxMinutes });

    if (minMs <= 0) {
      setSubmitError({ title: "Invalid minimum", detail: "The minimum duration must be positive." });
      return;
    }
    if (minMs >= maxMs) {
      setSubmitError({ title: "Invalid range", detail: "The minimum duration must be less than the maximum." });
      return;
    }

    const result = await updateDurationSettings({
      minDuration: formatMsAsTimeSpan(minMs),
      maxDuration: formatMsAsTimeSpan(maxMs),
    });

    if (!result.success) {
      setSubmitError(result.error);
      toastActionError(result.error);
      return;
    }

    setSettings(result.data);
    toastSuccess("Duration settings updated.");
    router.refresh();
  }

  return (
    <div className="max-w-2xl space-y-6">
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-6" noValidate>
        <fieldset className="space-y-3 rounded-lg border border-slate-200 p-4">
          <legend className="px-1 text-sm font-semibold text-slate-900">Minimum duration</legend>
          <div className="grid grid-cols-3 gap-3">
            <div>
              <Label htmlFor="minDays">Days</Label>
              <TextInput
                id="minDays"
                type="number"
                disabled={isSubmitting}
                {...register("minDays", { valueAsNumber: true, min: 0 })}
              />
            </div>
            <div>
              <Label htmlFor="minHours">Hours</Label>
              <TextInput
                id="minHours"
                type="number"
                disabled={isSubmitting}
                {...register("minHours", { valueAsNumber: true, min: 0, max: 23 })}
              />
            </div>
            <div>
              <Label htmlFor="minMinutes">Minutes</Label>
              <TextInput
                id="minMinutes"
                type="number"
                disabled={isSubmitting}
                {...register("minMinutes", { valueAsNumber: true, min: 0, max: 59 })}
              />
            </div>
          </div>
        </fieldset>

        <fieldset className="space-y-3 rounded-lg border border-slate-200 p-4">
          <legend className="px-1 text-sm font-semibold text-slate-900">Maximum duration</legend>
          <div className="grid grid-cols-3 gap-3">
            <div>
              <Label htmlFor="maxDays">Days</Label>
              <TextInput
                id="maxDays"
                type="number"
                disabled={isSubmitting}
                {...register("maxDays", { valueAsNumber: true, min: 0 })}
              />
            </div>
            <div>
              <Label htmlFor="maxHours">Hours</Label>
              <TextInput
                id="maxHours"
                type="number"
                disabled={isSubmitting}
                {...register("maxHours", { valueAsNumber: true, min: 0, max: 23 })}
              />
            </div>
            <div>
              <Label htmlFor="maxMinutes">Minutes</Label>
              <TextInput
                id="maxMinutes"
                type="number"
                disabled={isSubmitting}
                {...register("maxMinutes", { valueAsNumber: true, min: 0, max: 59 })}
              />
            </div>
          </div>
        </fieldset>

        {(errors.minDays || errors.maxDays) && (
          <HelperText className="text-red-600">Every field must be zero or a positive whole number.</HelperText>
        )}

        {submitError && (
          <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
            <p className="text-sm font-semibold text-red-700">{submitError.title}</p>
            {submitError.detail && <p className="text-sm text-red-600">{submitError.detail}</p>}
          </div>
        )}

        <Button type="submit" color="primary" disabled={isSubmitting}>
          {isSubmitting ? "Saving…" : "Save settings"}
        </Button>
      </form>

      <p className="text-sm text-slate-500">
        {settings.updatedBy && settings.updatedAt
          ? `Last updated by ${settings.updatedBy} on ${new Date(settings.updatedAt).toLocaleString()}.`
          : "Using the platform's configured defaults -- never overridden by an admin."}
      </p>
    </div>
  );
}
