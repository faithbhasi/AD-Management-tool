using System.Text.Json;
using Ilm.Application.Abstractions;
using Ilm.Domain.Common;
using Ilm.Domain.Configuration;

namespace Ilm.Application.Configuration;

public static class ConfigurationSerializer
{
    public static string Serialize(IlmConfigurationDocument document) =>
        JsonSerializer.Serialize(document, IlmJson.Indented);

    /// <summary>Parses a proposed document. Unknown properties are rejected rather than ignored.</summary>
    public static IlmConfigurationDocument Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IlmConfigurationDocument>(json, IlmJson.Indented)
                ?? throw new DomainException(SafeErrorCategory.ValidationFailed, "The configuration document is empty.");
        }
        catch (JsonException ex)
        {
            throw new DomainException(SafeErrorCategory.ValidationFailed, "The configuration document is not valid: " + ex.Message);
        }
    }

    public static string ContentHash(IlmConfigurationDocument document) =>
        Hashing.Sha256Hex(JsonSerializer.Serialize(document, IlmJson.Compact));
}
