using System.Security.Cryptography;
using System.Text;

namespace Ilm.Application.Abstractions;

public static class Hashing
{
    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Sha256Hex(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
