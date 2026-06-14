namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Login payload (REQUIREMENTS §1.1). On success the tokens come back as
/// Set-Cookie headers, never in the response body.
/// </summary>
/// <param name="Email">Login email.</param>
/// <param name="Password">Plaintext password over the wire (TLS); checked against the stored hash.</param>
public sealed record LoginRequest(string Email, string Password);
