using Duende.IdentityModel;
using Duende.IdentityServer.Models;

namespace IdentityService;

public static class Config
{
    /// <summary>
    /// Non-standard custom claim type carrying the ASP.NET Identity username. Every backend
    /// service's JwtBearer config sets NameClaimType to this literal string (see
    /// AuctionService.API's Program.cs comment: "NameClaimType is set to 'username' so that
    /// User.Identity!.Name returns the username claim"). The standard OIDC equivalent would be
    /// preferred_username, but the rest of the platform is already built against "username", so
    /// this constant — not a string literal — is what ProfileService.cs and the UserClaims list
    /// below both reference, to keep them from drifting out of sync.
    /// </summary>
    public const string UsernameClaimType = "username";

    /// <summary>
    /// Single platform-wide API scope/resource covering all ApexAutoBid backend services
    /// (Auction, Search, Bidding). Requirements.md never defines separate per-service scopes —
    /// every backend endpoint only needs "is this a valid ApexAutoBid access token" plus the
    /// role claim for admin gating (Requirements.md §10) — so one combined scope is what the
    /// docs actually call for, not per-service scopes invented ahead of need. The name matches
    /// the "apexautobid" audience already used for ad-hoc dev-JWT testing before this service
    /// existed (see the now-superseded dev-jwt-recipe memory).
    /// </summary>
    public const string ApiScopeName = "apexautobid";

    public static IEnumerable<IdentityResource> IdentityResources =>
        new IdentityResource[]
        {
            new IdentityResources.OpenId(),
            new IdentityResources.Profile(),
        };

    public static IEnumerable<ApiScope> ApiScopes =>
        new ApiScope[]
        {
            new ApiScope(ApiScopeName, "ApexAutoBid backend services"),
        };

    public static IEnumerable<ApiResource> ApiResources =>
        new ApiResource[]
        {
            new ApiResource(ApiScopeName, "ApexAutoBid backend services")
            {
                Scopes = { ApiScopeName },
                // Claims added to the ACCESS token when this resource's scope is requested.
                // ProfileService.GetProfileDataAsync (Services/ProfileService.cs) only issues
                // claims that are actually present in context.RequestedClaimTypes, and Duende
                // populates RequestedClaimTypes from the UserClaims declared on the
                // resources/scopes tied to the request — so these four must be listed here
                // (Requirements.md §3.4: username, email, role; email_verified is additionally
                // required by AuctionService.API's existing email-verified policy — see the
                // doc-alignment note in this task's report).
                UserClaims =
                {
                    UsernameClaimType,
                    JwtClaimTypes.Email,
                    JwtClaimTypes.EmailVerified,
                    JwtClaimTypes.Role,
                }
            }
        };

