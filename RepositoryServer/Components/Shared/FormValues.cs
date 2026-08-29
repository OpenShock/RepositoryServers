using System.Diagnostics.CodeAnalysis;

namespace OpenShock.RepositoryServer.Components.Shared;

/// <summary>
/// Parsing shared by the admin forms. Text inputs hand back strings, and the services take the real
/// types, so every form has the same two conversions to do.
/// </summary>
public static class FormValues
{
    private const int Sha256Length = 32;

    /// <summary>
    /// Parses an optional absolute URL. An empty field is a URL the record does not have, which is a
    /// different thing from a malformed one, so it succeeds with null rather than failing.
    /// </summary>
    public static bool TryParseUri(string? raw, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out uri);
    }

    /// <summary>
    /// Parses a SHA-256 digest written as hex, which is how every tool that prints one writes it.
    /// </summary>
    public static bool TryParseSha256(string? raw, [NotNullWhen(true)] out byte[]? hash)
    {
        hash = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        if (trimmed.Length != Sha256Length * 2) return false;

        // Checked before converting because Convert.FromHexString throws on a bad character, and a
        // mistyped digest is an ordinary thing for a form to be handed.
        foreach (var character in trimmed)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }

        hash = Convert.FromHexString(trimmed);
        return true;
    }

    /// <summary>Renders a digest the way it is entered and the way tools print it.</summary>
    public static string ToHex(byte[] hash) => Convert.ToHexStringLower(hash);
}
