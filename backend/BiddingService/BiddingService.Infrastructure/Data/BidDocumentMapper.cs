using BiddingService.Domain.Entities;
using BiddingService.Domain.Enums;

namespace BiddingService.Infrastructure.Data;

/// <summary>
/// Explicit, hand-written <see cref="Bid"/> ↔ <see cref="BidDocument"/> conversion, shared by
/// <see cref="BidRepository"/> (reads) and <see cref="BidPlacementUnitOfWork"/> (the atomic
/// write). Not a Mapster mapping: unlike <c>SearchService</c>'s <c>ItemRepository</c> (whose
/// <c>Item</c>/<c>ItemDocument</c> pair matches field-for-field with no type mismatch — see its
/// XML doc), <c>Bid.BidStatus</c> is an enum while <c>BidDocument.BidStatus</c> is a string, so
/// this pair is not a pure by-convention copy Mapster's ambient <c>.Adapt&lt;T&gt;()</c> extension
/// could handle safely without a registered <c>TypeAdapterConfig</c> rule — and registering one
/// here would mean either adding a Mapster dependency to this Infrastructure-only, six-field
/// conversion, or reaching into the DI-scoped config that Infrastructure has no business
/// depending on (see <c>AuctionMappingConfig</c>'s remarks on why the ambient extension is
/// avoided). A manual conversion sidesteps both concerns.
/// </summary>
internal static class BidDocumentMapper
{
    public static BidDocument ToDocument(Bid bid) => new()
    {
        Id = bid.Id,
        AuctionId = bid.AuctionId,
        Bidder = bid.Bidder,
        BidderEmail = bid.BidderEmail,
        BidTime = bid.BidTime,
        Amount = bid.Amount,
        BidStatus = bid.BidStatus.ToString()
    };

    public static Bid ToDomain(BidDocument document) => new()
    {
        Id = document.Id,
        AuctionId = document.AuctionId,
        Bidder = document.Bidder,
        BidderEmail = document.BidderEmail,
        BidTime = document.BidTime,
        Amount = document.Amount,
        BidStatus = Enum.Parse<BidStatus>(document.BidStatus)
    };
}
