using BiddingService.Application.DTOs;
using BiddingService.Domain.Entities;
using Contracts;
using Mapster;

namespace BiddingService.Application.Mappings;

/// <summary>
/// Mapster <see cref="IRegister"/> configuration for all Bidding-related type pairs.
/// Registered by <c>ApplicationServiceExtensions.AddApplicationServices</c> against a fresh,
/// DI-scoped <see cref="TypeAdapterConfig"/> (NOT the static <c>TypeAdapterConfig.GlobalSettings</c>)
/// — mirrors <c>AuctionService</c>/<c>SearchService</c>'s identical convention; see
/// <c>AuctionMappingConfig</c>'s remarks for why the ambient <c>.Adapt&lt;T&gt;()</c> extension
/// must not be used here.
/// </summary>
public class BidMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // ── 1. Bid → BidDto ───────────────────────────────────────────────────────
        //
        // Id, AuctionId, Bidder, BidTime, Amount all match by name/type. BidStatus is mapped
        // explicitly (enum → string) rather than relied on for automatic conversion, matching
        // AuctionMappingConfig's identical explicit-enum-to-string style for Auction.Status.
        // BidderEmail has no counterpart on BidDto and is therefore never mapped — Requirements
        // §3.3: never returned by any bids API response.

        config.NewConfig<Bid, BidDto>()
            .Map(dest => dest.BidStatus, src => src.BidStatus.ToString());

        // ── 2. Bid → Contracts.BidPlaced ──────────────────────────────────────────
        //
        // Id and AuctionId are Guid on the Domain entity but string on the wire contract
        // (Contracts/BidPlaced.cs) — explicit conversions required. Bidder/BidTime/Amount
        // match by name/type. BidStatus is the enum's name (e.g. "Accepted",
        // "AcceptedBelowReserve") — the exact strings AuctionService's/SearchService's
        // BidPlacedConsumer already compare against.

        config.NewConfig<Bid, BidPlaced>()
            .Map(dest => dest.Id, src => src.Id.ToString())
            .Map(dest => dest.AuctionId, src => src.AuctionId.ToString())
            .Map(dest => dest.BidStatus, src => src.BidStatus.ToString());

        // ── 3. AuctionCreated → local Auction projection ─────────────────────────
        //
        // Id, AuctionEnd, Seller, ReservePrice all match by name/type (Requirements §3.3's
        // Auction.cs (local) model). Finished has no source counterpart and keeps the
        // destination's default (false) — a freshly-consumed AuctionCreated auction is never
        // already finished. Registered explicitly (even though convention-only) to match this
        // codebase's IRegister convention — see ItemMappingConfig's rule 2 for the same pattern.

        config.NewConfig<AuctionCreated, Auction>();
    }
}
