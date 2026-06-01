using System;
using System.Net.Http;
using System.Threading;
using BlueMarble.Composition;
using BlueMarble.Imagery;
using BlueMarble.Settings;
using BlueMarble.UI;
using BlueMarble.Wallpaper;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace BlueMarble;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    public static AppServices Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TryAcquireSingleInstance())
        {
            Exit();
            return;
        }

        Services = AppServices.Build();
        Services.Logger.LogInformation("BlueMarble starting (version {Version})", AppInfo.Version);

        Services.Settings.Load();
        Services.Settings.StartWatching();
        Services.Cache.PruneOlderThan(TimeSpan.FromDays(30));
        Services.TrayHost.Show();
        Services.Scheduler.Start();
    }

    private static bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "BlueMarble.Desktop.SingleInstance", out var createdNew);
        return createdNew;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Services?.Logger.LogError(e.Exception, "Unhandled exception: {Message}", e.Message);
        e.Handled = true;
    }
}

public sealed class AppServices
{
    public required ILoggerFactory LoggerFactory { get; init; }
    public required ILogger Logger { get; init; }
    public required SettingsStore Settings { get; init; }
    public required HttpClient Http { get; init; }
    public required TileCache Cache { get; init; }
    public required GibsWmsClient Gibs { get; init; }
    public required BlueMarbleProvider Day { get; init; }
    public required ModisTrueColorProvider TrueColorDay { get; init; }
    public required HybridDayProvider HybridDay { get; init; }
    public required BlackMarbleProvider Night { get; init; }
    public required FrameComposer Composer { get; init; }
    public required WallpaperApplier WallpaperApplier { get; init; }
    public required IRefreshController Refresh { get; init; }
    public required RefreshScheduler Scheduler { get; init; }
    public required TrayIconHost TrayHost { get; init; }

    public static AppServices Build()
    {
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("BlueMarble");
        var settings = new SettingsStore(loggerFactory.CreateLogger<SettingsStore>());
        var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        });
        var cache = new TileCache(loggerFactory.CreateLogger<TileCache>());
        var gibs = new GibsWmsClient(http, loggerFactory.CreateLogger<GibsWmsClient>());
        var day = new BlueMarbleProvider(gibs, cache, loggerFactory.CreateLogger<BlueMarbleProvider>());
        var trueColorDay = new ModisTrueColorProvider(gibs, cache, loggerFactory.CreateLogger<ModisTrueColorProvider>());
        var hybridDay = new HybridDayProvider(day, trueColorDay, cache, loggerFactory.CreateLogger<HybridDayProvider>());
        var night = new BlackMarbleProvider(gibs, cache, loggerFactory.CreateLogger<BlackMarbleProvider>());
        var composer = new FrameComposer();
        var wallpaper = new WallpaperApplier(loggerFactory.CreateLogger<WallpaperApplier>());
        var refresh = new PrefetchRefreshController(day, hybridDay, night, composer, wallpaper, settings,
            loggerFactory.CreateLogger<PrefetchRefreshController>());
        var scheduler = new RefreshScheduler(refresh, settings,
            loggerFactory.CreateLogger<RefreshScheduler>());
        var trayHost = new TrayIconHost(loggerFactory.CreateLogger<TrayIconHost>(), refresh, settings);

        return new AppServices
        {
            LoggerFactory = loggerFactory,
            Logger = logger,
            Settings = settings,
            Http = http,
            Cache = cache,
            Gibs = gibs,
            Day = day,
            TrueColorDay = trueColorDay,
            HybridDay = hybridDay,
            Night = night,
            Composer = composer,
            WallpaperApplier = wallpaper,
            Refresh = refresh,
            Scheduler = scheduler,
            TrayHost = trayHost,
        };
    }
}

internal static class AppInfo
{
    public const string Version = "0.1.0";
}
