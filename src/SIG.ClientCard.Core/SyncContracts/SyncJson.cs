using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIG.ClientCard.Core.SyncContracts;

public static class SyncJson
{
    /// <summary>
    /// Wire format shared by outbox payloads and the pull bundle: snake_case
    /// properties matching Postgres column names, enums as snake_case strings.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}
