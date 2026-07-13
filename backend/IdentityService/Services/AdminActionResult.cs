namespace IdentityService.Services;

public enum AdminActionStatus
{
    Success,
    NotFound,
    ValidationFailed,
}

/// <summary>
/// Small result wrapper returned by <see cref="IAdminUserService"/>'s mutating methods so
/// <see cref="Controllers.AdminUsersController"/> can translate a not-found/validation-failed
/// outcome into the right HTTP status (404/400) without the service throwing for expected,
/// user-triggerable outcomes (e.g. an unknown user id, a duplicate username).
/// </summary>
public class AdminActionResult<T>
{
    public required AdminActionStatus Status { get; init; }
    public T? Value { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static AdminActionResult<T> Ok(T value) =>
        new() { Status = AdminActionStatus.Success, Value = value };

    public static AdminActionResult<T> NotFound() =>
        new() { Status = AdminActionStatus.NotFound };

    public static AdminActionResult<T> Invalid(IEnumerable<string> errors) =>
        new() { Status = AdminActionStatus.ValidationFailed, Errors = [.. errors] };
}
