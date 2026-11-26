using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;

namespace Sic.App;

/// <summary>
/// The command line. <see cref="Quit"/> (<c>--quit</c>) asks the SIC running with the data folder to quit, as its tray menu's
/// Quit does, and starts nothing: for scripts, and for testing an update that is applied when SIC quits. <see cref="Open"/>
/// (<c>--open &lt;folder&gt;</c>, with <c>--agent &lt;id&gt;</c> to start that agent there at once) is what File Explorer's Open in SIC
/// runs: handed to the SIC already running with the data folder, or carried out by this one once its window is up.
/// <see cref="Isolated"/> says the data folder was given on the command line, as the smoke and review scripts do: such a copy
/// never touches the Explorer menu, which belongs to the SIC of the default folder. <see cref="Measure"/>
/// (<c>--measure-hidden-output</c>) runs the hidden-page measurement instead of the smoke (MainWindow.Measure.cs), with the
/// smoke's isolation.
/// </summary>
internal sealed record StartupOptions(bool SmokeTest, string DataDirectory, string? ReportPath, bool RestoreOnly = false, bool MigrateLegacyState = false, bool Quit = false,
    Sic.Core.Shell.OpenRequest? Open = null, bool Isolated = false, OutputMeasure? Measure = null)
{
    /// <summary>Never the package id (SicTerm): that names the install folder in %LOCALAPPDATA%, which an uninstall deletes (docs/decisions/installer.md).</summary>
    internal const string DataFolderName = "SuperIntelligentChat";
    internal const string LegacyDataFolderName = "CommandCentral";

    internal static StartupOptions Parse(string[] arguments)
    {
        var smokeTest = false;
        var restoreOnly = false;
        var quit = false;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);
        var explicitDirectory = false;
        string? report = null, openFolder = null, openAgent = null;
        OutputMeasure? measure = null;
        static double Number(string text) => double.Parse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--terminal-smoke-test": smokeTest = true; break;
                case "--restore-smoke-test": smokeTest = true; restoreOnly = true; break;
                case "--measure-hidden-output": smokeTest = true; measure ??= new OutputMeasure(); break;
                case "--measure-rate" when index + 1 < arguments.Length: measure = (measure ?? new OutputMeasure()) with { Kilobytes = (int)Number(arguments[++index]) }; break;
                case "--measure-minutes" when index + 1 < arguments.Length: measure = (measure ?? new OutputMeasure()) with { HiddenMinutes = Number(arguments[++index]) }; break;
                case "--measure-tray-minutes" when index + 1 < arguments.Length: measure = (measure ?? new OutputMeasure()) with { TrayMinutes = Number(arguments[++index]) }; break;
                case "--measure-burst" when index + 1 < arguments.Length: measure = (measure ?? new OutputMeasure()) with { BurstMegabytes = (int)Number(arguments[++index]) }; break;
                case "--measure-burst-after" when index + 1 < arguments.Length: measure = (measure ?? new OutputMeasure()) with { BurstAfterSeconds = Number(arguments[++index]) }; break;
                case "--data-dir" when index + 1 < arguments.Length: directory = Path.GetFullPath(arguments[++index]); explicitDirectory = true; break;
                case "--report" when index + 1 < arguments.Length: report = Path.GetFullPath(arguments[++index]); break;
                case "--quit": quit = true; break;
                // Explorer hands a drive over as "C:\" and a folder without its trailing separator; both are kept as given, made full.
                case "--open" when index + 1 < arguments.Length: openFolder = Path.GetFullPath(arguments[++index]); break;
                case "--agent" when index + 1 < arguments.Length: openAgent = arguments[++index]; break;
                default: throw new ArgumentException("Unknown or incomplete application option: " + arguments[index]);
            }
        }
        if (smokeTest && report is null) throw new ArgumentException("The smoke test requires --report.");
        if (openAgent is not null && openFolder is null) throw new ArgumentException("--agent needs --open <folder>.");
        if (openFolder is not null && (smokeTest || quit)) throw new ArgumentException("--open cannot be combined with --quit or a smoke test.");
        if (measure is not null && (restoreOnly || !explicitDirectory || measure.Kilobytes is < 1 or > 100_000 || measure.HiddenMinutes < 0 || measure.TrayMinutes < 0
            || measure.BurstMegabytes is < 0 or > 1000 || measure.BurstAfterSeconds < 0))
            throw new ArgumentException("--measure-hidden-output needs --data-dir, a rate from 1 to 100000 KB/s, a burst of at most 1000 MB and no negative times, and is not a restore check.");
        return new StartupOptions(smokeTest, directory, report, restoreOnly, MigrateLegacyState: !explicitDirectory && !smokeTest && !quit, Quit: quit,
            Open: openFolder is null ? null : new Sic.Core.Shell.OpenRequest(openFolder, openAgent), Isolated: explicitDirectory, Measure: measure);
    }

    /// <summary>
    /// First launch after the rename from Command Central: copies the previous settings, tab layout and catalog into the
    /// new data folder when it is still empty. The old files are read, never moved or deleted.
    /// </summary>
    internal static bool CopyLegacyState(string directory)
    {
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyDataFolderName);
        if (File.Exists(Path.Combine(directory, "settings.json")) || !File.Exists(Path.Combine(legacy, "settings.json"))) return false;
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "settings.json", "layout.json", "catalog.db", "catalog.db-wal", "catalog.db-shm" })
        {
            var source = Path.Combine(legacy, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(directory, name), overwrite: false);
        }
        return true;
    }

    internal void WriteReport(object report)
    {
        if (ReportPath is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        File.WriteAllText(ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal void Trace(string stage)
    {
        if (!SmokeTest || ReportPath is null) return;
        File.AppendAllText(ReportPath + ".trace", DateTimeOffset.UtcNow.ToString("O") + " " + stage + Environment.NewLine);
    }
}

/// <summary>
/// The hidden-page measurement's settings (MainWindow.Measure.cs): the fixture's output rate in kilobytes a second, how long the
/// pane's tab stays hidden behind another, and how long the window then stays in the tray with the pane's tab selected; and,
/// when <see cref="BurstMegabytes"/> is set, a burst of that many megabytes printed as fast as the pipe takes it, that many
/// seconds after the tab was hidden.
/// </summary>
internal sealed record OutputMeasure(int Kilobytes = 100, double HiddenMinutes = 10, double TrayMinutes = 2, int BurstMegabytes = 0, double BurstAfterSeconds = 90);

internal static class Program
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        // First, before the arguments are read: Setup and the updater run SicTerm.exe with hook arguments of their own, which
        // Velopack handles and exits on. It would also apply a downloaded update at any launch, closing a running SIC and its
        // agents to do so when a second launch only meant to bring the window forward; SIC applies updates itself instead,
        // below once it owns the data folder, and at quit.
        // The hooks put Open in SIC on File Explorer's menu for the installed copy and take it off at uninstall; each runs in a
        // process of its own that Velopack ends when the callback returns (docs/decisions/explorer-menu.md).
        VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => ExplorerMenuRegistration.TryRegisterInstalled(fresh: true))
            .OnAfterUpdateFastCallback(_ => ExplorerMenuRegistration.TryRegisterInstalled(fresh: false))
            .OnBeforeUninstallFastCallback(_ => { ExplorerMenuRegistration.TryUnregisterInstalled(); StartupRegistration.TryRemoveInstalled(); })
            .Run();
        StartupOptions? options = null;
        try
        {
            options = StartupOptions.Parse(arguments);
            Directory.CreateDirectory(options.DataDirectory);
            Diagnostics.Start(options.DataDirectory);
            Diagnostics.Log("launch: " + (arguments.Length == 0 ? "(no arguments)" : string.Join(' ', arguments)) + " from " + Environment.ProcessPath);
            if (options.Quit) { Diagnostics.Log("quit requested of the running SIC"); SingleInstance.RequestQuit(options.DataDirectory); return; }
            // The smoke holds its data folder too, so that a second SicTerm.exe --open for that folder reaches it the way one reaches a real SIC.
            if (!SingleInstance.TryAcquire(options.DataDirectory, options.Open)) { Diagnostics.Log("handed to the running SIC; exiting"); return; }
            if (!options.SmokeTest && Updates.ApplyPendingAndRestart(arguments)) { Diagnostics.Log("applying a pending update and restarting"); return; }
            if (options.MigrateLegacyState)
            {
                try { StartupOptions.CopyLegacyState(options.DataDirectory); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Start fresh; the previous folder is untouched and can be copied by hand. */ }
            }
            if (options.ReportPath is not null)
            {
                // Diagnostics only: a XAML stowed exception never reaches the managed handlers, but the managed exception that caused it is visible here.
                var firstChanceLog = options.ReportPath + ".firstchance";
                AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
                {
                    try { File.AppendAllText(firstChanceLog, DateTimeOffset.UtcNow.ToString("O") + " " + args.Exception + Environment.NewLine + "--- managed stack at throw ---" + Environment.NewLine + Environment.StackTrace + Environment.NewLine + Environment.NewLine); }
                    catch (Exception) { /* never let diagnostics fail the app */ }
                };
            }
            options.Trace("managed-entry");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            options.Trace("com-wrappers-ready");
            Application.Start(_ =>
            {
                options.Trace("application-start-callback");
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new SicApplication(options);
            });
        }
        catch (Exception exception)
        {
            Diagnostics.Log("fatal: " + exception);
            options?.WriteReport(new { success = false, error = exception.ToString() });
            Environment.ExitCode = 1;
        }
        Diagnostics.Log("exit " + Environment.ExitCode);
    }
}

