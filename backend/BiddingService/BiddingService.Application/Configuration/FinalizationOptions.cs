using System.ComponentModel.DataAnnotations;
using BiddingService.Domain.Entities;

namespace BiddingService.Application.Configuration;

/// <summary>
/// Binds the <c>Bidding</c> configuration section's finalization-grace-period setting
/// (phase-end code review Critical 2). Bound in Infrastructure's
/// <c>InfrastructureServiceExtensions</c> (mirrors <c>AuctionService.Application.Configuration.ImagesOptions</c>'
/// identical "options class lives in Application, binding happens in Infrastructure" split), and
/// validated at startup in the API's <c>Program.cs</c> (mirrors that same service's
/// <c>ValidateDataAnnotations().ValidateOnStart()</c> convention).
/// </summary>
public class FinalizationOptions
{
    /// <summary>Configuration section name bound from <c>appsettings*.json</c> — shared with
    /// the unrelated, ad hoc-read <c>Bidding:FinalizationIntervalSeconds</c>
    /// (<c>AuctionFinalizerHostedService</c>) key, since both describe the same background
    /// finalizer.</summary>
    public const string SectionName = "Bidding";

    /// <summary>
    /// Seconds of grace added past <see cref="Auction.AuctionEnd"/> before an
    /// auction becomes eligible for finalization: eligibility is
    /// <c>AuctionEnd + FinalizationGraceSeconds &lt;= now</c>, not bare <c>AuctionEnd &lt;= now</c>
    /// (<c>AuctionFinalizationAppService.FinalizeExpiredAuctionsAsync</c> computes the
    /// equivalent <c>AuctionEnd &lt;= now - FinalizationGraceSeconds</c> cutoff it passes to
    /// <c>IAuctionRepository.GetExpiredUnfinalizedAsync</c>).
    /// <para>
    /// <b>Why this exists (Critical 2):</b> a bid placed legitimately right at
    /// <c>AuctionEnd</c> still has to complete <c>BidPlacementUnitOfWork</c>'s own Mongo
    /// bus-outbox transaction before it's visible to
    /// <c>IBidRepository.GetHighestAcceptedBidAsync</c>. Without this grace window, the
    /// finalizer could select and permanently finalize the auction (atomically — see
    /// <c>IAuctionFinalizationUnitOfWork</c>'s remarks) in the narrow gap before that bid
    /// commits, silently losing a bid that should have won.
    /// </para>
    /// <para>
    /// Default 10s — comfortably longer than that commit/publish round-trip takes under normal
    /// load (this service's own live-verified bus-outbox mechanics — see
    /// <c>BidPlacementUnitOfWork</c>'s remarks), without meaningfully delaying finalization of
    /// the overwhelming majority of auctions, which receive no bids in their final seconds at
    /// all.
    /// </para>
    /// </summary>
    [Range(0, 3600)]
    public int FinalizationGraceSeconds { get; init; } = 10;
}
