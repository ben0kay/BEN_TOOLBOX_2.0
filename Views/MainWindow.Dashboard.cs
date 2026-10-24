using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

// One shared shell clock timer; system sampling remains dashboard-only.
// Keep system-specific calls in Services; D3 can reuse the same sampler for sparklines.
public partial class MainWindow
{
    private readonly SystemTelemetryService _dashboardTelemetry = new();
    private readonly Stopwatch _applicationUptime = Stopwatch.StartNew();
    private readonly DispatcherTimer _dashboardTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _dashboardCancellation;
    private bool _dashboardActive;
    private bool _dashboardSampleBusy;
    private int _dashboardGeneration;
    private int _dashboardSeconds;

    private void InitializeDashboard()
    {
        DashboardCpuModelText.Text = SystemHardwareService.ReadCpuDescription();
        DashboardCpuCoresText.Text = $"{Environment.ProcessorCount} logical processors available";
        DashboardProvidersText.Text = OperatingSystem.IsWindows()
            ? "PROVIDER / WINDOWS" : "PROVIDER / UNSUPPORTED OS";

        UpdateDashboardClock();
        _dashboardTimer.Tick += DashboardClockTick;
        SizeChanged += (_, _) => UpdateDashboardCardWidths();
        Opened += (_, _) =>
        {
            _dashboardTimer.Start();
            FitWindowToWorkingArea();
            UpdateDashboardCardWidths();
        };
        Closed += (_, _) =>
        {
            SetDashboardActive(false);
            _dashboardTimer.Stop();
            _dashboardTimer.Tick -= DashboardClockTick;
        };
    }

    // Prefer 1920x1080, but don't put the main window behind a taskbar or off a smaller screen.
    // Screens report physical pixels; Avalonia window Width/Height are device-independent pixels.
    private void FitWindowToWorkingArea()
    {
        var screen = Screens.Primary;
        if (screen is null || screen.Scaling <= 0) return;

        double workingWidth = screen.WorkingArea.Width / screen.Scaling;
        double workingHeight = screen.WorkingArea.Height / screen.Scaling;
        if (workingWidth >= MinWidth && Width > workingWidth) Width = workingWidth;
        if (workingHeight >= MinHeight && Height > workingHeight) Height = workingHeight;
    }

    private void UpdateDashboardCardWidths()
    {
        // Sidebar = 242; content margins = 56; reserve scroll bar and section padding.
        var available = Math.Max(280, Bounds.Width - 242 - 56 - 24 - 40);
        bool twoStatusColumns = available >= 900;
        var statusWidth = twoStatusColumns ? (available - 12) / 2 : available - 12;
        DashboardStatusCard.Width = statusWidth;
        DashboardTelemetryCard.Width = statusWidth;

        var cardWidth = available >= 940 ? (available - 24) / 4 : (available - 16) / 2;
        DashboardCpuHardwareCard.Width = cardWidth;
        DashboardGpuHardwareCard.Width = cardWidth;
        DashboardMemoryHardwareCard.Width = cardWidth;
        DashboardDriveHardwareCard.Width = cardWidth;

        // Keep module cards compact even on wide monitors; wrap into two columns on small screens.
        int moduleColumns = available >= 1040 ? 4 : available >= 510 ? 2 : 1;
        double moduleWidth = Math.Min(300, (available - 12 * moduleColumns) / moduleColumns);
        GameTile.Width = FilesTile.Width = InspectTile.Width = SettingsTile.Width = moduleWidth;
    }

    private void SetDashboardActive(bool active)
    {
        if (_dashboardActive == active) return;
        _dashboardActive = active;
        _dashboardGeneration++;

        if (active)
        {
            _dashboardCancellation = new CancellationTokenSource();
            // Returning from another section starts a fresh history; don't present stale charts.
            DashboardCpuGraph.ClearSamples();
            DashboardRamGraph.ClearSamples();
            DashboardCpuGraphEmpty.IsVisible = true;
            DashboardRamGraphEmpty.IsVisible = true;
            _dashboardTelemetry.ResetCpuBaseline();
            _dashboardSeconds = 0;
            UpdateDashboardClock();
            _dashboardTimer.Start();
            _ = RefreshDashboardTelemetryAsync(includeDrive: true);
        }
        else
        {
            _dashboardCancellation?.Cancel();
            _dashboardCancellation?.Dispose();
            _dashboardCancellation = null;
        }
    }

    private void DashboardClockTick(object? sender, EventArgs e)
    {
        UpdateDashboardClock();
        if (!_dashboardActive) return;
        _dashboardSeconds++;

        // CPU/RAM update every 2 seconds; disk every 30 seconds.
        if (_dashboardSeconds % 2 == 0)
            _ = RefreshDashboardTelemetryAsync(includeDrive: _dashboardSeconds % 30 == 0);
    }

