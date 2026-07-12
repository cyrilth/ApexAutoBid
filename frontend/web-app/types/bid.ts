/** Mirrors BiddingService.Domain's BidStatus enum -- serialized as a string (Requirements §3.3). */
export type BidStatus = "Accepted" | "AcceptedBelowReserve" | "TooLow" | "Finished";

/**
 * Mirrors BiddingService.Application.DTOs.BidDto -- the `GET api/bids/{auctionId}`
 * response shape. Never carries `BidderEmail` -- the backend deliberately
 * never returns it from this endpoint (Requirements §3.3).
 */
export interface Bid {
  id: string;
  auctionId: string;
  bidder: string;
  bidTime: string;
  amount: number;
  bidStatus: BidStatus;
}
