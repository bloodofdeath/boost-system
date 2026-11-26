using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

/// <summary>
/// Collects a read-only hardware and OS profile using Win32 APIs, WMI, and Windows Storage Management.
/// Properly disposes all unmanaged WMI handles and runs independent probes concurrently.
/// </summary>
public static class HardwareInspector
{
    public static async Task<HardwareProfile> CollectAsync(CancellationToken ct = default)
    {
        var hw = new HardwareProfile
        {
            SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            CpuThreads = Environment.ProcessorCount
        };

        // Run CPU sampling (200ms non-blocking delay) concurrently with WMI, Storage, Power, and Network probes
        var cpuLoadTask = MeasureCpuUsagePercentAsync(ct);

        var osAndBatteryTask = Task.Run(() =>
        {
            var osInfo = QueryOsInfo();
            var isLaptop = QueryHasBattery();
            return (osInfo, isLaptop);
        }, ct);

        var cpuAndGpuTask = Task.Run(() =>
        {
            var cpu = QueryCpuDetails();
            var gpus = QueryGpuDetails();
            return (cpu, gpus);
        }, ct);

        var storageAndPowerTask = Task.Run(() =>
        {
            var disks = QueryDisks();
            var scheme = PowerCfgHelper.GetActiveScheme();
            var net = QueryActiveNetwork();
            return (disks, scheme, net);
        }, ct);

        // RAM is an instant Win32 syscall
        PopulateMemoryInfo(hw);

        await Task.WhenAll(cpuLoadTask, osAndBatteryTask, cpuAndGpuTask, storageAndPowerTask).ConfigureAwait(false);

        hw.CpuLoadPercent = await cpuLoadTask.ConfigureAwait(false);

        var (os, isLaptop) = await osAndBatteryTask.ConfigureAwait(false);
        hw.OsCaption = os.Caption;
        hw.OsVersion = os.Version;
        hw.OsBuild = os.Build;
        hw.IsLaptop = isLaptop;

        var (cpuInfo, gpuList) = await cpuAndGpuTask.ConfigureAwait(false);
        hw.CpuName = cpuInfo.Name;
        hw.CpuCores = cpuInfo.Cores;
        hw.CpuThreads = cpuInfo.Threads;
        hw.CpuMaxClockMhz = cpuInfo.MaxClockMhz;

        foreach (var g in gpuList)
        {
            hw.GpuNames.Add(g.Name);
            if (string.IsNullOrWhiteSpace(hw.GpuDriverVersion))
            {
                hw.GpuDriverVersion = g.Driver;
            }
        }

        var (diskList, powerScheme, netInfo) = await storageAndPowerTask.ConfigureAwait(false);
        hw.Disks.AddRange(diskList);

        var sysLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";
        var sysDisk = hw.Disks.FirstOrDefault(d => d.DriveLetter.StartsWith(sysLetter, StringComparison.OrdinalIgnoreCase));
        hw.HasSsdSystemDrive = sysDisk is not null &&
            (sysDisk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
             sysDisk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase));

        hw.ActivePowerPlanGuid = powerScheme.Guid;
        hw.ActivePowerPlanName = powerScheme.Name;
        hw.ActiveNetworkAdapter = netInfo.Adapter;
        hw.NetworkLinkSpeed = netInfo.LinkSpeed;

