"use client";

import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { Button, Label, TextInput } from "flowbite-react";
import { useBidStore } from "@/components/BidStoreProvider";
import { placeBid } from "@/lib/bid-actions";
import { signInReturningTo } from "@/lib/auth-actions";
import { formatCurrency } from "@/lib/format";
import { toastActionError, toastError, toastSuccess, toastWarning } from "@/lib/toast";
import type { BidStatus } from "@/types/bid";

interface BidPanelProps {
  auctionId: string;
  /** `auction.status === "Live"` (computed by the Server Component page). */
  isLive: boolean;
  /** `auction.currentHighBid` at the time of the page's server render. */
  initialHighBid: number | null;
  isSignedIn: boolean;
  /** True only for the auction's own seller -- the backend 400s this too (Requirements §3.3), this just avoids dangling a form in front of them. */
  isSeller: boolean;
  isEmailVerified: boolean;
}

interface BidFormValues {
  amount: number;
}

const ACCEPTED_STATUSES: ReadonlySet<BidStatus> = new Set(["Accepted", "AcceptedBelowReserve"]);

/**
 * Bid input + submit (Task 8.1). Client Component: needs `react-hook-form`,
 * the Zustand bid store, and interactive submit/loading state, all of which
 * require the browser.
 *
 * Must render inside a `BidStoreProvider` (see `app/auctions/[id]/page.tsx`)
 * -- shares that store with `BidHistory` so a just-placed bid appears at
 * the top of the history instantly, and derives its own "current high bid"
 * guidance live from the same store (see `highBid` below), which is also
 * exactly the hook point the Task 8.2/8.3 SignalR follow-up needs: once
 * that run adds a listener calling this same store's `prependBid` for other
 * bidders' bids, this panel's minimum-bid guidance updates live too, with
 * no changes needed here.
 */
export function BidPanel({ auctionId, isLive, initialHighBid, isSignedIn, isSeller, isEmailVerified }: BidPanelProps) {
  const bids = useBidStore((state) => state.bids);
  const prependBid = useBidStore((state) => state.prependBid);

  // Live current-high-bid guidance: starts from the server-rendered value,
  // then tracks whatever the store has seen since (this bidder's own
  // Accepted/AcceptedBelowReserve bids today; every other bidder's via
  // SignalR once Task 8.2/8.3 lands). UX guidance only -- the backend is
  // the sole authority on what actually counts (Requirements §3.3).
  const highBid = useMemo(() => {
    const accepted = bids.filter((bid) => ACCEPTED_STATUSES.has(bid.bidStatus)).map((bid) => bid.amount);
    return accepted.length === 0 ? initialHighBid : Math.max(initialHighBid ?? 0, ...accepted);
  }, [bids, initialHighBid]);

  const minBid = (highBid ?? 0) + 1;

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<BidFormValues>({ defaultValues: { amount: minBid } });

  async function onSubmit(values: BidFormValues) {
    const result = await placeBid(auctionId, Math.trunc(values.amount));

    if (!result.success) {
      toastActionError(result.error);
      return;
    }

    const bid = result.data;
    // The backend records every outcome -- TooLow and Finished bids
    // included -- so GET api/bids/{auctionId} would return this bid
    // regardless of outcome; prepend it here too so the visible history
    // never falls out of sync with what a refetch would show.
    prependBid(bid);
    // For Accepted/AcceptedBelowReserve, bid.amount + 1 is the new real
    // minimum. For TooLow/Finished, this bid never raised the high --
    // Math.max keeps the field at the previously-known minimum instead of
    // dropping it to a value that would just fail validation again.
    reset({ amount: Math.max(minBid, bid.amount + 1) });

    switch (bid.bidStatus) {
      case "Accepted":
        toastSuccess(`Bid accepted -- you're the high bidder at ${formatCurrency(bid.amount)}.`);
        break;
      case "AcceptedBelowReserve":
        toastWarning(`Bid recorded at ${formatCurrency(bid.amount)}, but it's still below the reserve price.`);
        break;
      case "TooLow":
        toastError("Someone already bid higher -- try a higher amount.");
        break;
      case "Finished":
        toastError("This auction has already ended.");
        break;
    }
  }

  if (!isLive) {
    return (
      <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
        <p className="text-sm font-medium text-slate-600">This auction is no longer accepting bids.</p>
      </div>
    );
  }

  if (!isSignedIn) {
    return (
      <div className="space-y-2 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
        <p className="text-sm text-slate-600">Sign in to place a bid on this auction.</p>
        {/* Bound Server Action -- signInReturningTo(callbackUrl) -- so IdentityServer
            sends the browser straight back to this exact auction page after login. */}
        <form action={signInReturningTo.bind(null, `/auctions/${auctionId}`)}>
          <Button type="submit" color="primary" size="sm">
            Sign in to bid
          </Button>
        </form>
      </div>
    );
  }

  if (isSeller) {
    return (
      <div className="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
        <p className="text-sm font-medium text-slate-600">You&apos;re the seller of this auction -- you can&apos;t place a bid on it.</p>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-3" noValidate>
      {isEmailVerified === false && (
        <p className="text-sm text-amber-700">
          Verify your email address to place a bid -- check your inbox for the confirmation link.
        </p>
      )}

      <div>
        <Label htmlFor="amount">Your bid</Label>
        <div className="flex items-start gap-2">
          <TextInput
            id="amount"
            type="number"
            step={1}
            color={errors.amount ? "failure" : undefined}
            disabled={isSubmitting}
            className="flex-1"
            {...register("amount", {
              required: "Enter a bid amount.",
              valueAsNumber: true,
              min: { value: minBid, message: `Bid must be at least ${formatCurrency(minBid)}.` },
              validate: (value) => Number.isInteger(value) || "Bids are whole dollar amounts.",
            })}
          />
          <Button type="submit" color="primary" disabled={isSubmitting}>
            {isSubmitting ? "Placing…" : "Place bid"}
          </Button>
        </div>
        <p className="mt-1 text-sm text-slate-500">Minimum bid: {formatCurrency(minBid)}</p>
        {errors.amount && <p className="mt-1 text-sm text-red-600">{errors.amount.message}</p>}
      </div>
    </form>
  );
}
