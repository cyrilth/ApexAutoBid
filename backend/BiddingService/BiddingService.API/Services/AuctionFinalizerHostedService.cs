using BiddingService.Application.Services;

namespace BiddingService.API.Services;

/// <summary>
/// Ticks <see cref="IAuctionFinalizationService.FinalizeExpiredAuctionsAsync"/> on a fixed
/// interval (Phase 5 Task 12), configurable via <c>Bidding:FinalizationIntervalSeconds</c>
/// (env var form <c>Bidding__FinalizationIntervalSeconds</c> — Requirements §3.3, default
/// <b>10</b> seconds so short dev auctions finalize promptly after ending). This class is
/// deliberately "dumb": it owns only the timer and per-tick DI scope — every actual
/// business decision (which auctions are due, who won, whether the item sold) lives in the
/// Application layer (<c>AuctionFinalizationAppService</c>), per Clean Architecture's
/// component-placement table ("Program.cs, Dockerfile" is the only thing that belongs to
/// API here beyond registering this class).
/// </summary>
/// <remarks>
/// <b>Scoped DI per tick:</b> a single <see cref="IServiceScope"/> is created for each
/// <see cref="PeriodicTimer"/> tick and disposed immediately after — <c>IAuctionRepository</c>/
/// <c>IBidRepository</c>/<c>IAuctionFinalizationUnitOfWork</c> are all scoped services (Mongo
/// bus-outbox transactions in particular are inherently scoped), so a long-lived singleton
/// scope would be wrong here, mirroring how every HTTP request gets its own scope.
/// <para>
/// <b>Survives transient errors:</b> the <c>do</c>/<c>while</c> loop awaits
/// <see cref="PeriodicTimer.WaitForNextTickAsync"/> ONLY after the previous tick's body has
/// fully completed (including any exception having been caught below) — ticks can never
/// overlap, so an auction can never be selected by two concurrent finalization passes. A
/// failing tick (e.g. Mongo/RabbitMQ transiently unavailable) is logged and the loop simply
/// waits for the next interval to retry, rather than crashing the whole host — the same
/// resilience contract <c>AuctionFinalizationAppService</c> already applies per-auction
/// applies here per-tick, as a backstop for failures happening before any single auction is
/// even reached (e.g. the initial repository query itself failing).
/// </para>
/// </remarks>
public class AuctionFinalizerHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AuctionFinalizerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue("Bidding:FinalizationIntervalSeconds", 10);
        var interval = TimeSpan.FromSeconds(intervalSeconds);

        logger.LogInformation(
            "Auction finalizer background service starting — interval {IntervalSeconds}s",
            intervalSeconds);

        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var finalizationService = scope.ServiceProvider.GetRequiredService<IAuctionFinalizationService>();
                await finalizationService.FinalizeExpiredAuctionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Auction finalizer tick failed — will retry next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
