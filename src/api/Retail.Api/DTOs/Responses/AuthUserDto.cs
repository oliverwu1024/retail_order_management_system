namespace Retail.Api.DTOs.Responses;

/// <summary>
/// The user profile returned by <c>register</c>, <c>login</c>, <c>refresh</c>, and
/// <c>me</c>. This is the ENTIRE success body — the tokens travel as cookies, so
/// the body deliberately carries no secret (ADR-0007).
/// </summary>
/// <param name="Id">Identity user id.</param>
/// <param name="Email">Login email.</param>
/// <param name="DisplayName">User-facing name.</param>
/// <param name="Roles">Roles the user holds; the SPA uses these to gate UI.</param>
public sealed record AuthUserDto(
    string Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);
