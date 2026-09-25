using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Ilm.Infrastructure.Directory.Ldap;

/// <summary>Converts binary objectSid values to S-1-... strings without Windows-only APIs.</summary>
public static class SidConverter
{
    public static string ToSddlString(ReadOnlySpan<byte> sid)
    {
        if (sid.Length < 8)
        {
            throw new ArgumentException("SID is too short.", nameof(sid));
        }

        var revision = sid[0];
        var subAuthorityCount = sid[1];
        if (sid.Length != 8 + (4 * subAuthorityCount))
        {
            throw new ArgumentException("SID length does not match its sub-authority count.", nameof(sid));
        }

        ulong authority = 0;
        for (var i = 2; i < 8; i++)
        {
            authority = (authority << 8) | sid[i];
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"S-{revision}-{authority}");
        for (var i = 0; i < subAuthorityCount; i++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(sid.Slice(8 + (4 * i), 4));
            sb.Append('-').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
