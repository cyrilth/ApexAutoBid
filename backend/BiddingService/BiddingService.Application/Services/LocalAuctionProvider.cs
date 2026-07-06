using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;

namespace BiddingService.Application.Services;

/// <summary>
/// Default <see cref="IAuctionProvider"/> implementation — resolves purely against this
/// service's own local Mongo projection (<see cref="IAuctionRepository"/>), with no gRPC
/// fallback. See <see cref="IAuctionProvider"/>'s remarks for how the later gRPC-fallback run
/// composes with this without changing it.
/// </summary>
public class LocalAuctionProvider(IAuctionRepository repository) : IAuctionProvider
{
    public Task<Auction?> GetAuctionAsync(Guid auctionId, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(auctionId, cancellationToken);
}
