using System.Diagnostics;
using System.IO;

namespace GoodbyeDPI.UI;

public class GoodbyeDpiService
{
    private Process? _process;

    public bool IsRunning => _process != null && !_process.HasExited;

    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;
    public event Action? ProcessExited;

    public string? FindExecutablePath()
    {
        // 1. Look in relative build folders or bin directories
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "native", arch, "goodbyedpi.exe"),
            Path.Combine(baseDir, "goodbyedpi.exe"),
            Path.Combine(baseDir, "..", "..", "..", "native", arch, "goodbyedpi.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "x86_64", "goodbyedpi.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "goodbyedpi.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "native", arch, "goodbyedpi.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "goodbyedpi.exe")
        };
        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    public bool Start(string exePath, string arguments)
    {
        if (IsRunning)
        {
            Stop();
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("GoodbyeDPI executable not found.", exePath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                OutputReceived?.Invoke(e.Data);
        };

        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                ErrorReceived?.Invoke(e.Data);
        };

        _process.Exited += (s, e) =>
        {
            ProcessExited?.Invoke();
        };

        bool started = _process.Start();
        if (started)
        {
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        return started;
    }

    public async Task StopAsync()
    {
        await Task.Run(() => Stop());
    }

    public void Stop()
    {
        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore already terminated
            }
            finally
            {
                try
                {
                    _process.Dispose();
                }
                catch
                {
                    // ignore
                }
                _process = null;
            }
        }
    }
}
