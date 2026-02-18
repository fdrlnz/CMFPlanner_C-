using System.IO;
using System.Windows;
using CMFPlanner.Plugins;
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

        // Core services will be registered here as they are implemented
        // e.g. services.AddSingleton<ISessionState, SessionState>();
    }
}
