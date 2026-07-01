using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuctionService.Infrastructure.Data;

/// <summary>
/// Handles database migration and seed data on application startup.
/// Call <c>await DbInitializer.InitDbAsync(app.Services)</c> from the API's
/// <c>Program.cs</c> immediately after <c>var app = builder.Build();</c>.
/// </summary>
public static class DbInitializer
{
    public static async Task InitDbAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AuctionDbContext>>();

        // NOTE: applying migrations against a not-yet-ready database (common when
        // the container starts before Postgres) currently throws and exits. A
        // startup retry policy is added with the Docker Compose work (Phase 1
        // Task 13/17), where service start-ordering actually matters.
        logger.LogInformation("Applying database migrations");
        await context.Database.MigrateAsync();

        await SeedDataAsync(context, config, logger);
    }

    private static async Task SeedDataAsync(
        AuctionDbContext context,
        IConfiguration config,
        ILogger logger)
    {
        if (await context.Auctions.AnyAsync())
        {
            logger.LogInformation("Auction seed data already present — skipping");
            return;
        }

        var baseUrl = config["Images:PublicBaseUrl"] ?? "http://localhost:9000";

        var auctions = new List<Auction>
        {
            // 1 – Ford GT (has a current high bid from seed bids)
            new()
            {
                ReservePrice = 20000,
                Seller = "bob",
                SellerEmail = "bob@apexautobid.local",
                CurrentHighBid = 18000,
                AuctionEnd = DateTime.UtcNow.AddDays(10),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Ford",
                    Model = "GT",
                    Color = "White",
                    Year = 2020,
                    Mileage = 50000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/ford-gt.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 2 – Bugatti Veyron
            new()
            {
                ReservePrice = 90000,
                Seller = "alice",
                SellerEmail = "alice@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(60),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Bugatti",
                    Model = "Veyron",
                    Color = "Black",
                    Year = 2018,
                    Mileage = 15000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/bugatti-veyron.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 3 – Ford Mustang (no reserve)
            new()
            {
                ReservePrice = 0,
                Seller = "bob",
                SellerEmail = "bob@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(4),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Ford",
                    Model = "Mustang",
                    Color = "Black",
                    Year = 2023,
                    Mileage = 65000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/ford-mustang.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 4 – Mercedes SLK (ended, reserve not met)
            new()
            {
                ReservePrice = 50000,
                Seller = "tom",
                SellerEmail = "tom@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(-1),
                Status = Status.ReserveNotMet,
                Item = new Item
                {
                    Make = "Mercedes",
                    Model = "SLK",
                    Color = "Silver",
                    Year = 2020,
                    Mileage = 15000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/mercedes-slk.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 5 – BMW X1
            new()
            {
                ReservePrice = 20000,
                Seller = "alice",
                SellerEmail = "alice@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(20),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "BMW",
                    Model = "X1",
                    Color = "White",
                    Year = 2017,
                    Mileage = 90000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/bmw-x1.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 6 – Ferrari Spider
            new()
            {
                ReservePrice = 20000,
                Seller = "bob",
                SellerEmail = "bob@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(45),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Ferrari",
                    Model = "Spider",
                    Color = "Red",
                    Year = 2015,
                    Mileage = 50000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/ferrari-spider.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 7 – Ferrari F-430
            new()
            {
                ReservePrice = 150000,
                Seller = "alice",
                SellerEmail = "alice@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(13),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Ferrari",
                    Model = "F-430",
                    Color = "Red",
                    Year = 2022,
                    Mileage = 5000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/ferrari-f430.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 8 – Audi R8 (no reserve)
            new()
            {
                ReservePrice = 0,
                Seller = "bob",
                SellerEmail = "bob@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddDays(30),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Audi",
                    Model = "R8",
                    Color = "White",
                    Year = 2021,
                    Mileage = 10000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/audi-r8.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 9 – Audi TT (ending in 6 hours)
            new()
            {
                ReservePrice = 20000,
                Seller = "tom",
                SellerEmail = "tom@apexautobid.local",
                AuctionEnd = DateTime.UtcNow.AddHours(6),
                Status = Status.Live,
                Item = new Item
                {
                    Make = "Audi",
                    Model = "TT",
                    Color = "Black",
                    Year = 2020,
                    Mileage = 25000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/audi-tt.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            },

            // 10 – Ford Model T (sold — Finished)
            new()
            {
                ReservePrice = 20000,
                Seller = "bob",
                SellerEmail = "bob@apexautobid.local",
                Winner = "alice",
                WinnerEmail = "alice@apexautobid.local",
                SoldAmount = 25000,
                AuctionEnd = DateTime.UtcNow.AddDays(-2),
                Status = Status.Finished,
                Item = new Item
                {
                    Make = "Ford",
                    Model = "Model T",
                    Color = "Rust",
                    Year = 1938,
                    Mileage = 150000,
                    Images =
                    [
                        new ItemImage
                        {
                            Url = $"{baseUrl}/auction-images/ford-model-t.jpg",
                            ThumbnailUrl = null,
                            SortOrder = 0
                        }
                    ]
                }
            }
        };

        try
        {
            context.Auctions.AddRange(auctions);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} auctions", auctions.Count);
        }
        catch (DbUpdateException ex)
        {
            // Another instance starting concurrently (rolling deploy / scale-up)
            // may have inserted the seed rows between the AnyAsync() check above
            // and this save, tripping the unique (ItemId, SortOrder) index. Treat
            // that as already-seeded rather than crashing the process.
            logger.LogWarning(ex,
                "Auction seed insert failed — assuming another instance seeded concurrently");
        }
    }
}