    private void UpdateDashboardClock()
    {
        var now = DateTime.Now;
        DashboardTimeText.Text = now.ToString("HH:mm:ss");
        DashboardDateText.Text = now.ToString("ddd, dd MMM yyyy");
        var elapsed = _applicationUptime.Elapsed;
        DashboardUptimeText.Text = elapsed.Days > 0
            ? $"{elapsed.Days}d {elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private async Task RefreshDashboardTelemetryAsync(bool includeDrive)
    {
        if (!_dashboardActive || _dashboardSampleBusy) return;
        _dashboardSampleBusy = true;
        var generation = _dashboardGeneration;
        var token = _dashboardCancellation?.Token ?? CancellationToken.None;
        try
        {
            var sample = await Task.Run(() => _dashboardTelemetry.Sample(includeDrive), token);
            if (token.IsCancellationRequested || !_dashboardActive || generation != _dashboardGeneration)
                return;

            // The D2 sampling pipeline is the single source for both values and D3 charts.
            // Failed readings are added as gaps, never fabricated as a 0% measurement.
            DashboardCpuGraph.AddSample(sample.CpuPercent);
            DashboardRamGraph.AddSample(sample.Memory?.UsagePercent);
            DashboardCpuGraphEmpty.IsVisible = DashboardCpuGraph.ValidSampleCount == 0;
            DashboardRamGraphEmpty.IsVisible = DashboardRamGraph.ValidSampleCount == 0;

            if (sample.CpuPercent is double cpu)
            {
                var display = cpu.ToString("N0") + "%";
                DashboardCpuValueText.Text = display;
                DashboardCpuLoadText.Text = display;
                DashboardCpuDetailText.Text = "System-wide load  /  2s · last 2 min";
            }
            else
            {
                DashboardCpuValueText.Text = "—";
                DashboardCpuLoadText.Text = "—";
                DashboardCpuDetailText.Text = "CPU sample unavailable / initializing";
            }

            if (sample.Memory is { } memory)
            {
                var display = memory.UsagePercent.ToString("N0") + "%";
                DashboardRamValueText.Text = display;
                DashboardRamDetailText.Text =
                    $"{SystemHardwareService.FormatGiB(memory.UsedBytes)} / {SystemHardwareService.FormatGiB(memory.TotalBytes)} used · 2s history";
                DashboardMemoryModelText.Text = $"{SystemHardwareService.FormatGiB(memory.TotalBytes)} physical RAM";
                DashboardMemoryUsedText.Text = display;
                DashboardMemoryDetailText.Text =
                    $"{SystemHardwareService.FormatGiB(memory.AvailableBytes)} available";
            }
            else
            {
                DashboardRamValueText.Text = "—";
                DashboardRamDetailText.Text = "System memory unavailable";
                DashboardMemoryModelText.Text = "Physical RAM unavailable";
                DashboardMemoryUsedText.Text = "—";
                DashboardMemoryDetailText.Text = "Provider unavailable";
            }

            DashboardTelemetryStateText.Text = sample.CpuPercent.HasValue && sample.Memory is not null
                ? "LIVE READINGS" : sample.CpuPercent.HasValue || sample.Memory is not null
                ? "PARTIAL DATA" : "DATA UNAVAILABLE";

            if (includeDrive)
            {
                if (sample.Drive is { } drive)
                {
                    DashboardDriveNameText.Text = $"{drive.Name} / Fixed disk";
                    DashboardDriveCapacityText.Text = SystemHardwareService.FormatGiB(drive.TotalBytes);
                    DashboardDriveProgress.Value = drive.UsedPercent;
                    DashboardDriveDetailText.Text =
                        $"{SystemHardwareService.FormatGiB(drive.AvailableBytes)} free  /  {drive.UsedPercent:N0}% used"
                        + (drive.DriveCount > 1 ? $"  ·  {drive.DriveCount} drives" : "");
                }
                else
                {
                    DashboardDriveNameText.Text = "Drive unavailable";
                    DashboardDriveCapacityText.Text = "—";
                    DashboardDriveProgress.Value = 0;
                    DashboardDriveDetailText.Text = "No accessible fixed drive";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when leaving the dashboard or closing the window.
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Dashboard telemetry unavailable: " + exception.Message);
            if (_dashboardActive && generation == _dashboardGeneration)
                DashboardTelemetryStateText.Text = "DATA UNAVAILABLE";
        }
        finally
        {
            _dashboardSampleBusy = false;
        }
    }
}
