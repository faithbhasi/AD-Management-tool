using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ilm.Application.Abstractions;

/// <summary>Shared JSON settings: enums as names, stable property order, no HTML escaping surprises.</summary>
public static class IlmJson
{
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
