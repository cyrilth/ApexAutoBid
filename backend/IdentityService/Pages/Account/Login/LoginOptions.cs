namespace IdentityService.Pages.Account.Login;

public static class LoginOptions
{
    public static readonly bool AllowLocalLogin = true;
    public static readonly bool AllowRememberLogin = true;
    public static readonly TimeSpan RememberMeLoginDuration = TimeSpan.FromDays(30);
    public static readonly string InvalidCredentialsErrorMessage = "Invalid username or password";

    // Phase 3 Task 16.3 — deliberately distinct from InvalidCredentialsErrorMessage (see
    // Index.cshtml.cs's remarks on the enumeration-parity tradeoff this accepts). Doesn't name
    // the account or state how long the lockout lasts — HostingExtensions.cs's
    // Identity:Lockout:LockoutMinutes is a tuning knob, not something to bake into user-facing
    // copy that would need to change in lockstep with it.
    public static readonly string AccountLockedErrorMessage =
        "This account has been temporarily locked due to repeated failed sign-in attempts. Please try again later.";
}
