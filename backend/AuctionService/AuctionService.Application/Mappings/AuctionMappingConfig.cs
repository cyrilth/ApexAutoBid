using AuctionService.Application.DTOs;
using AuctionService.Domain.Entities;
using Contracts;
using Mapster;

namespace AuctionService.Application.Mappings;

/// <summary>
/// Mapster <see cref="IRegister"/> configuration for all Auction-related type pairs.
/// Registered automatically via <c>TypeAdapterConfig.GlobalSettings.Scan(...)</c>
/// called from <c>ApplicationServiceExtensions.AddApplicationServices</c>.
/// </summary>
public class AuctionMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // ── 1. ItemImage ↔ ImageDto ──────────────────────────────────────────────
        //
        // All property names match (Url, ThumbnailUrl, SortOrder) so the forward
        // mapping is convention-only. The reverse is also symmetric; it is used when
        // constructing a new ItemImage from an ImageDto during create/update flows.
        // ItemImage.Item and ItemImage.ItemId are EF Core navigation members —
        // deliberately excluded from the reverse mapping.

        config.NewConfig<ItemImage, ImageDto>();

        config.NewConfig<ImageDto, ItemImage>()
            .Ignore(dest => dest.ItemId)
            .Ignore(dest => dest.Item);

        // ── 2. Auction → AuctionDto ──────────────────────────────────────────────
        //
        // Item fields are flattened explicitly (Make, Model, Year, Color, Mileage)
        // from the nested Item navigation.
        //
        // Status: mapped as the enum name string so API consumers are decoupled
        // from the server-side enum definition (matches event-contract convention).
        //
        // Images: projected from Item.Images ordered by SortOrder ascending so that
        // index 0 in the output list always carries the primary image (SortOrder=0).
        //
        // Privacy: SellerEmail and WinnerEmail are not present on AuctionDto — they
        // are never mapped here. No explicit Ignore() is needed because Mapster only
        // maps to properties that exist on the destination type.

        config.NewConfig<Auction, AuctionDto>()
            .Map(dest => dest.Make, src => src.Item.Make)
            .Map(dest => dest.Model, src => src.Item.Model)
            .Map(dest => dest.Year, src => src.Item.Year)
            .Map(dest => dest.Color, src => src.Item.Color)
            .Map(dest => dest.Mileage, src => src.Item.Mileage)
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.Images,
                src => src.Item.Images
                    .OrderBy(img => img.SortOrder)
                    .Select(img => img.Adapt<ImageDto>())
                    .ToList());

        // ── 3. CreateAuctionDto → Auction ────────────────────────────────────────
        //
        // Auction has two C# `required` members absent from the DTO: Seller and
        // SellerEmail. These are set by the service layer at create-time (Seller from
        // the JWT username claim, SellerEmail from the email claim). ConstructUsing
        // satisfies the required-member constraint with string.Empty placeholders;
        // the service layer MUST overwrite them before saving.
        //
        // The nested Item is produced by a separate CreateAuctionDto → Item mapping
        // (rule 3b) and assigned via Map below.
        //
        // Fields managed by the service layer at create-time (Id, CreatedAt,
        // UpdatedAt, Status, Winner, WinnerEmail, SoldAmount, CurrentHighBid) are
        // not on the DTO and are left to their entity defaults.

        config.NewConfig<CreateAuctionDto, Auction>()
            .ConstructUsing(src => new Auction
            {
                // Placeholder values — the service layer overwrites these before save.
                Seller = string.Empty,
                SellerEmail = string.Empty,
                Item = null!        // assigned immediately below via .Map
            })
            .Map(dest => dest.ReservePrice, src => src.ReservePrice)
            .Map(dest => dest.AuctionEnd, src => src.AuctionEnd)
            .Map(dest => dest.Item, src => src.Adapt<Item>())
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.Seller)
            .Ignore(dest => dest.SellerEmail)
            .Ignore(dest => dest.Status)
            .Ignore(dest => dest.Winner!)       // nullable string? — ! suppresses CS8603
            .Ignore(dest => dest.WinnerEmail!)  // nullable string?
            .Ignore(dest => dest.SoldAmount!)   // nullable int?
            .Ignore(dest => dest.CurrentHighBid!) // nullable int?
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt);

        // ── 3b. CreateAuctionDto → Item ──────────────────────────────────────────
        //
        // Item has three C# `required` string members: Make, Model, Color — all
        // present on the DTO by the same names and mapped by convention.
        //
        // Images: each ImageDto is mapped to an ItemImage via the ImageDto → ItemImage
        // rule (rule 1 reverse). SortOrder is preserved because it flows through
        // ImageDto.SortOrder → ItemImage.SortOrder by name convention.
        //
        // Navigation members (AuctionId, Auction) are set by EF Core after the entity
        // graph is attached — explicitly ignored here.

        config.NewConfig<CreateAuctionDto, Item>()
            .Map(dest => dest.Make, src => src.Make)
            .Map(dest => dest.Model, src => src.Model)
            .Map(dest => dest.Color, src => src.Color)
            .Map(dest => dest.Year, src => src.Year)
            .Map(dest => dest.Mileage, src => src.Mileage)
            .Map(dest => dest.Images,
                src => src.Images
                    .Select(img => img.Adapt<ItemImage>())
                    .ToList())
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.AuctionId)
            .Ignore(dest => dest.Auction);

        // ── 4. UpdateAuctionDto → Auction ────────────────────────────────────────
        //
        // Partial-update semantics: IgnoreNullValues(true) means only non-null DTO
        // properties overwrite the existing entity. The controller fetches the tracked
        // Auction entity, calls adapter.Map(dto, auction), and EF Core's change
        // tracker detects only the modified columns.
        //
        // Item fields: applied separately via UpdateAuctionDto → Item (rule 4b) on
        // the tracked Item entity so EF Core can detect per-column changes.
        //
        // Images: UpdateAuctionDto.Images is not mapped here because gallery
        // replacement is a wholesale DELETE + INSERT that cannot be expressed as a
        // naive property copy. The service layer checks dto.Images != null and
        // performs the swap explicitly against the DbContext. The Item navigation
        // itself is also ignored here — the service maps dto → item directly (4b).

        config.NewConfig<UpdateAuctionDto, Auction>()
            .IgnoreNullValues(true)
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.Seller)
            .Ignore(dest => dest.SellerEmail)
            .Ignore(dest => dest.Status)
            .Ignore(dest => dest.Winner!)       // nullable string?
            .Ignore(dest => dest.WinnerEmail!)  // nullable string?
            .Ignore(dest => dest.SoldAmount!)   // nullable int?
            .Ignore(dest => dest.CurrentHighBid!) // nullable int?
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.AuctionEnd)
            .Ignore(dest => dest.ReservePrice)
            .Ignore(dest => dest.Item);

        // ── 4b. UpdateAuctionDto → Item ──────────────────────────────────────────
        //
        // Same partial-update semantics. Item's `required` string members (Make,
        // Model, Color) are guarded by IgnoreNullValues(true) — a null DTO field
        // will leave the existing entity value in place, so the required-member
        // guarantee on the tracked entity is never violated.
        //
        // Images: intentionally ignored. The gallery swap (delete all existing
        // ItemImage rows then insert the new list) is service-layer logic triggered
        // when UpdateAuctionDto.Images != null; the mapping layer never touches it.

        config.NewConfig<UpdateAuctionDto, Item>()
            .IgnoreNullValues(true)
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.AuctionId)
            .Ignore(dest => dest.Auction)
            .Ignore(dest => dest.Images);   // gallery swap is service-layer logic

        // ── 5. AuctionDto → AuctionCreated ──────────────────────────────────────
        //
        // Most fields map by name (Id, CreatedAt, UpdatedAt, AuctionEnd, Seller,
        // Make, Model, Year, Color, Mileage, Status, ReservePrice, SoldAmount,
        // CurrentHighBid). Three require explicit expressions:
        //
        //   Winner:       AuctionDto.Winner is string? — the event record requires
        //                 a non-nullable string, so null collapses to string.Empty.
        //   ImageUrl:     Projected from the primary image (index 0 of the already-
        //                 ordered Images list). Empty string when gallery is empty.
        //   ThumbnailUrl: Projected from the same primary image; null when absent.

        config.NewConfig<AuctionDto, AuctionCreated>()
            .Map(dest => dest.Winner, src => src.Winner ?? string.Empty)
            .Map(dest => dest.ImageUrl, src => src.Images.Count > 0 ? src.Images[0].Url : string.Empty)
            .Map(dest => dest.ThumbnailUrl, src => src.Images.Count > 0 ? src.Images[0].ThumbnailUrl : null);

        // ── 6. AuctionDto → AuctionUpdated ──────────────────────────────────────
        //
        // Id on the event is string (vs Guid on AuctionDto) — mapped explicitly.
        // ImageUrl / ThumbnailUrl follow the same primary-image projection as above.
        // AuctionEnd (DateTime → DateTime?) is an implicit widening; convention handles it.
        // Make, Model, Color, Mileage, Year all map by name.

        config.NewConfig<AuctionDto, AuctionUpdated>()
            .Map(dest => dest.Id, src => src.Id.ToString())
            .Map(dest => dest.ImageUrl, src => src.Images.Count > 0 ? src.Images[0].Url : string.Empty)
            .Map(dest => dest.ThumbnailUrl, src => src.Images.Count > 0 ? src.Images[0].ThumbnailUrl : null);
    }
}
