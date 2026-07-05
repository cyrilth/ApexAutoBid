namespace IdentityService.Services;

/// <summary>
/// Why <see cref="ExternalLoginProvisioningService"/> refused to auto-provision a first-time
/// external-login visitor. Kept as a closed, small enum (rather than a free-text reason string)
/// specifically so Pages/ExternalLogin/Callback.cshtml.cs can round-trip it through a redirect
/// query string to the Login page and have Login map it to a fixed, safe, pre-written message —
/// never surfacing raw IdentityResult error text (which can embed the user's email — see
/// SeedData.cs's Phase 3 Task 14 landmine (b) comment for the same class of leak) to the browser
/// or to process logs.
/// </summary>
public enum ExternalLoginRejectedReason
{
    /// <summary>
    /// A local account already exists with the email the external provider asserted. Deliberately
    /// NOT auto-linked to that existing account (see ExternalLoginProvisioningService's remarks)
    /// — the user must sign in locally to prove ownership first.
    /// </summary>
    EmailAlreadyRegistered,
}

/// <summary>
/// Thrown by <see cref="ExternalLoginProvisioningService.ProvisionAsync"/> when a first-time
/// external login cannot be safely auto-provisioned. Caught by
/// Pages/ExternalLogin/Callback.cshtml.cs.
/// </summary>
public class ExternalLoginRejectedException(ExternalLoginRejectedReason reason) : Exception
{
    public ExternalLoginRejectedReason Reason { get; } = reason;
}
