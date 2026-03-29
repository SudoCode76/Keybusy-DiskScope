using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private const uint HardWorkingSetMaxFlag = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeEx(
        IntPtr hProcess,
        UIntPtr dwMinimumWorkingSetSize,
        UIntPtr dwMaximumWorkingSetSize,
        uint flags);

    public static IServiceProvider Services { get; private set; } = null!;

    public Window? MainWindow { get; private set; }

    private readonly string _appDataFolder;
    private readonly ILogger<App>? _appLogger;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // EF Core — SQLite, stored in LocalApplicationData
        _appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeybusyDiskScope");
        Directory.CreateDirectory(_appDataFolder);
        var dbPath = Path.Combine(_appDataFolder, "diskscope.db");

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Domain services
        services.AddSingleton<IDriveInfoService, DriveInfoService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IFileDeleteService, FileDeleteService>();
        services.AddSingleton<IDiffService, DiffService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels — transient: fresh instance each navigation
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ScanViewModel>();
        services.AddTransient<SnapshotsViewModel>();
        services.AddTransient<CompareViewModel>();
        services.AddTransient<SettingsViewModel>();

        Services = services.BuildServiceProvider();
        _appLogger = Services.GetService<ILogger<App>>();
        TryLimitWorkingSet();
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

        _ = Services.GetRequiredService<ISettingsService>();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTime.Now:O}] Unhandled exception");
            builder.AppendLine($"Message: {e.Message}");
            if (e.Exception is not null)
            {
                builder.AppendLine(e.Exception.ToString());
            }

            builder.AppendLine(new string('-', 60));

            var logPath = Path.Combine(_appDataFolder, "crash.log");
            File.AppendAllText(logPath, builder.ToString());
        }
        catch
        {
            // Ignorar fallos al escribir el log
        }

        _appLogger?.LogError(e.Exception, "Unhandled exception: {Message}", e.Message);
    }

    private void TryLimitWorkingSet()
    {
        const ulong minBytes = 64ul * 1024 * 1024;
        const ulong maxBytes = 2ul * 1024 * 1024 * 1024;

        bool success = SetProcessWorkingSetSizeEx(
            GetCurrentProcess(),
            new UIntPtr(minBytes),
            new UIntPtr(maxBytes),
            HardWorkingSetMaxFlag);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            _appLogger?.LogWarning("Working set limit not applied. Win32Error={Error}", error);
        }
    }
}
