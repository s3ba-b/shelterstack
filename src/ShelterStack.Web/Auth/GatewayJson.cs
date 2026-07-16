using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShelterStack.Web.Auth;

/// <summary>
/// The JSON options every Gateway call (de)serializes with. Enums cross the wire by name
/// ("Dog", "Female") because the Animals API serializes them that way, so the Web app must read
/// and write them the same way rather than as integers.
/// </summary>
public static class GatewayJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