public sealed partial class SicApplication : Application
{
    private readonly StartupOptions _options;
    private MainWindow? _window;

    internal SicApplication(StartupOptions options)
    {
        _options = options;
        _options.Trace("application-constructor");
        UnhandledException += (_, args) =>
        {
            Diagnostics.Log("unhandled exception, exiting: " + args.Message + Environment.NewLine + args.Exception);
            _options.Trace("xaml-unhandled: " + args.Message);
            _options.WriteReport(new { success = false, error = args.Exception.ToString(), xamlMessage = args.Message });
            Environment.ExitCode = 1;
            args.Handled = true;
            Exit();
        };
        if (_options.ReportPath is not null)
        {
            // Diagnostics only: surface XAML errors through UnhandledException instead of a native fail-fast so the report names them.
            DebugSettings.FailFastOnErrors = false;
            DebugSettings.LayoutCycleTracingLevel = LayoutCycleTracingLevel.High;
            DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
            DebugSettings.XamlResourceReferenceFailed += (_, args) => _options.Trace("xaml-resource-failed: " + args.Message);
        }        InitializeComponent();
        _options.Trace("control-resources-ready");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _options.Trace("on-launched");
        _window = new MainWindow(_options);
        _options.Trace("window-constructed");
        _window.Activate();
        _options.Trace("window-activated");
    }
}