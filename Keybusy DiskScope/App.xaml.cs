using System.IO;
using Keybusy_DiskScope.Data;
using Keybusy_DiskScope.Services.Implementation;
using Keybusy_DiskScope.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Keybusy_DiskScope;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // EF Core — SQLite, stored in LocalApplicationData
        var dbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeybusyDiskScope");
        Directory.CreateDirectory(dbFolder);
        var dbPath = Path.Combine(dbFolder, "diskscope.db");

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Domain services
        services.AddSingleton<IDriveInfoService, DriveInfoService>();
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IDiffService, DiffService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels — transient: fresh instance each navigation
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ScanViewModel>();
        services.AddTransient<SnapshotsViewModel>();
        services.AddTransient<CompareViewModel>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Ensure database schema is created before the first window opens
        using (var scope = Services.CreateScope())
        {
            var factory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var db = factory.CreateDbContext();
            db.Database.EnsureCreated();
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
