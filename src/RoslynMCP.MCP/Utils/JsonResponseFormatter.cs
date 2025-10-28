using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMCP.MCP.Utils;

/// <summary>
/// Utility for consistent JSON formatting across all MCP tools
/// </summary>
public static class JsonResponseFormatter
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Serialize an object to JSON string with default formatting options
    /// </summary>
    public static string ToJson<T>(T obj) => JsonSerializer.Serialize(obj, DefaultOptions);

    /// <summary>
    /// Serialize an object to JSON string with custom options
    /// </summary>
    public static string ToJson<T>(T obj, JsonSerializerOptions options) => 
        JsonSerializer.Serialize(obj, options);
}
