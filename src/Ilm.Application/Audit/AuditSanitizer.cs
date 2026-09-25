using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ilm.Application.Abstractions;

namespace Ilm.Application.Audit;

/// <summary>
/// Removes secrets from values before they are written to audit or logs. Redaction is by property name
/// and by value pattern (bearer tokens, JWTs, Okta SSWS tokens, private keys).
/// </summary>
public static partial class AuditSanitizer
{
    public const string Redacted = "[REDACTED]";

    private static readonly string[] SensitiveNameFragments =
    [
        "password", "passwd", "pwd", "secret", "token", "unicodepwd", "credential", "apikey", "api_key",
        "assertion", "privatekey", "private_key", "authorization", "cookie", "sessionid", "client_secret",
        "managedpassword", "ms-mcs-admpwd", "laps", "otp", "code_verifier",
    ];

    public static bool IsSensitiveName(string name)
    {
        var lower = name.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveNameFragments.Any(f => lower.Contains(f.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    public static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = BearerPattern().Replace(value, "Bearer " + Redacted);
        text = SswsPattern().Replace(text, "SSWS " + Redacted);
        text = JwtPattern().Replace(text, Redacted);
        text = PemPattern().Replace(text, Redacted);
        text = PasswordAssignmentPattern().Replace(text, m => m.Groups[1].Value + "=" + Redacted);
        return text;
    }

    /// <summary>Serialises a value to compact JSON with sensitive properties and values redacted.</summary>
    public static string? ToSafeJson(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return SanitizeText(s);
        }

        var node = JsonSerializer.SerializeToNode(value, value.GetType(), IlmJson.Compact);
        Scrub(node);
        return node?.ToJsonString(IlmJson.Compact);
    }

    private static void Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (IsSensitiveName(key))
                    {
                        obj[key] = Redacted;
                    }
                    else
                    {
                        Scrub(obj[key]);
                    }
                }

                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue v && v.TryGetValue<string>(out var str))
                    {
                        arr[i] = SanitizeText(str);
                    }
                    else
                    {
                        Scrub(arr[i]);
                    }
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                var clean = SanitizeText(text);
                if (!string.Equals(clean, text, StringComparison.Ordinal))
                {
                    value.ReplaceWith(clean);
                }

                break;
        }
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"SSWS\s+[A-Za-z0-9\-_]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex SswsPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]*", RegexOptions.CultureInvariant, 200)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant, 200)]
    private static partial Regex PemPattern();

    [GeneratedRegex(@"\b(password|pwd|secret|client_secret|token)\s*=\s*[^;&\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex PasswordAssignmentPattern();
}
