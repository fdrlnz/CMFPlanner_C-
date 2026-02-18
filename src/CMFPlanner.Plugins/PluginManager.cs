using System.Composition.Hosting;
using System.Reflection;
using System.Text.Json;
using CMFPlanner.Plugins.Interfaces;

namespace CMFPlanner.Plugins;

/// <summary>
/// Scans a directory for MEF-based plugins, loads them at runtime, and exposes
/// them by category. Plugins are discovered from subdirectories that each contain
/// a <c>plugin.json</c> metadata file and one or more DLLs.
/// </summary>
public sealed class PluginManager
{
    private readonly string _pluginsDirectory;
    private readonly List<PluginLoadResult> _loaded = [];
    private readonly List<string> _errors = [];

    public IReadOnlyList<PluginLoadResult> LoadedPlugins => _loaded.AsReadOnly();

    /// <summary>Non-fatal error messages collected during <see cref="DiscoverAndLoad"/>.</summary>
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();

    public PluginManager(string pluginsDirectory)
    {
        _pluginsDirectory = pluginsDirectory;
    }

    /// <summary>Scans the plugins directory and loads all valid plugins.</summary>
    public void DiscoverAndLoad()
    {
        if (!Directory.Exists(_pluginsDirectory))
            return;

        foreach (var dir in Directory.GetDirectories(_pluginsDirectory))
            TryLoadPlugin(dir);
    }

    // Typed accessors --------------------------------------------------------

    public IEnumerable<IVisualizationPlugin> VisualizationPlugins =>
        _loaded.Select(r => r.Plugin).OfType<IVisualizationPlugin>();

    public IEnumerable<ISegmentationPlugin> SegmentationPlugins =>
        _loaded.Select(r => r.Plugin).OfType<ISegmentationPlugin>();

    public IEnumerable<IPlanningPlugin> PlanningPlugins =>
        _loaded.Select(r => r.Plugin).OfType<IPlanningPlugin>();

    public IEnumerable<IExportPlugin> ExportPlugins =>
        _loaded.Select(r => r.Plugin).OfType<IExportPlugin>();

    // Private ----------------------------------------------------------------

    private void TryLoadPlugin(string dir)
    {
        var metadataPath = Path.Combine(dir, "plugin.json");
        if (!File.Exists(metadataPath))
            return;

        PluginMetadata? metadata;
        try
        {
            var json = File.ReadAllText(metadataPath);
            metadata = JsonSerializer.Deserialize<PluginMetadata>(json);
            if (metadata is null)
            {
                _errors.Add($"Failed to parse plugin.json in '{dir}'.");
                return;
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Error reading plugin.json in '{dir}': {ex.Message}");
            return;
        }

        var dlls = Directory.GetFiles(dir, "*.dll");
        if (dlls.Length == 0)
        {
            _errors.Add($"No DLL found in plugin directory '{dir}'.");
            return;
        }

        try
        {
            var assemblies = dlls.Select(Assembly.LoadFrom).ToList();

            var configuration = new ContainerConfiguration()
                .WithAssemblies(assemblies);

            using var container = configuration.CreateContainer();
            var plugins = container.GetExports<IPlugin>().ToList();

            if (plugins.Count == 0)
            {
                _errors.Add($"No [Export(typeof(IPlugin))] found in '{dir}'.");
                return;
            }

            foreach (var plugin in plugins)
            {
                plugin.Initialize();
                _loaded.Add(new PluginLoadResult(plugin, metadata, dir));
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Error loading plugin from '{dir}': {ex.Message}");
        }
    }
}
