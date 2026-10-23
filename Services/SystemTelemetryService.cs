using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BEN_TOOLBOX_2_0.Services;

// Lightweight, local read-only snapshots. No background timer: the visible dashboard owns sampling.
public sealed record PhysicalMemorySnapshot(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public double UsagePercent => TotalBytes == 0 ? 0 : 100d * UsedBytes / TotalBytes;
}

public sealed record DriveSpaceSnapshot(string Name, long TotalBytes, long AvailableBytes, int DriveCount)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : Math.Clamp(
        100d * (TotalBytes - AvailableBytes) / TotalBytes, 0, 100);
}

public sealed record SystemTelemetrySample(double? CpuPercent, PhysicalMemorySnapshot? Memory,
    DriveSpaceSnapshot? Drive);

public sealed class SystemTelemetryService
{
    private readonly object _cpuLock = new();
    private (ulong idle, ulong kernel, ulong user)? _previousCpu;

    public SystemTelemetrySample Sample(bool includeDrives)
    {
        // A read may fail independently; never turn missing samples into manufactured 0% usage.
        return new SystemTelemetrySample(ReadCpuUsage(), ReadPhysicalMemory(),
            includeDrives ? ReadSystemDrive() : null);
    }

    private double? ReadCpuUsage()
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (_cpuLock)
        {
            try
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
                var current = (idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
                var previous = _previousCpu;
                _previousCpu = current;
                if (!previous.HasValue) return null; // Requires two distinct samples.
                if (current.Item1 < previous.Value.idle ||
                    current.Item2 < previous.Value.kernel || current.Item3 < previous.Value.user)
                    return null;
                var idleDelta = current.Item1 - previous.Value.idle;
                var totalDelta = (current.Item2 - previous.Value.kernel)
                               + (current.Item3 - previous.Value.user);
                if (totalDelta == 0 || idleDelta > totalDelta) return null;
                // Kernel time includes idle time on Windows. This is system-wide usage.
                return Math.Clamp(100d * (totalDelta - idleDelta) / totalDelta, 0, 100);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static PhysicalMemorySnapshot? ReadPhysicalMemory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var info = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref info) || info.TotalPhysical == 0 ||
                info.AvailablePhysical > info.TotalPhysical) return null;
            return new PhysicalMemorySnapshot(info.TotalPhysical, info.AvailablePhysical);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DriveSpaceSnapshot? ReadSystemDrive()
    {
        try
        {
            // Prefer the OS drive but do not assume Windows was installed on C:.
            var preferredRoot = Path.GetPathRoot(Environment.SystemDirectory);
            DriveSpaceSnapshot? first = null;
            DriveSpaceSnapshot? preferred = null;
            int driveCount = 0;
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    long total = drive.TotalSize;
                    long available = drive.AvailableFreeSpace;
                    if (total <= 0 || available < 0 || available > total) continue;
                    driveCount++;
                    var entry = new DriveSpaceSnapshot(drive.Name, total, available, 0);
                    first ??= entry;
                    if (string.Equals(drive.Name, preferredRoot, StringComparison.OrdinalIgnoreCase))
                        preferred = entry;
                }
                catch (Exception) { /* One disconnected/inaccessible drive must not hide the others. */ }
            }
            var choice = preferred ?? first;
            return choice is null ? null : choice with { DriveCount = driveCount };
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out NativeFileTime idleTime,
        out NativeFileTime kernelTime, out NativeFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
