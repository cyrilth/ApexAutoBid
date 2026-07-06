using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using BiddingService.Infrastructure.Protos;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace BiddingService.Infrastructure.Grpc;

/// <summary>
/// Decorates <see cref="LocalAuctionProvider"/> with the gRPC fallback described in
/// Requirements §3.3 (Phase 5 Tasks 6/7): local Mongo first; on a local miss, calls the
/// Auction Service's <c>Auctions.GetAuction</c> rpc (<c>Protos/auctions.proto</c> — copied
/// verbatim from <c>AuctionService.API</c>'s server-side contract, see that file's remarks),
/// persists the fetched auction into this service's own local store so the NEXT lookup for the
/// same auction is a local hit, and returns it. A gRPC <see cref="StatusCode.NotFound"/> (the
/// auction genuinely doesn't exist on the Auction Service either) surfaces as
/// <see langword="null"/>, which <c>BidAppService.PlaceBidAsync</c> already turns into
/// <see cref="BidOutcome.AuctionNotFound"/> (404 — Requirements §3.3) with no change needed
/// there. Any other <see cref="RpcException"/>/<see cref="System.Net.Http.HttpRequestException"/>
/// (after the resilience pipeline's retries are exhausted — see Program.cs's
/// <c>AddResilienceHandler</c> on the underlying <c>Auctions.AuctionsClient</c> HttpClient)
/// propagates up to <c>GlobalExceptionHandler</c> as an ordinary 500 ProblemDetails
/// (Requirements §13.1) — a persistently unreachable Auction Service is not the same outcome
/// as "this specific auction doesn't exist".
/// </summary>
/// <remarks>
/// <para>
/// Depends on the CONCRETE <see cref="LocalAuctionProvider"/> type, not <see cref="IAuctionProvider"/>
/// — Program.cs registers <c>IAuctionProvider → GrpcFallbackAuctionProvider</c>, overriding
/// <c>ApplicationServiceExtensions</c>'s own <c>IAuctionProvider → LocalAuctionProvider</c>
/// registration (.NET's DI container resolves the LAST-registered descriptor for a
/// non-enumerable service type — the same pattern AuctionService.API's
/// <c>LoggingAuthorizationMiddlewareResultHandler</c> registration relies on). A constructor
/// parameter typed <see cref="IAuctionProvider"/> would therefore resolve to this very class
/// and recurse infinitely; requesting the concrete type instead (registered as its own scoped
/// service in Program.cs, alongside the override) sidesteps that without any keyed-service
/// plumbing.
/// </para>
/// <para>
/// Resilience (retry with backoff on transient gRPC/HTTP failures, logged at Warning per
/// Requirements §13.5's log-level table) is applied one layer down, on the
/// <c>Auctions.AuctionsClient</c>'s own <c>HttpClient</c> pipeline (Program.cs's
/// <c>AddResilienceHandler</c>) — this class only ever sees the final outcome (a response, or
/// an exception once retries are exhausted), and has no retry logic of its own.
/// </para>
/// </remarks>
public class GrpcFallbackAuctionProvider(
    LocalAuctionProvider inner,
    Auctions.AuctionsClient grpcClient,
    IAuctionRepository repository,
    ILogger<GrpcFallbackAuctionProvider> logger) : IAuctionProvider
{
    public async Task<Auction?> GetAuctionAsync(Guid auctionId, CancellationToken cancellationToken)
    {
        var local = await inner.GetAuctionAsync(auctionId, cancellationToken);
        if (local is not null)
            return local;

        logger.LogInformation(
            "Auction {AuctionId} not found locally — falling back to gRPC GetAuction", auctionId);

        GetAuctionResponse response;
        try
        {
            response = await grpcClient.GetAuctionAsync(
                new GetAuctionRequest { Id = auctionId.ToString() },
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            logger.LogWarning(
                "gRPC fallback: auction {AuctionId} was not found on the Auction Service either",
                auctionId);
            return null;
        }

        var auction = new Auction
        {
            Id = Guid.Parse(response.Id),
            Seller = response.Seller,
            ReservePrice = response.ReservePrice,
            AuctionEnd = response.AuctionEnd.ToDateTime(),
            // A non-"Live" status fetched fresh from the Auction Service means the auction is
            // already decided there (Finished/ReserveNotMet/Cancelled) even though the
            // AuctionCreated event that would normally carry that locally hadn't been consumed
            // yet — treat it as already finished in the local projection too, so
            // BidAppService's own Finished-or-past-AuctionEnd check can't let a stray bid
            // through against an auction the Auction Service itself already closed out.
            Finished = response.Status != "Live"
        };

        await repository.InsertIfNotExistsAsync(auction, cancellationToken);

        logger.LogInformation(
            "Fetched auction {AuctionId} via gRPC fallback and persisted it locally", auctionId);

        return auction;
    }
}
