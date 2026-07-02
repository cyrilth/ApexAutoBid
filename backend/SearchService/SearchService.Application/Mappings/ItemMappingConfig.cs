using Contracts;
using Mapster;
using SearchService.Domain.Entities;

namespace SearchService.Application.Mappings;

/// <summary>
/// Mapster <see cref="IRegister"/> configuration for Contract → Domain <see cref="Item"/>
/// mappings used by the event consumers. Registered by
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
    }
}
