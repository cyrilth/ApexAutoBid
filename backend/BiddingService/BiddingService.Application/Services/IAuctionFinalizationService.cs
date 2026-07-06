namespace BiddingService.Application.Services;

/// <summary>
/// Application-level service driving one "tick" of the background auction finalizer (Phase 5
/// Task 12). The API-layer hosted service (<c>BiddingService.API/Services/AuctionFinalizerHostedService</c>)
/// resolves this from a fresh DI scope on its own timer and does nothing else — all of the
/// actual business logic (which auctions are due, who won, whether the item sold) lives here,
/// never in the hosted service itself.
/// </summary>
public interface IAuctionFinalizationService
{
    /// <summary>
    /// Finds every local auction past <c>AuctionEnd</c> that isn't yet finalized, determines
    /// each one's outcome, and finalizes it (Requirements §3.3). A failure finalizing one
    /// auction is logged and does not prevent the others found in the same call from being
    /// finalized; see <c>AuctionFinalizationAppService</c>'s remarks.
    /// </summary>
    Task FinalizeExpiredAuctionsAsync(CancellationToken cancellationToken);
}
