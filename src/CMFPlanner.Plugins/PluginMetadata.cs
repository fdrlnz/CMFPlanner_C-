using System.Text.Json.Serialization;

namespace CMFPlanner.Plugins;

/// <summary>Metadata declared in a plugin's plugin.json file.</summary>
public sealed record PluginMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("description")] string Description);
