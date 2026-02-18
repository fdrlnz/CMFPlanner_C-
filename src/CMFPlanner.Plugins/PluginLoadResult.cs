using CMFPlanner.Plugins.Interfaces;

namespace CMFPlanner.Plugins;

/// <summary>Represents a successfully loaded plugin with its metadata and source directory.</summary>
public sealed record PluginLoadResult(
    IPlugin Plugin,
    PluginMetadata Metadata,
    string PluginDirectory);
