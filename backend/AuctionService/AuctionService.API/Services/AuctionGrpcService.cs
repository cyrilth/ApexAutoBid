using AuctionService.API.Protos;
using AuctionService.Application.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace AuctionService.API.Services;

/// <summary>
/// gRPC server implementation of the <c>Auctions.GetAuction</c> rpc (Phase 5 Task 8,
/// Protos/auctions.proto) — the Bidding Service's fallback lookup for auction data when the
/// <c>AuctionCreated</c> event hasn't been consumed yet (Requirements.md §3.2/§3.3,
/// Architecture.md §3.2). Delegates through the Application layer's <see cref="IAuctionService"/>
/// exactly like <see cref="Controllers.AuctionsController"/> does — this class is API-layer
/// glue, not a second data-access path (Clean Architecture: API → Application → Domain).
/// <para>
/// <b>Anonymous by design</b>, mirroring <c>GET api/auctions/{id}</c>'s own anonymous access —
/// no auth requirement is attached to this rpc in Program.cs. The response carries only fields
/// already public via that same anonymous HTTP endpoint, MINUS the post-sale contact-exchange
/// emails (Requirements §3.1): <c>requestingUser: null</c> is passed to
/// <see cref="IAuctionService.GetAuctionByIdAsync"/> below specifically so
/// <c>SellerEmail</c>/<c>WinnerEmail</c> can never be populated on this path, regardless of the
/// auction's sale state. The proto contract itself also never declares those two fields at all
/// (Protos/auctions.proto's own remarks) — this is defense-in-depth, not the only safeguard.
/// Bid validation needs the seller's USERNAME (Requirements §3.3 — "the seller cannot bid on
/// their own auction"), never an email, so <see cref="GetAuctionResponse.Seller"/> is the
/// username claim value, unchanged from what <c>AuctionDto.Seller</c>/<c>Auction.Seller</c>
/// already carry.
/// </para>
/// </summary>
public class AuctionGrpcService(
    IAuctionService service,
    ILogger<AuctionGrpcService> logger) : Auctions.AuctionsBase
{
    public override async Task<GetAuctionResponse> GetAuction(GetAuctionRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            logger.LogWarning("GetAuction gRPC call had an unparsable id {AuctionId}", request.Id);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be a valid GUID"));
        }

        // requestingUser: null — see this class's own remarks. Never forward any caller
        // identity into this internal, anonymous rpc.
        var auction = await service.GetAuctionByIdAsync(id, requestingUser: null);
        if (auction is null)
        {
            logger.LogInformation("GetAuction gRPC call for {AuctionId} — not found", id);
            throw new RpcException(new Status(StatusCode.NotFound, $"Auction {id} not found"));
        }

        return new GetAuctionResponse
        {
            Id = auction.Id.ToString(),
            Seller = auction.Seller,
            ReservePrice = auction.ReservePrice,
            // Timestamp.FromDateTime requires Kind == Utc — AuctionEnd is always populated with
            // a UTC instant (seed data / CreateAuctionDto validation), but Npgsql returns
            // "timestamp without time zone" columns with Kind = Unspecified, so it must be
            // relabeled (not converted — SpecifyKind, not ToUniversalTime) before conversion,
            // mirroring AuctionsController.GetAllAuctions' own date-handling remarks.
            AuctionEnd = Timestamp.FromDateTime(DateTime.SpecifyKind(auction.AuctionEnd, DateTimeKind.Utc)),
            Status = auction.Status
        };
    }
}
