/**
 * Request/response shapes for the Phase 11 admin API (`/admin/*`), mirroring the backend DTOs
 * field-for-field (ASP.NET Core's default System.Text.Json camelCase policy):
 *
 *  - backend/IdentityService/Dtos/Admin/*.cs (users)
 *  - backend/AuctionService/AuctionService.Application/DTOs/{Banner,PlatformSettings,DurationLimits,AuctionStats}*.cs
 *  - backend/BiddingService/BiddingService.Application/DTOs/BidStatsDto.cs
 *
 * `TimeSpan` fields (MinDuration/MaxDuration) serialize with .NET 8+'s built-in constant
 * ("c") format -- `"[-][d.]hh:mm:ss[.fffffff]"`, e.g. `"01:00:00"` or `"90.00:00:00"` -- so
 * they're typed as plain strings here, not parsed/re-serialized.
 */

// ── Identity Service: api/admin/users ───────────────────────────────────────

/** Mirrors IdentityService.Dtos.Admin.UserListItemDto. */
export interface AdminUserListItem {
  id: string;
  userName: string;
  email?: string | null;
  emailConfirmed: boolean;
  lockedOut: boolean;
  lockoutEnd?: string | null;
  roles: string[];
}

/** Mirrors IdentityService.Dtos.Admin.UserListResultDto -- `GET api/admin/users` response. */
export interface AdminUserListResult {
  items: AdminUserListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Mirrors IdentityService.Dtos.Admin.UserStatsDto -- `GET api/admin/users/stats` response. */
export interface AdminUserStats {
  total: number;
  confirmed: number;
  locked: number;
}

/** Mirrors IdentityService.Dtos.Admin.CreateUserRequestDto -- `POST api/admin/users` body. */
export interface CreateUserPayload {
  userName: string;
  email: string;
  password: string;
  preConfirmed: boolean;
}

/** Mirrors IdentityService.Dtos.Admin.CreateUserResponseDto. */
export interface CreateUserResult {
  id: string;
  userName: string;
  email?: string | null;
  emailConfirmed: boolean;
}

/** Mirrors IdentityService.Dtos.Admin.ResetPasswordRequestDto -- `POST api/admin/users/{id}/reset-password` body. */
export interface ResetPasswordPayload {
  sendResetLink: boolean;
  newPassword?: string;
}

/** Mirrors IdentityService.Dtos.Admin.ResetPasswordResponseDto. */
export interface ResetPasswordResult {
  linkSent: boolean;
  /** Only present when `sendResetLink` was `false` -- shown to the admin exactly once. */
  temporaryPassword?: string;
}

/** Mirrors IdentityService.Dtos.Admin.RolesUpdateResponseDto -- `PUT api/admin/users/{id}/roles` response. */
export interface RolesUpdateResult {
  id: string;
  roles: string[];
}

/** Mirrors IdentityService.Dtos.Admin.LockResponseDto -- `PUT api/admin/users/{id}/lock` response. */
export interface LockUserResult {
  id: string;
  lockedOut: boolean;
  lockoutEnd?: string | null;
}

// ── Auction Service: api/admin/auctions, api/admin/banners, api/admin/settings ─────────────

/** Mirrors AuctionService.Application.DTOs.AuctionStatsDto -- `GET api/admin/auctions/stats` response. */
export interface AdminAuctionStats {
  total: number;
  /** Always carries every Status enum value ("Live"/"Finished"/"ReserveNotMet"/"Cancelled"), 0 when absent. */
  byStatus: Record<string, number>;
}

/** Mirrors AuctionService.Domain.Enums.BannerScope's string values. */
export type BannerScope = "Global" | "HomePage" | "Auction";

/** Mirrors AuctionService.Application.DTOs.BannerDto. */
export interface Banner {
  id: string;
  message: string;
  scope: BannerScope;
  auctionId?: string | null;
  /** ISO 8601 UTC instant. */
  activeFrom: string;
  /** ISO 8601 UTC instant. */
  activeUntil: string;
  createdBy: string;
}

/** Mirrors AuctionService.Application.DTOs.CreateBannerDto / UpdateBannerDto -- both are a full replace. */
export interface BannerPayload {
  message: string;
  scope: BannerScope;
  auctionId?: string | null;
  activeFrom: string;
  activeUntil: string;
}

/** Mirrors AuctionService.Application.DTOs.PlatformSettingsDto -- `GET api/admin/settings/duration` response. */
export interface DurationSettings {
  minDuration: string;
  maxDuration: string;
  updatedBy?: string | null;
  updatedAt?: string | null;
}

/** Mirrors AuctionService.Application.DTOs.UpdateDurationSettingsDto -- `PUT api/admin/settings/duration` body. */
export interface DurationSettingsPayload {
  minDuration: string;
  maxDuration: string;
}

/** Mirrors AuctionService.Application.DTOs.DurationLimitsDto -- the anon `GET api/auctions/duration-limits` response. */
export interface DurationLimits {
  minDuration: string;
  maxDuration: string;
}

// ── Bidding Service: api/admin/bids ──────────────────────────────────────────

/** Mirrors BiddingService.Application.DTOs.BidStatsDto -- `GET api/admin/bids/stats` response. */
export interface AdminBidStats {
  totalBids: number;
}
