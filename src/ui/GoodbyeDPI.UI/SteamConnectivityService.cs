using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace GoodbyeDPI.UI;

public class SteamTestResult
{
    public string TargetName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class SteamConnectivityService
{
    public static async Task<List<SteamTestResult>> RunDiagnosticsAsync(Action<SteamTestResult>? onStepCompleted = null)
    {
        var targets = new List<Func<Task<SteamTestResult>>>
        {
            () => TestDnsResolutionAsync("store.steampowered.com"),
            () => TestDnsResolutionAsync("steamcommunity.com"),
            () => TestDnsResolutionAsync("api.steampowered.com"),
            () => TestHttpEndpointAsync("Steam Store Web", "https://store.steampowered.com"),
            () => TestHttpEndpointAsync("Steam Community Web", "https://steamcommunity.com"),
            () => TestHttpEndpointAsync("Steam Web API", "https://api.steampowered.com/ISteamWebAPIUtil/GetServerInfo/v1/"),
            () => TestCmEndpointAsync("Steam CM WebSocket (live list)", "websockets"),
            () => TestCmEndpointAsync("Steam Client CM TCP (live list)", "netfilter"),
            () => TestTcpPortAsync("Steam CDN (TCP 80)", "media.steampowered.com", 80)
        };

        var results = new List<SteamTestResult>();
        foreach (var taskFunc in targets)
        {
            var res = await taskFunc();
            results.Add(res);
            onStepCompleted?.Invoke(res);
        }

        return results;
    }

    private static async Task<SteamTestResult> TestDnsResolutionAsync(string host)
    {
        var sw = Stopwatch.StartNew();
        var result = new SteamTestResult
        {
            TargetName = $"DNS Lookup ({host})",
            Endpoint = host,
            Protocol = "DNS"
        };

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;

            if (addresses.Length > 0)
            {
                result.Success = true;
                result.Message = $"Resolved to: {string.Join(", ", addresses.Select(a => a.ToString()))}";
            }
            else
            {
                result.Success = false;
                result.Message = "No IP addresses returned.";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Success = false;
            result.Message = $"Resolution failed: {ex.Message}";
        }

        return result;
    }

    private static async Task<SteamTestResult> TestHttpEndpointAsync(string name, string url)
    {
        var sw = Stopwatch.StartNew();
        var result = new SteamTestResult
        {
            TargetName = name,
            Endpoint = url,
            Protocol = "HTTPS/HTTP"
        };

        try
        {
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Valve Steam Client");

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Success = response.IsSuccessStatusCode || ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400);
            result.Message = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Success = false;
            result.Message = $"Request failed: {ex.Message}";
        }

        return result;
    }

    private static async Task<SteamTestResult> TestCmEndpointAsync(string name, string type)
    {
        var sw = Stopwatch.StartNew();
        string? endpoint = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await http.GetStringAsync("https://api.steampowered.com/ISteamDirectory/GetCMListForConnect/v1/?cellid=0&maxcount=20");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var s in doc.RootElement.GetProperty("response").GetProperty("serverlist").EnumerateArray())
            {
                if (s.GetProperty("type").GetString() == type)
                {
                    endpoint = s.GetProperty("endpoint").GetString();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            return new SteamTestResult { TargetName = name, Endpoint = "ISteamDirectory", Protocol = "TCP", Success = false, LatencyMs = sw.ElapsedMilliseconds, Message = $"CM list fetch failed: {ex.Message}" };
        }

        if (endpoint == null || endpoint.LastIndexOf(':') < 0)
        {
            return new SteamTestResult { TargetName = name, Endpoint = "ISteamDirectory", Protocol = "TCP", Success = false, LatencyMs = sw.ElapsedMilliseconds, Message = $"No '{type}' endpoint in CM list" };
        }

        int idx = endpoint.LastIndexOf(':');
        return await TestTcpPortAsync(name, endpoint[..idx], int.Parse(endpoint[(idx + 1)..]));
    }

    private static async Task<SteamTestResult> TestTcpPortAsync(string name, string host, int port)
    {
        var sw = Stopwatch.StartNew();
        var result = new SteamTestResult
        {
            TargetName = name,
            Endpoint = $"{host}:{port}",
            Protocol = "TCP"
        };

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Success = client.Connected;
            result.Message = "Connected successfully";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Success = false;
            result.Message = $"TCP Connection failed: {ex.Message}";
        }

        return result;
    }
}
