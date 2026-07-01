using AuctionService.Domain.Entities;
using AuctionService.Domain.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Infrastructure.Data;

public class AuctionDbContext(DbContextOptions<AuctionDbContext> options) : DbContext(options)
{
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Auction>(auction =>
        {
            auction.HasKey(a => a.Id);

            // Required strings → NOT NULL columns
            auction.Property(a => a.Seller).IsRequired();
            auction.Property(a => a.SellerEmail).IsRequired();

            // Status stored as its string name (e.g. "Live") rather than an integer.
            // Human-readable in the DB, safe against enum reordering, and trivially
            // queryable by operators without a lookup table.
            auction.Property(a => a.Status)
                .HasConversion<string>()
                .IsRequired();

            // One-to-one: Auction → Item
            // Item is the dependent end (owns AuctionId FK).
            // Cascade delete: removing an Auction removes its Item.
            auction.HasOne(a => a.Item)
                .WithOne(i => i.Auction)
                .HasForeignKey<Item>(i => i.AuctionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Item>(item =>
        {
            item.HasKey(i => i.Id);

            item.Property(i => i.Make).IsRequired();
            item.Property(i => i.Model).IsRequired();
            item.Property(i => i.Color).IsRequired();

            // One-to-many: Item → ItemImage
            // Cascade delete: removing an Item removes all its images.
            item.HasMany(i => i.Images)
                .WithOne(img => img.Item)
                .HasForeignKey(img => img.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemImage>(image =>
        {
            image.HasKey(img => img.Id);

            image.Property(img => img.Url).IsRequired();

            // Unique index on (ItemId, SortOrder) so gallery ordering is unambiguous
            // and the application cannot accidentally assign the same SortOrder twice
            // for the same item (which would break primary-image selection at SortOrder 0).
            image.HasIndex(img => new { img.ItemId, img.SortOrder })
                .IsUnique();
        });

        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.HasKey(a => a.Id);

            audit.Property(a => a.Actor).IsRequired();
            audit.Property(a => a.Action).IsRequired();
            audit.Property(a => a.EntityType).IsRequired();
            audit.Property(a => a.EntityId).IsRequired();

            // JSON payload summary stored as text — no fixed schema, never queried directly.
            audit.Property(a => a.Data).IsRequired();
        });

        // MassTransit transactional Outbox / Inbox tables.
        // These are managed by MassTransit and must live in the same DbContext so
        // they participate in the same database transaction as domain writes.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
