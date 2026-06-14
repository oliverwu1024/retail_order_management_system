namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Payload to update the current customer's profile (REQUIREMENTS §1.2). Only
/// DisplayName + Phone are editable — Email is immutable in the MVP and lives on
/// the Identity user, so it is not part of this request.
/// </summary>
public sealed record UpsertProfileRequest(
    string DisplayName,
    string? Phone);
