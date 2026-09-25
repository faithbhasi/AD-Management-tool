using System.Globalization;
using System.Text;

namespace Ilm.Infrastructure.Directory.Ldap;

/// <summary>
/// Builds LDAP filters with RFC 4515 escaping. User input only ever enters a filter through
/// <see cref="Escape"/> or <see cref="EscapeBinary"/>; there is no API that accepts a raw filter fragment.
/// </summary>
public static class LdapFilter
{
    public const string InChainRule = "1.2.840.113556.1.4.1941";

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\5c"); break;
                case '*': sb.Append(@"\2a"); break;
                case '(': sb.Append(@"\28"); break;
                case ')': sb.Append(@"\29"); break;
                case '\0': sb.Append(@"\00"); break;
                default:
                    if (c < 0x20 || c == 0x7f)
                    {
                        sb.Append('\\').Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    public static string EscapeBinary(ReadOnlySpan<byte> value)
    {
        var sb = new StringBuilder(value.Length * 3);
        foreach (var b in value)
        {
            sb.Append('\\').Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    public static string Equal(string attribute, string value) => $"({ValidateAttribute(attribute)}={Escape(value)})";

    public static string Prefix(string attribute, string value) => $"({ValidateAttribute(attribute)}={Escape(value)}*)";

    public static string And(params string[] parts) => "(&" + string.Concat(parts) + ")";

    public static string Or(params string[] parts) => "(|" + string.Concat(parts) + ")";

    /// <summary>Recursive membership: groups whose member chain contains <paramref name="memberDn"/>.</summary>
    public static string GroupsContainingInChain(string memberDn) => $"(member:{InChainRule}:={Escape(memberDn)})";

    public static string ObjectGuid(Guid guid) => $"(objectGUID={EscapeBinary(guid.ToByteArray())})";

    private static string ValidateAttribute(string attribute)
    {
        if (string.IsNullOrEmpty(attribute) || !attribute.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new ArgumentException("Invalid LDAP attribute name.", nameof(attribute));
        }

        return attribute;
    }
}

/// <summary>RFC 4514 escaping for a single RDN value (used only when composing a new object's CN).</summary>
public static class LdapDn
{
    public static string EscapeRdnValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var leading = i == 0 && (c == ' ' || c == '#');
            var trailing = i == value.Length - 1 && c == ' ';
            if (leading || trailing || c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=')
            {
                sb.Append('\\').Append(c);
            }
            else if (c < 0x20)
            {
                sb.Append('\\').Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public static string FromDnsName(string dnsName) =>
        string.Join(",", dnsName.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => "DC=" + EscapeRdnValue(p)));

    public static string GuidReference(Guid guid) => $"<GUID={guid:D}>";

    public static string SidReference(string sid)
    {
        if (!sid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) || !sid.All(c => char.IsAsciiDigit(c) || c is '-' or 'S' or 's'))
        {
            throw new ArgumentException("Invalid SID.", nameof(sid));
        }

        return $"<SID={sid}>";
    }
}
