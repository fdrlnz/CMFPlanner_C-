namespace CMFPlanner.Plugins.Interfaces;

/// <summary>Base interface for all CMF Planner plugins.</summary>
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }

    /// <summary>Called once after the plugin is loaded.</summary>
    void Initialize();
}