    public static IEnumerable<Client> Clients =>
        new Client[]
        {
            // Requirements.md §3.4 "IdentityServer Clients": "Next.js web app (authorization
            // code flow via next-auth)". next-auth's OIDC provider exchanges the authorization
            // code server-side (inside the Next.js server, never in the browser), so this is a
            // CONFIDENTIAL client with a secret — unlike the `scalar` public/PKCE client, which
            // Tasks.md assigns to a separate later task (13.1) and is deliberately NOT added
            // here. ClientId/RedirectUris are a judgment call: the frontend (Phase 7) doesn't
            // exist yet, so these are provisional dev defaults (the "webapp" name matches
            // Architecture.md's docker-compose/k8s naming) — revisit once the frontend's actual
            // next-auth provider config is written.
            new Client
            {
                ClientId = "webapp",
                ClientName = "ApexAutoBid Web App",
                // Dev-only, committable secret (Requirements.md §6) — not an external-provider
                // credential, so it's fine to commit like the MinIO/RabbitMQ dev credentials
                // elsewhere in the repo.
                ClientSecrets = { new Secret("webapp-dev-secret".Sha256()) },

                AllowedGrantTypes = GrantTypes.Code,
                // Duende defaults RequirePkce to true already; set explicitly for readability.
                RequirePkce = true,

                RedirectUris = { "http://localhost:3000/api/auth/callback/identityserver" },
                FrontChannelLogoutUri = "http://localhost:3000/signout-oidc",
                PostLogoutRedirectUris = { "http://localhost:3000" },

                // Lets a signed-in session refresh its access token without forcing the user
                // back through the login page — a reasonable default for a persistent web app
                // session; Requirements.md doesn't mandate it either way.
                AllowOfflineAccess = true,
                // Rotate the refresh token on every use instead of Duende's default ReUse
                // (same handle valid for the full 30-day absolute lifetime). Rotation limits
                // how long a leaked handle stays usable and makes reuse-after-rotation
                // detectable as a compromise signal.
                RefreshTokenUsage = TokenUsage.OneTimeOnly,
                AllowedScopes =
                {
                    "openid",
                    "profile",
                    "offline_access",
                    ApiScopeName,
                }
            },

            // Requirements.md §3.4 "IdentityServer Clients": "scalar — API docs pages
            // (authorization code + PKCE, public client without secret); requires CORS on the
            // token endpoint for browser-based code exchange" (Phase 3 Task 13).
            //
            // PUBLIC client: RequireClientSecret = false (Duende defaults this to true, so it
            // must be explicitly disabled) — the Scalar UI runs entirely in the browser with no
            // backend to keep a secret confidential, so PKCE (required below) is the only
            // credential, exactly like a SPA/native app client.
            //
            // Redirect URI is a judgment call, resolved via decompilation, not guesswork:
            // Scalar.AspNetCore 2.16.7's own OAuth2 flow (decompiled/decompressed its embedded
            // scalar.js bundle) defaults the redirect to the CURRENT page's own URL
            // (window.location.origin + window.location.pathname) if none is configured — but
            // that default is ambiguous here, since /scalar, /scalar/, and /scalar/v1 are all
            // URLs a user could land on depending on trailing-slash/document-name routing. To
            // remove that ambiguity, AuctionService.API's Program.cs pins an explicit, fixed
            // redirect URI via ScalarOptions.AddAuthorizationCodeFlow(...).WithRedirectUri(...)
            // — each value here must match one of those exactly. AuctionService's own docs page
            // (port 5054) and the Gateway's aggregated docs page (Phase 4 Task 7.3, port 6001 —
            // GatewayService's Program.cs) both run the same login against this one client, so
            // both redirect URIs are listed; a future Search/Bidding-hosted-standalone docs page
            // would add a third entry the same way. Like the `webapp` client, these dev URLs are
            // provisional — Phase 8/9 containerization may change the hosts.
            new Client
            {
                ClientId = "scalar",
                ClientName = "ApexAutoBid API Docs (Scalar)",
                RequireClientSecret = false,

                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,

                RedirectUris = { "http://localhost:5054/scalar", "http://localhost:6001/scalar" },

                // Task 13.2 / Phase 4 Task 7.3: Duende's InMemoryCorsPolicyService (swapped in
                // automatically by AddInMemoryClients — verified via decompilation) allows an
                // origin for /connect/token (and discovery/userinfo/revocation —
                // CorsOptions.CorsPaths' documented defaults) if ANY registered client lists it
                // in AllowedCorsOrigins. UseIdentityServer() already calls app.ConfigureCors()
                // internally (also verified via decompilation), so no ASP.NET Core CORS
                // middleware/policy needs to be registered anywhere — this property is the
                // entire mechanism. Both origins are listed for the same reason both redirect
                // URIs are above — the browser-based code exchange can originate from either
                // docs page.
                AllowedCorsOrigins = { "http://localhost:5054", "http://localhost:6001" },

                // No offline_access: a docs page doesn't need a long-lived session — each
                // "Authorize" click is expected to mint a fresh token, unlike the webapp's
                // persistent user session. Like ClientId and RedirectUris above, this scope
                // list must match AuctionService.API Program.cs's WithSelectedScopes exactly
                // (no shared constant is possible across independently deployable services).
                AllowedScopes = { "openid", "profile", ApiScopeName },
            },
        };
}
