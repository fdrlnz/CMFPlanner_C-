using System.IO;
using System.Windows;
using CMFPlanner.Core.Session;
using CMFPlanner.Dicom;
using CMFPlanner.Plugins;
using CMFPlanner.Visualization;
using Microsoft.Extensions.DependencyInjection;

namespace CMFPlanner.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Discover and load plugins from the plugins/ directory next to the executable.
        var pluginManager = Services.GetRequiredService<PluginManager>();
        pluginManager.DiscoverAndLoad();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // UI
        services.AddSingleton<MainWindow>();

        // Plugin system
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        services.AddSingleton(new PluginManager(pluginsDir));

        // DICOM loading
        services.AddSingleton<IDicomLoader, DicomLoader>();

        // Session state
        services.AddSingleton<ISessionState, SessionState>();

        // Volume builder
        services.AddSingleton<IVolumeBuilder, VtkVolumeBuilder>();

        // Mesh extraction (marching cubes)
        services.AddSingleton<IMeshExtractor, MarchingCubesExtractor>();
    }
}
