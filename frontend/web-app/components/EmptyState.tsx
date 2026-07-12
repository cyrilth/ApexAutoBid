import Link from "next/link";
import { Button } from "flowbite-react";
import { CarIcon } from "@/components/icons/CarIcon";

interface EmptyStateProps {
  message: string;
  ctaLabel?: string;
  ctaHref?: string;
}

/** Centered icon + one-line message + relevant CTA (Docs/DesignGuide.md §6). */
export function EmptyState({ message, ctaLabel, ctaHref }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-lg border border-slate-200 bg-white py-16 text-center">
      <CarIcon className="h-12 w-12 text-slate-300" />
      <p className="text-sm text-slate-500">{message}</p>
      {ctaLabel && ctaHref && (
        <Button as={Link} href={ctaHref} color="primary">
          {ctaLabel}
        </Button>
      )}
    </div>
  );
}
