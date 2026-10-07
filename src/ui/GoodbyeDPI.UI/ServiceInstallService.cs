using System.Diagnostics;
using System.IO;

namespace GoodbyeDPI.UI;

public enum ServiceState { NotInstalled, Stopped, Running, Transitioning }

/// <summary>
/// Installs goodbyedpi.exe as the "GoodbyeDPI" Windows service (the name is hardcoded in src/service.c)
/// using sc.exe, mirroring the upstream service_install_*.cmd scripts.
/// </summary>
public static class ServiceInstallService
{
    public const string ServiceName = "GoodbyeDPI";
    private const string DriverName = "WinDivert1.4";
    private const int ErrorServiceDoesNotExist = 1060;

    // Program Files is admin-writable only; the service runs as LocalSystem, so its binary must not be user-writable.
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GoodbyeDPI");

    public static async Task<ServiceState> QueryAsync()
    {
        var (code, output) = await RunScAsync("query", ServiceName);
        if (code == ErrorServiceDoesNotExist) return ServiceState.NotInstalled;
        if (output.Contains("RUNNING", StringComparison.Ordinal)) return ServiceState.Running;
        if (output.Contains("STOPPED", StringComparison.Ordinal)) return ServiceState.Stopped;
        return code == 0 ? ServiceState.Transitioning : ServiceState.NotInstalled;
    }

    public static async Task<(bool Success, string Message)> InstallAsync(string sourceExe, string arguments)
    {
        try
        {
            await StopAndDeleteServiceAsync();

            string installedExe = await Task.Run(() => CopyBinaries(sourceExe));
            string binPath = $"\"{installedExe}\" {arguments}";

            var create = await RunScAsync("create", ServiceName, "binPath=", binPath, "start=", "auto");
            if (create.ExitCode != 0)
                return (false, $"sc create failed ({create.ExitCode}): {create.Output}");

            await RunScAsync("description", ServiceName,
                "Passive Deep Packet Inspection blocker and Active DPI circumvention utility");

            var start = await RunScAsync("start", ServiceName);
            if (start.ExitCode != 0)
                return (false, $"Service installed but failed to start ({start.ExitCode}): {start.Output}");

            if (!await WaitForStateAsync(ServiceState.Running))
                return (false, "Service installed but did not reach the Running state. Check the arguments and that WinDivert can load.");

            return (true, $"Service installed and running. Starts automatically with Windows. Binary: {installedExe}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool Success, string Message)> UninstallAsync()
    {
        try
        {
            await StopAndDeleteServiceAsync();

            // Same cleanup as upstream service_remove.cmd: unload the WinDivert driver service.
            await RunScAsync("stop", DriverName);
            await RunScAsync("delete", DriverName);

            if (await QueryAsync() != ServiceState.NotInstalled)
                return (false, "Service is marked for deletion but still present. It will disappear after a reboot or once handles close.");

            // Best effort: driver/DLL may still be locked until the driver unloads.
            try { if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, recursive: true); }
            catch (Exception) { }

            return (true, "Service removed.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task StopAndDeleteServiceAsync()
    {
        if (await QueryAsync() == ServiceState.NotInstalled) return;
        await RunScAsync("stop", ServiceName);
        await WaitForStateAsync(ServiceState.Stopped);
        await RunScAsync("delete", ServiceName);
    }

    private static async Task<bool> WaitForStateAsync(ServiceState wanted)
    {
        for (int i = 0; i < 20; i++)
        {
            if (await QueryAsync() == wanted) return true;
            await Task.Delay(500);
        }
        return false;
    }

    // Copies the arch directory (goodbyedpi.exe + WinDivert.dll + WinDivert*.sys) next to each other under InstallDir.
    private static string CopyBinaries(string sourceExe)
    {
        string srcDir = Path.GetDirectoryName(Path.GetFullPath(sourceExe))
            ?? throw new InvalidOperationException("Invalid executable path.");
        string destDir = Path.Combine(InstallDir, new DirectoryInfo(srcDir).Name);

        if (!string.Equals(Path.GetFullPath(srcDir).TrimEnd('\\'), Path.GetFullPath(destDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(srcDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        return Path.Combine(destDir, Path.GetFileName(sourceExe));
    }

    private static async Task<(int ExitCode, string Output)> RunScAsync(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start sc.exe.");
        Task<string> stdOut = proc.StandardOutput.ReadToEndAsync();
        Task<string> stdErr = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, ((await stdOut) + " " + (await stdErr)).Trim());
    }
}
