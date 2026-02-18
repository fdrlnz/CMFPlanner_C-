using System.Windows;
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

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // UI
        services.AddSingleton<MainWindow>();

        // Core services will be registered here as they are implemented
        // e.g. services.AddSingleton<ISessionState, SessionState>();
    }
}
