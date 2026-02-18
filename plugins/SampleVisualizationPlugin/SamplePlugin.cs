using System.Composition;
using CMFPlanner.Plugins.Interfaces;

namespace SampleVisualizationPlugin;

/// <summary>
/// Minimal sample plugin that demonstrates the MEF plugin architecture.
/// Drop SampleVisualizationPlugin.dll + plugin.json into any plugins/ subdirectory
/// and it will be discovered at runtime without recompiling core.
/// </summary>
[Export(typeof(IPlugin))]
public sealed class SamplePlugin : IVisualizationPlugin
{
    public string Name        => "Sample Visualization Plugin";
    public string Version     => "1.0.0";
    public string Author      => "CMF Planner Team";
    public string Description => "Demonstrates the MEF plugin architecture. Replace with a real visualization plugin.";

    public void Initialize()
    {
        // Plugin-specific startup logic goes here.
    }
}
