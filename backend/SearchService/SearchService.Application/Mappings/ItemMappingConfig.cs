using Contracts;
using Mapster;
using SearchService.Application.DTOs;
using SearchService.Domain.Entities;

namespace SearchService.Application.Mappings;

/// <summary>
/// Mapster <see cref="IRegister"/> configuration for mappings involving Domain
/// <see cref="Item"/> — both inbound (Contract → Item, for the event consumers) and outbound
/// (Item → <see cref="ItemDto"/>, for <c>SearchAppService</c>). Registered by
/// <c>ApplicationServiceExtensions.AddApplicationServices</c> against a fresh, DI-scoped
/// <see cref="TypeAdapterConfig"/> — see <c>AuctionMappingConfig</c>'s remarks for why the
/// ambient <c>.Adapt&lt;T&gt;()</c> extension is not used here.
/// </summary>
public class ItemMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // ── AuctionCreated → Item ────────────────────────────────────────────────
        //
        // Every field matches by name and type by convention (Id, CreatedAt, UpdatedAt,
        // AuctionEnd, Seller, Make, Model, Year, Color, Mileage, ImageUrl, ThumbnailUrl,
        // Status, ReservePrice, SoldAmount, CurrentHighBid) except Winner: AuctionService
        // always publishes Winner = "" for a brand-new auction (AuctionCreated.Winner is a
        // non-nullable string on the event contract), but Item.Winner is string? —
        // normalize empty to null so the search index never carries a phantom winner for a
        // still-live auction (carried forward from the Task 3 code review).

        config.NewConfig<AuctionCreated, Item>()
            .Map(dest => dest.Winner,
                src => string.IsNullOrEmpty(src.Winner) ? null : src.Winner);

        // ── Item → ItemDto (Phase 2 Task 5) ──────────────────────────────────────
        //
        // Every field matches by name and type by convention (see ItemDto's XML doc — it's
        // a deliberate field-for-field mirror of Item), so this rule adds no .Map()/.Ignore()
        // calls. Registered explicitly anyway to match this codebase's convention of listing
        // every type pair through an IRegister even when the mapping is convention-only —
        // see rule 1 of AuctionMappingConfig (ItemImage ↔ ImageDto) for the same pattern.

        config.NewConfig<Item, ItemDto>();

        // ── AuctionSyncDto → Item (Phase 2 Task 6 HTTP polling fallback) ─────────
        //
        // This is DataSyncService's "full document replace" reconciliation path (see its XML
        // doc's "Backstop rationale") — maps EVERY Item field from the upstream AuctionDto
        // wire shape (Id, CreatedAt, UpdatedAt, AuctionEnd, Seller, Make, Model, Year, Color,
        // Mileage, Status, ReservePrice, SoldAmount, CurrentHighBid all match by name/type),
        // not just a subset, so the sync can actually repair CurrentHighBid/Status/Winner
        // drift, not merely base item fields.
        //
        // Winner: normalized empty→null, identical rule to AuctionCreated→Item above.
        //
        // ImageUrl/ThumbnailUrl: AuctionSyncDto carries the full gallery (a cross-service
        // wire payload) but Item stores only the primary image, so both are derived here by
        // re-deriving the lowest-SortOrder image rather than trusting Images[0] blindly (see
        // AuctionSyncDto's XML doc) — same "primary image" semantics as AuctionCreated's flat
        // ImageUrl/ThumbnailUrl fields.

        config.NewConfig<AuctionSyncDto, Item>()
            .Map(dest => dest.Winner,
                src => string.IsNullOrEmpty(src.Winner) ? null : src.Winner)
            .Map(dest => dest.ImageUrl,
                src => src.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
                    ?? string.Empty)
            .Map(dest => dest.ThumbnailUrl,
                src => src.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl).FirstOrDefault());
    }
}
