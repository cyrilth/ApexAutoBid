/**
 * Payload shapes pushed over NotificationHub (`/notifications`, Phase 6 Tasks 3/4 --
 * `components/NotificationProvider.tsx`, `hooks/useLiveBids.ts`). SignalR's default JSON hub
 * protocol uses System.Text.Json's standard camelCase policy (NotificationService/Program.cs
 * registers no `AddJsonProtocol` override), so these mirror `backend/Contracts/*.cs`
 * field-for-field, just camelCased -- the same convention every REST DTO mirror in
 * `types/*.ts` already follows.
 *
 * `Contracts.BidPlaced` needs no interface of its own here -- once camelCased, its fields
 * (Id, AuctionId, Bidder, BidTime, Amount, BidStatus) are already IDENTICAL to `types/bid.ts`'s
 * `Bid` shape, so `hooks/useLiveBids.ts` types the "BidPlaced" event payload as a plain `Bid`.
 */

/** Mirrors `Contracts.AuctionCreated` -- the "AuctionCreated" broadcast (`Clients.All`). */
export interface AuctionCreatedPayload {
  id: string;
  createdAt: string;
  updatedAt: string;
  auctionEnd: string;
  seller: string;
  winner: string;
  make: string;
  model: string;
  year: number;
  color: string;
  mileage: number;
  imageUrl: string;
  thumbnailUrl?: string | null;
  status: string;
  reservePrice: number;
  soldAmount?: number | null;
  currentHighBid?: number | null;
}

/**
 * Mirrors `Contracts.AuctionFinished` -- the "AuctionFinished" broadcast (`Clients.All`) AND
 * the "AuctionWon"/"AuctionSellerResult" targeted sends (`Clients.User`); all three carry the
 * exact same shape (`NotificationService.Consumers.AuctionFinishedConsumer` forwards the one
 * MassTransit message object to all three `SendAsync` calls).
 *
 * `winnerEmail` is deliberately never READ anywhere this type is used, even though the field
 * is present on the wire -- Requirements §13.5 forbids surfacing email addresses outside the
 * post-sale contact flow, which the frontend gets from `GET api/auctions/{id}`
 * (`components/PostSaleContact.tsx`) instead, never SignalR. Kept in this interface (rather
 * than omitted) purely so it's typed if anyone ever needs to double-check that -- accessing it
 * is not itself the violation, displaying/logging it would be.
 */
export interface AuctionFinishedPayload {
  itemSold: boolean;
  auctionId: string;
  winner?: string | null;
  winnerEmail?: string | null;
  seller: string;
  amount?: number | null;
}
