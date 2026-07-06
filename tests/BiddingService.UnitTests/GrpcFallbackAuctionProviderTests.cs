using BiddingService.Application.Services;
using BiddingService.Domain.Entities;
using BiddingService.Domain.Interfaces;
using BiddingService.Infrastructure.Grpc;
using BiddingService.Infrastructure.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BiddingService.UnitTests;

/// <summary>
/// Unit tests for <see cref="GrpcFallbackAuctionProvider"/> (Phase 5 Task 15.6). The generated
/// <see cref="Auctions.AuctionsClient"/> turns out to be cleanly mockable: it is a non-sealed
/// class with a protected parameterless constructor and <c>virtual</c> <c>GetAuctionAsync</c>
/// overloads — both deliberately present in the codegen specifically to support mocking
/// frameworks (the same Castle DynamicProxy mechanics NSubstitute itself uses), so
/// <c>Substitute.For&lt;Auctions.AuctionsClient&gt;()</c> works with no contortions. This lets
/// every branch of the decorator be exercised directly, not just the local-hit short-circuit:
/// local hit (no gRPC call at all), local miss + gRPC success (fetch-and-persist), and local
/// miss + gRPC <see cref="StatusCode.NotFound"/> (surfaces as <see langword="null"/>).
/// </summary>
public class GrpcFallbackAuctionProviderTests
{
    private static GrpcFallbackAuctionProvider BuildSut(
        LocalAuctionProvider inner, Auctions.AuctionsClient grpcClient, IAuctionRepository repository) =>
        new(inner, grpcClient, repository, NullLogger<GrpcFallbackAuctionProvider>.Instance);

    private static AsyncUnaryCall<GetAuctionResponse> SuccessfulCall(GetAuctionResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private static AsyncUnaryCall<GetAuctionResponse> FaultedCall(Exception exception) =>
        new(
            Task.FromException<GetAuctionResponse>(exception),
            Task.FromException<Metadata>(exception),
            () => new Status(StatusCode.Unknown, exception.Message),
            () => new Metadata(),
            () => { });

    // ── Local hit — short-circuits before ever touching the gRPC client ─────────

    [Fact]
    public async Task GetAuctionAsync_WhenLocalRepositoryHasTheAuction_ReturnsItWithoutCallingGrpcOrUpserting()
    {
        var id = Guid.NewGuid();
        var localAuction = new Auction
        {
            Id = id, Seller = "bob", ReservePrice = 20000,
            AuctionEnd = DateTime.UtcNow.AddDays(7), Finished = false
        };
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(localAuction);
        var inner = new LocalAuctionProvider(repository);
        var grpcClient = Substitute.For<Auctions.AuctionsClient>();
        var sut = BuildSut(inner, grpcClient, repository);

        var result = await sut.GetAuctionAsync(id, CancellationToken.None);

        Assert.Same(localAuction, result);
        // Discard, not await: AsyncUnaryCall<T> is itself awaitable, but this call's return
        // value is irrelevant — DidNotReceive() performs its assertion the moment it's invoked.
        _ = grpcClient.DidNotReceive().GetAuctionAsync(
            Arg.Any<GetAuctionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().InsertIfNotExistsAsync(Arg.Any<Auction>(), Arg.Any<CancellationToken>());
    }

    // ── Local miss + gRPC success — fetches, persists locally, and returns it ───

    [Fact]
    public async Task GetAuctionAsync_WhenLocalMissAndGrpcSucceedsWithLiveStatus_PersistsAndReturnsUnfinishedAuction()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Auction?)null);
        var inner = new LocalAuctionProvider(repository);
        var grpcClient = Substitute.For<Auctions.AuctionsClient>();

        var auctionEnd = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var response = new GetAuctionResponse
        {
            Id = id.ToString(),
            Seller = "bob",
            ReservePrice = 20000,
            AuctionEnd = Timestamp.FromDateTime(auctionEnd),
            Status = "Live"
        };
        grpcClient.GetAuctionAsync(
                Arg.Any<GetAuctionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulCall(response));

        var sut = BuildSut(inner, grpcClient, repository);

        var result = await sut.GetAuctionAsync(id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(id, result!.Id);
        Assert.Equal("bob", result.Seller);
        Assert.Equal(20000, result.ReservePrice);
        Assert.Equal(auctionEnd, result.AuctionEnd);
        Assert.False(result.Finished);

        await repository.Received(1).InsertIfNotExistsAsync(
            Arg.Is<Auction>(a => a.Id == id && !a.Finished), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAuctionAsync_WhenLocalMissAndGrpcReturnsNonLiveStatus_PersistsAsAlreadyFinished()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Auction?)null);
        var inner = new LocalAuctionProvider(repository);
        var grpcClient = Substitute.For<Auctions.AuctionsClient>();

        var response = new GetAuctionResponse
        {
            Id = id.ToString(),
            Seller = "bob",
            ReservePrice = 20000,
            AuctionEnd = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = "ReserveNotMet"
        };
        grpcClient.GetAuctionAsync(
                Arg.Any<GetAuctionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulCall(response));

        var sut = BuildSut(inner, grpcClient, repository);

        var result = await sut.GetAuctionAsync(id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Finished);
        await repository.Received(1).InsertIfNotExistsAsync(
            Arg.Is<Auction>(a => a.Finished), Arg.Any<CancellationToken>());
    }

    // ── Local miss + gRPC NotFound — surfaces as null, nothing persisted ────────

    [Fact]
    public async Task GetAuctionAsync_WhenLocalMissAndGrpcThrowsNotFound_ReturnsNullWithoutPersisting()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Auction?)null);
        var inner = new LocalAuctionProvider(repository);
        var grpcClient = Substitute.For<Auctions.AuctionsClient>();

        var notFound = new RpcException(new Status(StatusCode.NotFound, "auction not found"));
        grpcClient.GetAuctionAsync(
                Arg.Any<GetAuctionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(FaultedCall(notFound));

        var sut = BuildSut(inner, grpcClient, repository);

        var result = await sut.GetAuctionAsync(id, CancellationToken.None);

        Assert.Null(result);
        await repository.DidNotReceive().InsertIfNotExistsAsync(Arg.Any<Auction>(), Arg.Any<CancellationToken>());
    }

    // ── Any other RpcException is NOT swallowed — propagates to the caller ─────

    [Fact]
    public async Task GetAuctionAsync_WhenGrpcThrowsANonNotFoundRpcException_PropagatesIt()
    {
        var id = Guid.NewGuid();
        var repository = Substitute.For<IAuctionRepository>();
        repository.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Auction?)null);
        var inner = new LocalAuctionProvider(repository);
        var grpcClient = Substitute.For<Auctions.AuctionsClient>();

        var unavailable = new RpcException(new Status(StatusCode.Unavailable, "auction service unreachable"));
        grpcClient.GetAuctionAsync(
                Arg.Any<GetAuctionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(FaultedCall(unavailable));

        var sut = BuildSut(inner, grpcClient, repository);

        await Assert.ThrowsAsync<RpcException>(() => sut.GetAuctionAsync(id, CancellationToken.None));
    }
}
