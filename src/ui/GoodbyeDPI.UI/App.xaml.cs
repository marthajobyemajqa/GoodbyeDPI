using System.Configuration;
using System.Data;
using System.Windows;

namespace GoodbyeDPI.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!IsAdministrator())
        {
            RestartAsAdministrator();
            Shutdown();
            return;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static void RestartAsAdministrator()
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "GoodbyeDPI.UI.exe",
            Verb = "runas"
        };

        try
        {
            System.Diagnostics.Process.Start(processInfo);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt
        }
    }
}

