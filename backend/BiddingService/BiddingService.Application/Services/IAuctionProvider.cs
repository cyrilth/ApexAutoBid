using BiddingService.Domain.Entities;

namespace BiddingService.Application.Services;

/// <summary>
/// Resolves the local <see cref="Auction"/> projection a bid is being placed against.
/// <para>
/// <b>The gRPC-fallback seam (Requirements §3.3 / Tasks 6–8, a later run):</b> this interface
/// exists specifically so <c>BidAppService</c>/<c>BidsController</c> never need to change when
/// the gRPC fallback is added. Today, <c>LocalAuctionProvider</c> (registered by
/// <c>ApplicationServiceExtensions.AddApplicationServices</c>) is the only implementation — it
/// simply queries <see cref="Domain.Interfaces.IAuctionRepository"/> and returns
/// <see langword="null"/> when the event hasn't been consumed yet, which
/// <c>BidAppService.PlaceBidAsync</c> currently surfaces as a 404 ("must exist locally ... —
/// otherwise 404", Requirements §3.3).
/// </para>
/// <para>
/// <b>Implemented (Phase 5 Tasks 6/7):</b> <c>BiddingService.Infrastructure.Grpc.GrpcFallbackAuctionProvider</c>
/// is an Infrastructure-layer decorator (Grpc.Net.ClientFactory + Microsoft.Extensions.Http.Resilience,
/// per the NuGet placement table) that tries the concrete <c>LocalAuctionProvider</c> first and,
/// only on a local miss, calls the Auction Service's gRPC <c>GetAuction</c> endpoint as a
/// fallback, persisting what it fetches so the next lookup is a local hit. Wired up with two
/// extra DI registrations in <c>Program.cs</c> (the composition root), overriding this
/// interface's registration below — a pure addition, no change to this interface,
/// <c>LocalAuctionProvider</c>, or any Application-layer consumer of it (see
/// <c>GrpcFallbackAuctionProvider</c>'s own remarks for exactly how that override resolves).
/// </para>
/// </summary>
public interface IAuctionProvider
{
    /// <summary>
    /// Returns the local projection of the auction with the given id, or
    /// <see langword="null"/> when it cannot be resolved (today: not yet synced from
    /// <c>AuctionCreated</c>; later: also not resolvable via the gRPC fallback).
    /// </summary>
    Task<Auction?> GetAuctionAsync(Guid auctionId, CancellationToken cancellationToken);
}
