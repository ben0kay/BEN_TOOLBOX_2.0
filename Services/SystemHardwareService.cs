using System;
using Microsoft.Win32;

namespace BEN_TOOLBOX_2_0.Services;

// Static hardware descriptions are read once, not on every dashboard refresh.
public static class SystemHardwareService
{
    public static string ReadCpuDescription()
    {
        if (!OperatingSystem.IsWindows()) return "Processor model unavailable";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);
            var name = key?.GetValue("ProcessorNameString") as string;
            return string.IsNullOrWhiteSpace(name) ? "Processor model unavailable"
                : name.Trim();
        }
        catch (Exception)
        {
            return "Processor model unavailable";
        }
    }

    public static string FormatGiB(ulong bytes) => (bytes / 1073741824d).ToString("N1") + " GiB";

    public static string FormatGiB(long bytes) => (bytes / 1073741824d).ToString("N1") + " GiB";
}
