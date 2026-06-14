using System.Text.RegularExpressions;

namespace Retail.Api.Common.Helpers;

/// <summary>
/// Generates URL-safe slugs from free text (e.g. a product name → <c>"acme-running-shoe"</c>).
/// Used to seed the editable Slug field at create time; uniqueness is enforced by
/// the service + a filtered unique index.
/// </summary>
public static partial class Slug
{
    /// <summary>
    /// Lower-cases the input, collapses every run of non-alphanumeric characters to a
    /// single hyphen, and trims leading/trailing hyphens. Returns empty string for
    /// input that contains no alphanumerics.
    /// </summary>
    public static string From(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // CA1308: slugs are lower-case by web convention (URLs), so ToLowerInvariant
        // is the correct normalization here, not ToUpperInvariant.
#pragma warning disable CA1308
        string lower = input.Trim().ToLowerInvariant();
#pragma warning restore CA1308

        return NonSlugCharacters().Replace(lower, "-").Trim('-');
    }

    // One or more characters that are not ASCII lower-case letters or digits.
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();
}
