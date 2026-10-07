using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace GoodbyeDPI.UI;

public class NetworkAdapterInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NetworkInterfaceType NetworkInterfaceType { get; set; }
    public OperationalStatus Status { get; set; }
    public List<string> DnsAddresses { get; set; } = new();

    public override string ToString() => $"{Name} ({Description})";
}

public static class DnsService
{
    public static List<NetworkAdapterInfo> GetActiveAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        foreach (var ni in interfaces)
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("Filter", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("Scheduler", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("LightWeight", StringComparison.OrdinalIgnoreCase) ||
                ni.Name.Contains("Filter", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                continue;
            var ipProps = ni.GetIPProperties();
            var dnsList = ipProps.DnsAddresses
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            list.Add(new NetworkAdapterInfo
            {
                Id = ni.Id,
                Name = ni.Name,
                Description = ni.Description,
                NetworkInterfaceType = ni.NetworkInterfaceType,
                Status = ni.OperationalStatus,
                DnsAddresses = dnsList
            });
        }

        return list;
    }

    public static async Task<(bool Success, string Message)> SetDnsAsync(string adapterName, string primaryDns, string? secondaryDns)
    {
        try
        {
            // Set primary DNS
            var primaryCmd = $"interface ipv4 set dns name=\"{adapterName}\" static {primaryDns} validate=no";
            var result1 = await RunNetshAsync(primaryCmd);
            if (!result1.Success)
            {
                return (false, $"Failed to set primary DNS: {result1.Output}");
            }

            // Set secondary DNS if provided
            if (!string.IsNullOrWhiteSpace(secondaryDns))
            {
                var secondaryCmd = $"interface ipv4 add dns name=\"{adapterName}\" {secondaryDns} index=2 validate=no";
                var result2 = await RunNetshAsync(secondaryCmd);
                if (!result2.Success)
                {
                    return (false, $"Failed to set secondary DNS: {result2.Output}");
                }
            }

            // Flush local DNS cache
            await FlushDnsCacheAsync();

            return (true, "DNS servers configured successfully.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool Success, string Message)> ResetDnsToDhcpAsync(string adapterName)
    {
        try
        {
            var cmd = $"interface ipv4 set dns name=\"{adapterName}\" source=dhcp";
            var result = await RunNetshAsync(cmd);
            if (!result.Success)
            {
                return (false, $"Failed to reset DNS: {result.Output}");
            }

            await FlushDnsCacheAsync();
            return (true, "Adapter DNS reset to DHCP.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task FlushDnsCacheAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static async Task<(bool Success, string Output)> RunNetshAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            return (false, "Could not start netsh process.");

        var stdOut = await proc.StandardOutput.ReadToEndAsync();
        var stdErr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var output = (stdOut + " " + stdErr).Trim();
        bool success = proc.ExitCode == 0;
        return (success, output);
    }
}
