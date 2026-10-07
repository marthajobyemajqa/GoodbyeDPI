using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace GoodbyeDPI.UI;

public partial class MainWindow : Window
{
    private readonly GoodbyeDpiService _dpiService = new();
    private readonly ObservableCollection<SteamTestResult> _steamResults = new();
    private List<NetworkAdapterInfo> _adapters = new();

    public MainWindow()
    {
        InitializeComponent();
        SteamResultsGrid.ItemsSource = _steamResults;
        SimpleResultsList.ItemsSource = _steamResults;

        _dpiService.OutputReceived += OnDpiOutput;
        _dpiService.ErrorReceived += OnDpiOutput;
        _dpiService.ProcessExited += OnDpiExited;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAdapters();
        LocateGoodbyeDpiExe();
        await RefreshServiceStateAsync();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_dpiService.IsRunning)
        {
            _dpiService.Stop();
        }
    }

    private void LocateGoodbyeDpiExe()
    {
        var foundPath = _dpiService.FindExecutablePath();
        if (!string.IsNullOrEmpty(foundPath))
        {
            ExePathInput.Text = foundPath;
            AppendLog($"Auto-detected GoodbyeDPI at: {foundPath}");
        }
        else
        {
            AppendLog("GoodbyeDPI executable not detected automatically. Please specify path in GoodbyeDPI Daemon & Logs tab.");
        }
    }

    private void RefreshAdapters()
    {
        _adapters = DnsService.GetActiveAdapters();
        AdapterComboBox.ItemsSource = null;
        AdapterComboBox.ItemsSource = _adapters;

        if (_adapters.Count > 0)
        {
            AdapterComboBox.SelectedIndex = 0;
        }
        else
        {
            CurrentDnsDisplay.Text = "No active network adapters found.";
        }
    }

    private void RefreshAdaptersBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdapters();
        FooterStatusText.Text = $"Refreshed {_adapters.Count} active adapters.";
    }

    private void AdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterComboBox.SelectedItem is NetworkAdapterInfo adapter)
        {
            if (adapter.DnsAddresses.Count > 0)
            {
                CurrentDnsDisplay.Text = string.Join(", ", adapter.DnsAddresses);
            }
            else
            {
                CurrentDnsDisplay.Text = "Configured via DHCP or not explicitly set";
            }
        }
    }

    private void PresetCloudflare_Click(object sender, RoutedEventArgs e)
    {
        PrimaryDnsInput.Text = "1.1.1.1";
        SecondaryDnsInput.Text = "1.0.0.1";
        DpiDnsAddrInput.Text = "1.1.1.1";
        DpiDnsV6AddrInput.Text = "2606:4700:4700::1111";
    }

    private void PresetGoogle_Click(object sender, RoutedEventArgs e)
    {
        PrimaryDnsInput.Text = "8.8.8.8";
        SecondaryDnsInput.Text = "8.8.4.4";
        DpiDnsAddrInput.Text = "8.8.8.8";
        DpiDnsV6AddrInput.Text = "2001:4860:4860::8888";
    }

    private void PresetQuad9_Click(object sender, RoutedEventArgs e)
    {
        PrimaryDnsInput.Text = "9.9.9.9";
        SecondaryDnsInput.Text = "149.112.112.112";
        DpiDnsAddrInput.Text = "9.9.9.9";
        DpiDnsV6AddrInput.Text = "2620:fe::fe";
    }

    private void PresetAdGuard_Click(object sender, RoutedEventArgs e)
    {
        PrimaryDnsInput.Text = "94.140.14.14";
        SecondaryDnsInput.Text = "94.140.15.15";
        DpiDnsAddrInput.Text = "94.140.14.14";
        DpiDnsV6AddrInput.Text = "2a10:50c0::ad1:ff";
    }

    private void PresetOpenDns_Click(object sender, RoutedEventArgs e)
    {
        PrimaryDnsInput.Text = "208.67.222.222";
        SecondaryDnsInput.Text = "208.67.220.220";
        DpiDnsAddrInput.Text = "208.67.222.222";
        DpiDnsV6AddrInput.Text = "2620:119:35::35";
    }

    private async void ApplyDnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterComboBox.SelectedItem is not NetworkAdapterInfo adapter)
        {
            MessageBox.Show("Please select a network adapter first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string primary = PrimaryDnsInput.Text.Trim();
        string secondary = SecondaryDnsInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(primary))
        {
            MessageBox.Show("Primary DNS address is required.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyDnsBtn.IsEnabled = false;
        FooterStatusText.Text = $"Applying DNS to {adapter.Name}...";

        var (success, msg) = await DnsService.SetDnsAsync(adapter.Name, primary, secondary);
        ApplyDnsBtn.IsEnabled = true;

        if (success)
        {
            FooterStatusText.Text = $"DNS updated on {adapter.Name}: {primary} / {secondary}";
            RefreshAdapters();
            MessageBox.Show($"DNS updated successfully for '{adapter.Name}'.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            FooterStatusText.Text = "Failed to update DNS.";
            MessageBox.Show($"Failed to set DNS: {msg}\nMake sure the app is running as Administrator.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetDnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AdapterComboBox.SelectedItem is not NetworkAdapterInfo adapter)
        {
            MessageBox.Show("Please select a network adapter first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResetDnsBtn.IsEnabled = false;
        FooterStatusText.Text = $"Resetting {adapter.Name} DNS to DHCP...";

        var (success, msg) = await DnsService.ResetDnsToDhcpAsync(adapter.Name);
        ResetDnsBtn.IsEnabled = true;

        if (success)
        {
            FooterStatusText.Text = $"DNS on {adapter.Name} reset to DHCP.";
            RefreshAdapters();
            MessageBox.Show($"DNS on '{adapter.Name}' reset to automatic (DHCP).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            FooterStatusText.Text = "Failed to reset DNS.";
            MessageBox.Show($"Failed to reset DNS: {msg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FlushDnsBtn_Click(object sender, RoutedEventArgs e)
    {
        FlushDnsBtn.IsEnabled = false;
        FooterStatusText.Text = "Flushing DNS resolver cache...";
        await DnsService.FlushDnsCacheAsync();
        FlushDnsBtn.IsEnabled = true;
        FooterStatusText.Text = "DNS resolver cache flushed.";
        MessageBox.Show("Windows DNS resolver cache flushed successfully.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RunSteamTestBtn_Click(object sender, RoutedEventArgs e)
    {
        RunSteamTestBtn.IsEnabled = false;
        TestProgressBar.Visibility = Visibility.Visible;
        _steamResults.Clear();
        FooterStatusText.Text = "Running Steam connectivity tests...";

        try
        {
            await SteamConnectivityService.RunDiagnosticsAsync(result =>
            {
                Dispatcher.Invoke(() =>
                {
                    _steamResults.Add(result);
                });
            });

            int passed = _steamResults.Count(r => r.Success);
            int total = _steamResults.Count;
            FooterStatusText.Text = $"Steam diagnostics finished: {passed}/{total} checks passed.";
        }
        catch (Exception ex)
        {
            FooterStatusText.Text = $"Diagnostics error: {ex.Message}";
        }
        finally
        {
            RunSteamTestBtn.IsEnabled = true;
            TestProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void BrowseExeBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select GoodbyeDPI Executable"
        };

        if (dlg.ShowDialog() == true)
        {
            ExePathInput.Text = dlg.FileName;
        }
    }

    private void StartDpiBtn_Click(object sender, RoutedEventArgs e)
    {
        string exePath = ExePathInput.Text.Trim();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            MessageBox.Show("Please select a valid goodbyedpi.exe executable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_serviceState == ServiceState.Running)
        {
            MessageBox.Show("The GoodbyeDPI service is running. Uninstall it before starting a local instance.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string args = BuildAdvancedArgs();

        try
        {
            AppendLog($"Starting: {exePath} {args}");
            bool started = _dpiService.Start(exePath, args);
            if (started)
            {
                SetDpiRunningState(true);
                FooterStatusText.Text = $"GoodbyeDPI started with args: {args}";
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Failed to start GoodbyeDPI: {ex.Message}");
            MessageBox.Show($"Failed to start GoodbyeDPI: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StopDpiBtn_Click(object sender, RoutedEventArgs e)
    {
        StopDpiBtn.IsEnabled = false;
        FooterStatusText.Text = "Stopping GoodbyeDPI...";
        await _dpiService.StopAsync();
        SetDpiRunningState(false);
        FooterStatusText.Text = "GoodbyeDPI stopped.";
        AppendLog("GoodbyeDPI stopped by user.");
    }

    private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        LogsTextBox.Clear();
    }

    private void OnDpiOutput(string line)
    {
        Dispatcher.BeginInvoke(() => AppendLog(line));
    }

    private void OnDpiExited()
    {
        Dispatcher.BeginInvoke(() =>
        {
            SetDpiRunningState(false);
            FooterStatusText.Text = "GoodbyeDPI process terminated.";
            AppendLog("GoodbyeDPI process exited.");
        });
    }

    private void SetDpiRunningState(bool running)
    {
        StartDpiBtn.IsEnabled = !running;
        StopDpiBtn.IsEnabled = running;
        UpdateOneClickButton(running);

        if (running)
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x4F, 0x32));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            StatusBadgeText.Text = "GoodbyeDPI: Running";
        }
        else
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44));
            StatusBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            StatusBadgeText.Text = "GoodbyeDPI: Stopped";
        }
    }

    private const string OneClickArgs = "-5 --dns-addr 1.1.1.1 --dnsv6-addr 2606:4700:4700::1111";
    private bool _oneClickBusy;

    private void UpdateOneClickButton(bool running)
    {
        OneClickBtn.Content = running ? "Disconnect" : "Connect";
        OneClickBtn.Background = new SolidColorBrush(running ? Color.FromRgb(0xF3, 0x8B, 0xA8) : Color.FromRgb(0xA6, 0xE3, 0xA1));
    }

    private void AdvancedToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        bool showAdvanced = AdvancedTabs.Visibility != Visibility.Visible;
        AdvancedTabs.Visibility = showAdvanced ? Visibility.Visible : Visibility.Collapsed;
        SimplePanel.Visibility = showAdvanced ? Visibility.Collapsed : Visibility.Visible;
        AdvancedToggleBtn.Content = showAdvanced ? "Simple mode" : "Advanced mode";
    }

    private async void OneClickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_oneClickBusy || _serviceBusy) return;
        _oneClickBusy = true;
        OneClickBtn.IsEnabled = false;
        OneClickProgress.Visibility = Visibility.Visible;

        try
        {
            if (_dpiService.IsRunning)
            {
                OneClickStatus.Text = "Disconnecting...";
                await _dpiService.StopAsync();
                SetDpiRunningState(false);
                _steamResults.Clear();
                OneClickStatus.Text = "Disconnected.";
                AppendLog("GoodbyeDPI stopped by user.");
                return;
            }

            if (_serviceState == ServiceState.Running)
            {
                OneClickStatus.Text = "The GoodbyeDPI service is already running. Uninstall it to use Connect/Disconnect.";
                return;
            }

            string? exePath = _dpiService.FindExecutablePath();
            if (exePath == null)
            {
                OneClickStatus.Text = "goodbyedpi.exe not found. Switch to Advanced mode and select it.";
                return;
            }

            _steamResults.Clear();
            OneClickStatus.Text = "Starting GoodbyeDPI...";
            AppendLog($"Starting: {exePath} {OneClickArgs}");
            if (!_dpiService.Start(exePath, OneClickArgs))
            {
                OneClickStatus.Text = "Failed to start GoodbyeDPI.";
                return;
            }
            SetDpiRunningState(true);

            await Task.Delay(2000);
            if (!_dpiService.IsRunning)
            {
                OneClickStatus.Text = "GoodbyeDPI exited immediately. Check the log in Advanced mode.";
                return;
            }

            await DnsService.FlushDnsCacheAsync();
            OneClickStatus.Text = "Testing Steam connection...";
            await SteamConnectivityService.RunDiagnosticsAsync(r => Dispatcher.Invoke(() => _steamResults.Add(r)));

            int passed = _steamResults.Count(r => r.Success);
            OneClickStatus.Text = passed == _steamResults.Count
                ? $"Connected. Steam reachable ({passed}/{_steamResults.Count} checks passed)."
                : $"Connected, but Steam checks failed: {passed}/{_steamResults.Count} passed.";
        }
        catch (Exception ex)
        {
            OneClickStatus.Text = $"Error: {ex.Message}";
            AppendLog($"One-click error: {ex.Message}");
        }
        finally
        {
            _oneClickBusy = false;
            OneClickBtn.IsEnabled = true;
            OneClickProgress.Visibility = Visibility.Hidden;
        }
    }

    private string BuildAdvancedArgs()
    {
        string mode = "-1";
        if (DpiModeCombo.SelectedItem is ComboBoxItem item && item.Content is string content)
        {
            var parts = content.Split(':');
            if (parts.Length > 0)
                mode = parts[0].Trim();
        }

        string args = mode;
        if (EnableDpiDnsRedir.IsChecked == true)
        {
            string dnsIp = DpiDnsAddrInput.Text.Trim();
            string dnsPort = DpiDnsPortInput.Text.Trim();
            string dnsV6Ip = DpiDnsV6AddrInput.Text.Trim();
            if (!string.IsNullOrEmpty(dnsIp))
            {
                args += $" --dns-addr {dnsIp}";
                if (!string.IsNullOrEmpty(dnsPort))
                {
                    args += $" --dns-port {dnsPort}";
                }
            }
            if (!string.IsNullOrEmpty(dnsV6Ip))
            {
                args += $" --dnsv6-addr {dnsV6Ip}";
            }
        }
        return args;
    }

    private bool _serviceBusy;
    private ServiceState _serviceState = ServiceState.NotInstalled;

    private async Task RefreshServiceStateAsync()
    {
        _serviceState = await ServiceInstallService.QueryAsync();
        ServiceStatusSimple.Text = _serviceState switch
        {
            ServiceState.Running => "Service: running (starts with Windows)",
            ServiceState.Stopped => "Service: installed, stopped",
            ServiceState.Transitioning => "Service: installed",
            _ => "Service: not installed"
        };
        string label = _serviceState == ServiceState.NotInstalled ? "Install as service" : "Uninstall service";
        ServiceBtnSimple.Content = label;
        ServiceBtnAdv.Content = label;
    }

    private void ReportService(string message, bool success)
    {
        OneClickStatus.Text = message;
        FooterStatusText.Text = message;
        AppendLog(message);
        if (!success && AdvancedTabs.Visibility == Visibility.Visible)
            MessageBox.Show(message, "Service", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void ServiceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_serviceBusy || _oneClickBusy) return;
        _serviceBusy = true;
        ServiceBtnSimple.IsEnabled = false;
        ServiceBtnAdv.IsEnabled = false;
        OneClickBtn.IsEnabled = false;
        OneClickProgress.Visibility = Visibility.Visible;
        bool simple = SimplePanel.Visibility == Visibility.Visible;

        try
        {
            if (_serviceState != ServiceState.NotInstalled)
            {
                OneClickStatus.Text = "Removing service...";
                var (ok, msg) = await ServiceInstallService.UninstallAsync();
                ReportService(msg, ok);
                return;
            }

            string? exePath = simple ? _dpiService.FindExecutablePath() : ExePathInput.Text.Trim();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                ReportService("goodbyedpi.exe not found. Select it in Advanced mode first.", false);
                return;
            }

            if (_dpiService.IsRunning)
            {
                await _dpiService.StopAsync();
                SetDpiRunningState(false);
            }

            string args = simple ? OneClickArgs : BuildAdvancedArgs();
            OneClickStatus.Text = "Installing service...";
            AppendLog($"Installing service: {exePath} {args}");
            var (installed, installMsg) = await ServiceInstallService.InstallAsync(exePath, args);
            ReportService(installMsg, installed);

            if (installed && simple)
            {
                await DnsService.FlushDnsCacheAsync();
                OneClickStatus.Text = "Service running. Testing Steam connection...";
                _steamResults.Clear();
                await SteamConnectivityService.RunDiagnosticsAsync(r => Dispatcher.Invoke(() => _steamResults.Add(r)));
                int passed = _steamResults.Count(r => r.Success);
                OneClickStatus.Text = $"Service installed and running. Steam checks: {passed}/{_steamResults.Count} passed.";
            }
        }
        catch (Exception ex)
        {
            ReportService($"Service error: {ex.Message}", false);
        }
        finally
        {
            await RefreshServiceStateAsync();
            _serviceBusy = false;
            ServiceBtnSimple.IsEnabled = true;
            ServiceBtnAdv.IsEnabled = true;
            OneClickBtn.IsEnabled = true;
            OneClickProgress.Visibility = Visibility.Hidden;
        }
    }

    private void AppendLog(string text)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogsTextBox.AppendText($"[{timestamp}] {text}\n");
        LogsTextBox.ScrollToEnd();
    }
}
