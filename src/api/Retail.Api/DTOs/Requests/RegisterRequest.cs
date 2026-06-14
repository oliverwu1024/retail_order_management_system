namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Customer self-signup payload (REQUIREMENTS §1.1). Validated by
/// <c>RegisterRequestValidator</c> before it reaches the service.
/// </summary>
/// <param name="Email">Login email; must be globally unique.</param>
/// <param name="Password">Plaintext password over the wire (TLS) — never stored; Identity hashes it.</param>
/// <param name="DisplayName">User-facing name shown in the UI.</param>
public sealed record RegisterRequest(string Email, string Password, string DisplayName);
