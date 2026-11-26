using Microsoft.UI.Xaml.Controls;
using Sic.Core.Shell;

namespace Sic.App;

internal sealed partial class MainWindow
{
    private bool _syncingRunAtStartupSwitch;

    /// <summary>
    /// Settings → General → Run at Startup (Chris, November 12, 2026; docs/decisions/run-at-startup.md): on, SicTerm starts as
    /// its person signs in to Windows. Its state is the registry's, not settings.json's, as Open in SicTerm's is: on when the
    /// entry starts this exe and Task Manager has not turned it off. A copy with a data folder of its own (the smoke) shows
    /// the switch disabled and leaves the real entry alone.
    /// </summary>
    private void BuildRunAtStartupSwitch(StackPanel stack)
    {
        var executable = Environment.ProcessPath ?? "";
        string? problem = null, other = null;
        var on = false;
        try
        {
            on = StartupRegistration.IsOn(executable);
            if (RunAtStartup.ExecutableOf(StartupRegistration.Command()) is { } registered && !string.Equals(registered, executable, StringComparison.OrdinalIgnoreCase)) other = registered;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { problem = exception.Message; }
        var toggle = new ToggleSwitch { Header = "Run at Startup", OnContent = "On", OffContent = "Off", IsOn = on, IsEnabled = !_options.Isolated && problem is null };
        void Describe(string? pointsAt) => Explain(toggle, pointsAt is not null ? "Starts another copy: " + pointsAt : "SicTerm starts at sign-in to Windows");
        Describe(other);
        toggle.Toggled += (_, _) =>
        {
            if (_syncingRunAtStartupSwitch) return;
            try
            {
                if (toggle.IsOn) StartupRegistration.Enable(executable); else StartupRegistration.Disable();
                Describe(null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ShowError("Run at Startup could not be changed: " + exception.Message);
                _syncingRunAtStartupSwitch = true;
                try { toggle.IsOn = !toggle.IsOn; } finally { _syncingRunAtStartupSwitch = false; }
            }
        };
        stack.Children.Add(toggle);
        // A disabled switch shows no tooltip, so a registry that could not be read is said on the page. The other reason, a data
        // folder of its own, is said once, under Open in SicTerm's switch below.
        if (problem is not null)
        {
            var note = _theme.Tertiary("The registry could not be read: " + problem);
            note.TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap; note.TextTrimming = Microsoft.UI.Xaml.TextTrimming.None;
            stack.Children.Add(note);
        }
    }
}