        return hw;
    }

    private static (string Caption, string Version, string Build) QueryOsInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    return (
                        obj["Caption"]?.ToString()?.Trim() ?? "Windows",
                        obj["Version"]?.ToString()?.Trim() ?? string.Empty,
                        obj["BuildNumber"]?.ToString()?.Trim() ?? string.Empty);
                }
            }
        }
        catch { }

        return ("Windows", Environment.OSVersion.Version.ToString(), Environment.OSVersion.Version.Build.ToString());
    }

    private static bool QueryHasBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static (string Name, int Cores, int Threads, int MaxClockMhz) QueryCpuDetails()
    {
        var fallbackThreads = Environment.ProcessorCount;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString()?.Trim() ?? "Неизвестный процессор";
                    var cores = Convert.ToInt32(obj["NumberOfCores"] ?? fallbackThreads);
                    var threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? fallbackThreads);
                    var mhz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);
                    return (name, cores, threads, mhz);
                }
            }
        }
        catch { }

        return ("Неизвестный процессор", fallbackThreads, fallbackThreads, 0);
    }

    private static List<(string Name, string Driver)> QueryGpuDetails()
    {
        var gpus = new List<(string Name, string Driver)>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString()?.Trim();
                    var drv = obj["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        gpus.Add((name, drv));
                    }
                }
            }
        }
        catch { }

        return gpus
            .OrderByDescending(g =>
                g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            .ThenBy(g => g.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();
    }

    private static void PopulateMemoryInfo(HardwareProfile hw)
    {
        try
        {
            var mem = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                hw.TotalRamGb = Math.Round(mem.ullTotalPhys / 1024.0 / 1024.0 / 1024.0, 1);
                hw.FreeRamGb = Math.Round(mem.ullAvailPhys / 1024.0 / 1024.0 / 1024.0, 1);
                hw.UsedRamGb = Math.Round(Math.Max(0, hw.TotalRamGb - hw.FreeRamGb), 1);
            }
        }
        catch { }
    }

    private static List<DiskInfo> QueryDisks()
    {
        var list = new List<DiskInfo>();
        var driveMap = QueryLogicalDriveToPhysicalDiskMap();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var letter = drive.Name.TrimEnd('\\');
                var keyChar = letter.TrimEnd(':').ToUpperInvariant();
                var hasPhys = driveMap.TryGetValue(keyChar, out var phys);

                list.Add(new DiskInfo
                {
                    DriveLetter = letter,
                    Model = hasPhys ? phys.Model : "Локальный диск",
                    MediaType = hasPhys ? phys.MediaType : "SSD/Диск",
                    TotalGb = Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 1),
                    FreeGb = Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 1)
                });
            }
        }
        catch { }

        return list;
    }

    private static Dictionary<string, (string Model, string MediaType)> QueryLogicalDriveToPhysicalDiskMap()
    {
        var result = new Dictionary<string, (string Model, string MediaType)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var physicalByNumber = new Dictionary<int, (string Model, string MediaType)>();
            using (var diskSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk")))
            using (var disks = diskSearcher.Get())
            {
                foreach (ManagementBaseObject obj in disks)
                {
                    using (obj)
                    {
                        if (!int.TryParse(obj["DeviceId"]?.ToString(), out var diskNum)) continue;
                        var model = obj["FriendlyName"]?.ToString()?.Trim() ?? "Диск";
                        var mediaCode = Convert.ToInt32(obj["MediaType"] ?? 0);
                        var busCode = Convert.ToInt32(obj["BusType"] ?? 0);

                        var mediaType = busCode == 17 ? "NVMe SSD" : mediaCode switch
                        {
                            3 => "HDD",
                            4 => busCode == 11 ? "SATA SSD" : "SSD",
                            5 => "SCM",
                            _ => model.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "NVMe SSD" :
                                 model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD" : "Диск"
                        };

                        physicalByNumber[diskNum] = (model, mediaType);
                    }
                }
            }

            using (var partSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, DiskNumber FROM MSFT_Partition")))
            using (var partitions = partSearcher.Get())
            {
                foreach (ManagementBaseObject obj in partitions)
                {
                    using (obj)
                    {
                        var letterRaw = obj["DriveLetter"];
                        if (letterRaw is null) continue;

                        char c = letterRaw is char ch ? ch : Convert.ToChar(letterRaw);
                        if (!char.IsLetter(c)) continue;

                        var diskNum = Convert.ToInt32(obj["DiskNumber"] ?? -1);
                        if (physicalByNumber.TryGetValue(diskNum, out var phys))
                        {
                            result[c.ToString().ToUpperInvariant()] = phys;
                        }
                    }
                }
            }
        }
        catch { }

        return result;
    }

    private static (string Adapter, string LinkSpeed) QueryActiveNetwork()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                            !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            if (nic is not null)
            {
                var speed = nic.Speed >= 1_000_000_000
                    ? $"{nic.Speed / 1_000_000_000.0:0.#} Гбит/с"
                    : nic.Speed > 0
                        ? $"{nic.Speed / 1_000_000.0:0.#} Мбит/с"
                        : string.Empty;
                return (nic.Description, speed);
            }
        }
        catch { }

        return ("Не определён", string.Empty);
    }

    private static async Task<double> MeasureCpuUsagePercentAsync(CancellationToken ct)
    {
        try
        {
            if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return 0;
            await Task.Delay(200, ct).ConfigureAwait(false);
            if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return 0;

            ulong idleDiff = idle2 - idle1;
            ulong kernelDiff = kernel2 - kernel1;
            ulong userDiff = user2 - user1;
            ulong total = kernelDiff + userDiff;
            if (total == 0) return 0;

            var busy = total > idleDiff ? (double)(total - idleDiff) / total * 100.0 : 0;
            return Math.Round(Math.Clamp(busy, 0, 100), 1);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);
}
