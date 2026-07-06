using System.Text.Json.Serialization;

namespace AuctionService.Application.DTOs;

/// <summary>
/// Extends <see cref="AuctionDto"/> with the two post-sale contact-exchange fields
/// (Requirements §3.1 / Tasks.md Phase 5 Task 19) — this is the "dedicated response path"
/// <see cref="AuctionDto"/>'s own remarks refer to. Returned ONLY by
/// <c>GET api/auctions/{id}</c> (<c>AuctionsController.GetAuctionById</c> /
/// <c>IAuctionService.GetAuctionByIdAsync</c>) — never by the list endpoint, events, or the
/// search projection, all of which keep using the plain <see cref="AuctionDto"/>.
/// <para>
/// Both fields default to <see langword="null"/>. <c>AuctionAppService.GetAuctionByIdAsync</c>
/// is the ONLY code that ever populates them, and only under the exact conditions in
/// Requirements §3.1: <see cref="WinnerEmail"/> is set only when the caller's <c>username</c>
/// claim equals the auction's <c>Seller</c>; <see cref="SellerEmail"/> is set only when it
/// equals the <c>Winner</c>; and even then, only once the auction is sold
/// (<c>Status = Finished</c> with a recorded <c>Winner</c>). Every other caller — anonymous, an
/// unrelated authenticated user, or the seller/winner asking for the "wrong" field — gets
/// neither.
/// </para>
/// <para>
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> omits each field from the JSON response
/// entirely rather than serializing a literal <c>null</c> — a deliberately stricter choice than
/// this DTO hierarchy's other nullable fields (e.g. <see cref="AuctionDto.Winner"/>,
/// <see cref="AuctionDto.SoldAmount"/>), which serialize as JSON <c>null</c> when absent. These
/// two fields carry real PII, so omission (rather than a literal, always-present null) is
/// preferred here.
/// </para>
/// </summary>
public class AuctionDetailDto : AuctionDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SellerEmail { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerEmail { get; set; }
}
