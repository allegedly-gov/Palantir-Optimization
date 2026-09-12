using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PalantirOptimization
{
    public class OptimizationConfig
    {
        public bool UnlockFPS { get; set; }
        public bool ClearCache { get; set; }
        public bool HighPerformance { get; set; }
        public bool NetworkOpt { get; set; }
        public bool TimerResolution { get; set; }
        public bool DisableGameDVR { get; set; }
        public bool ClearShaderCache { get; set; }
        public bool AdvancedNetwork { get; set; }
        public bool NetworkQoS { get; set; }
        public bool AudioLatency { get; set; }
        public bool CloudflareDNS { get; set; }
    }

    public class OptimizerCore
    {
        private const string RobloxProcessName = "RobloxPlayerBeta";
        private const string QosPolicyName = "RobloxQoS";

        private static readonly string StateDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PalantirOptimizer");
        private static readonly string StateFile = Path.Combine(StateDirectory, "state.json");
        private static readonly string BackupDirectory = Path.Combine(StateDirectory, "Backups");

        [DllImport("ntdll.dll", EntryPoint = "NtSetTimerResolution")]
        private static extern int NtSetTimerResolution(
            uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        private sealed class OptimizationState
        {
            public bool HasState { get; set; }
            public string? OriginalPowerPlanGuid { get; set; }

            public int? OriginalRobloxPriority { get; set; }

            public int? GameDvrEnabled { get; set; }
            public int? AppCaptureEnabled { get; set; }
            public bool? NetworkThrottlingIndexWasPresent { get; set; }
            public int OriginalNetworkThrottlingIndex { get; set; }

            public bool? AudioLatencyWasPresent { get; set; }
            public int OriginalAudioLatency { get; set; }

            public bool QosPolicyCreated { get; set; }
            public bool DnsChanged { get; set; }

            public bool TimerResolutionRequested { get; set; }

            public List<FileBackup> FileBackups { get; set; } = new List<FileBackup>();
        }

        private sealed class FileBackup
        {
            public string OriginalPath { get; set; } = "";
            public string BackupPath { get; set; } = "";
        }

        public static async Task<(bool success, string message)> OptimizeAsync(OptimizationConfig config)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var state = LoadState();

                    if (state.HasState)
                        return (false, "An optimization state already exists. Use RESTORE before optimizing again.");

                    state.HasState = true;
                    var actionsTaken = new List<string>();

                    var processes = Process.GetProcessesByName(RobloxProcessName);
                    if (processes.Length > 0)
                    {
                        var process = processes.First();

                        try
                        {
                            state.OriginalRobloxPriority = (int)process.PriorityClass;
                            process.PriorityClass = ProcessPriorityClass.High;
                            actionsTaken.Add("Priority set to High");
                        }
                        catch
                        {
                            actionsTaken.Add("Could not change Roblox priority");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                    else
                    {
                        actionsTaken.Add("Roblox not running (Priority skipped)");
                    }

                    string localAppData =
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string robloxPath = Path.Combine(localAppData, "Roblox");

                    if (config.ClearCache && Directory.Exists(robloxPath))
                    {
                        string logsPath = Path.Combine(robloxPath, "logs");
                        string downloadsPath = Path.Combine(robloxPath, "Downloads");

                        int filesBackedUp = BackupAndDeleteFiles(logsPath, state);
                        filesBackedUp += BackupAndDeleteFiles(downloadsPath, state);

                        actionsTaken.Add($"Cleared {filesBackedUp} Roblox cache files");
                    }

                    string robloxVersionsPath = Path.Combine(robloxPath, "Versions");
                    if (Directory.Exists(robloxVersionsPath))
                    {
                        var versionDirs = Directory.GetDirectories(robloxVersionsPath);

                        foreach (var dir in versionDirs)
                        {
                            string exePath = Path.Combine(dir, "RobloxPlayerBeta.exe");
                            if (!File.Exists(exePath))
                                continue;

                            string clientSettingsDir = Path.Combine(dir, "ClientSettings");
                            Directory.CreateDirectory(clientSettingsDir);

                            string jsonPayload =
                                "{\n" +
                                "  \"FFlagHandleAltEnterFullscreenManually\": \"False\",\n" +
                                "  \"DFFlagDebugPauseVoxelizer\": \"True\",\n" +
                                "  \"FFlagDebugSkyGray\": \"True\",\n" +
                                "  \"FIntFRMMinGrassDistance\": \"0\",\n" +
                                "  \"FIntFRMMaxGrassDistance\": \"0\",\n" +
                                "  \"FIntGrassMovementReducedMotionFactor\": \"0\",\n" +
                                "  \"DFIntCSGLevelOfDetailSwitchingDistance\": \"222\",\n" +
                                "  \"DFIntCSGLevelOfDetailSwitchingDistanceL12\": \"166\",\n" +
                                "  \"DFIntCSGLevelOfDetailSwitchingDistanceL23\": \"111\",\n" +
                                "  \"DFIntCSGLevelOfDetailSwitchingDistanceL34\": \"55\",\n" +
                                "  \"DFFlagDisableDPIScale\": \"True\",\n" +
                                "  \"FFlagDebugGraphicsPreferVulkan\": \"True\",\n" +
                                "  \"DFIntDebugFRMQualityLevelOverride\": \"1\"";

                            if (config.UnlockFPS)
                                jsonPayload += ",\n  \"DFIntTaskSchedulerTargetFps\": \"240\"";

                            if (config.NetworkOpt)
                            {
                                jsonPayload +=
                                    ",\n  \"FFlagDebugDisableTelemetry\": \"True\"" +
                                    ",\n  \"FIntRakNetPingMultiplier\": \"1\"";
                            }

                            jsonPayload += "\n}";

                            string jsonPath1 = Path.Combine(clientSettingsDir, "ClientSettings.json");
                            string jsonPath2 = Path.Combine(clientSettingsDir, "ClientAppSettings.json");

                            BackupFileIfPresent(jsonPath1, state);
                            BackupFileIfPresent(jsonPath2, state);

                            File.WriteAllText(jsonPath1, jsonPayload);
                            File.WriteAllText(jsonPath2, jsonPayload);

                            actionsTaken.Add("Injected FastFlags");
                            break;
                        }
                    }

                    if (config.HighPerformance)
                    {
                        state.OriginalPowerPlanGuid = GetActivePowerPlanGuid();

                        var psi = new ProcessStartInfo(
                            "powercfg",
                            "-setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        Process.Start(psi)?.WaitForExit();
                        actionsTaken.Add("Set High Performance Power Mode");
                    }

                    if (config.TimerResolution)
                    {
                        int result = NtSetTimerResolution(5000, true, out _);
                        if (result == 0)
                        {
                            state.TimerResolutionRequested = true;
                            actionsTaken.Add("Set system timer to 0.5ms");
                        }
                        else
                        {
                            actionsTaken.Add($"Timer resolution request failed (NTSTATUS 0x{result:X8})");
                        }
                    }

                    if (config.DisableGameDVR)
                    {
                        try
                        {
                            using (var key = Registry.CurrentUser.OpenSubKey(
                                @"System\GameConfigStore", writable: true))
                            {
                                if (key != null)
                                {
                                    state.GameDvrEnabled = ReadDword(key, "GameDVR_Enabled");
                                    key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                                }
                            }

                            using (var key = Registry.CurrentUser.OpenSubKey(
                                @"Software\Microsoft\Windows\CurrentVersion\GameDVR", writable: true))
                            {
                                if (key != null)
                                {
                                    state.AppCaptureEnabled = ReadDword(key, "AppCaptureEnabled");
                                    key.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                                }
                            }

                            actionsTaken.Add("Disabled Game DVR/Xbox Bar");
                        }
                        catch
                        {
                            actionsTaken.Add("Could not modify Game DVR settings");
                        }
                    }

                    if (config.ClearShaderCache)
                    {
                        int shadersBackedUp = 0;
                        string[] cacheDirs =
                        {
                            Path.Combine(localAppData, "NVIDIA", "DXCache"),
                            Path.Combine(localAppData, "NVIDIA", "GLCache"),
                            Path.Combine(localAppData, "AMD", "DxCache")
                        };

                        foreach (var cDir in cacheDirs)
                            shadersBackedUp += BackupAndDeleteFiles(cDir, state);

                        actionsTaken.Add($"Cleared {shadersBackedUp} GPU shader cache files");
                    }

                    if (config.AdvancedNetwork)
                    {
                        try
                        {
                            using (var key = Registry.LocalMachine.OpenSubKey(
                                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                                writable: true))
                            {
                                if (key != null)
                                {
                                    object? value = key.GetValue("NetworkThrottlingIndex", null);
                                    state.NetworkThrottlingIndexWasPresent = value != null;

                                    if (value != null)
                                        state.OriginalNetworkThrottlingIndex = Convert.ToInt32(value);

                                    key.SetValue(
                                        "NetworkThrottlingIndex",
                                        unchecked((int)0xFFFFFFFF),
                                        RegistryValueKind.DWord);
                                }
                            }

                            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };

                            Process.Start(psi)?.WaitForExit();
                            actionsTaken.Add("Disabled network throttling and flushed DNS");
                        }
                        catch
                        {
                            actionsTaken.Add("Could not apply advanced network tweaks");
                        }
                    }

                    if (config.NetworkQoS)
                    {
                        try
                        {
                            var ps = new ProcessStartInfo(
                                "powershell",
                                "-NoProfile -Command " +
                                "\"New-NetQosPolicy -Name 'RobloxQoS' " +
                                "-AppPathNameMatchCondition '*RobloxPlayerBeta.exe' " +
                                "-PriorityValue8021Action 1 -PolicyStore ActiveStore\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            Process.Start(ps)?.WaitForExit();
                            state.QosPolicyCreated = true;
                            actionsTaken.Add("Applied Network QoS policy for Roblox");
                        }
                        catch
                        {
                            actionsTaken.Add("Could not apply Network QoS policy");
                        }
                    }

                    if (config.AudioLatency)
                    {
                        try
                        {
                            using (var key = Registry.CurrentUser.OpenSubKey(
                                @"Software\Microsoft\Multimedia\Audio", writable: true))
                            {
                                if (key != null)
                                {
                                    object? value = key.GetValue("Latency", null);
                                    state.AudioLatencyWasPresent = value != null;

                                    if (value != null)
                                        state.OriginalAudioLatency = Convert.ToInt32(value);

                                    key.SetValue("Latency", 0, RegistryValueKind.DWord);
                                }
                            }

                            var psAudio = new ProcessStartInfo(
                                "powershell",
                                "-NoProfile -Command \"Restart-Service -Name Audiosrv -Force\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            Process.Start(psAudio)?.WaitForExit();
                            actionsTaken.Add("Enabled low-latency audio mode");
                        }
                        catch
                        {
                            actionsTaken.Add("Could not apply audio latency setting");
                        }
                    }

                    if (config.CloudflareDNS)
                    {
                        try
                        {
                            var psDns = new ProcessStartInfo(
                                "powershell",
                                "-NoProfile -Command \"Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | Set-DnsClientServerAddress -ServerAddresses '1.1.1.1','1.0.0.1'\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            Process.Start(psDns)?.WaitForExit();
                            state.DnsChanged = true;
                            actionsTaken.Add("Switched to Cloudflare DNS (1.1.1.1)");
                        }
                        catch
                        {
                            actionsTaken.Add("Could not change DNS settings");
                        }
                    }

                    SaveState(state);

                    return (true, string.Join("\n", actionsTaken));
                }
                catch (UnauthorizedAccessException)
                {
                    return (false,
                        "Access Denied. Please ensure you run this application as Administrator.");
                }
                catch (Exception ex)
                {
                    return (false, $"An error occurred: {ex.Message}");
                }
            });
        }

        public static async Task<(bool success, string message)> RestoreAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var state = LoadState();

                    if (!state.HasState)
                        return (true, "Nothing to restore.");

                    var restored = new List<string>();
                    var failed = new List<string>();

                    if (state.TimerResolutionRequested)
                    {
                        try
                        {
                            NtSetTimerResolution(5000, false, out _);
                            restored.Add("system timer");
                        }
                        catch
                        {
                            failed.Add("system timer");
                        }
                    }

                    if (state.OriginalRobloxPriority.HasValue)
                    {
                        try
                        {
                            var processes = Process.GetProcessesByName(RobloxProcessName);
                            foreach (var process in processes)
                            {
                                try
                                {
                                    process.PriorityClass =
                                        (ProcessPriorityClass)state.OriginalRobloxPriority.Value;
                                }
                                finally
                                {
                                    process.Dispose();
                                }
                            }

                            restored.Add("Roblox priority");
                        }
                        catch
                        {
                            failed.Add("Roblox priority");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(state.OriginalPowerPlanGuid))
                    {
                        try
                        {
                            var psi = new ProcessStartInfo(
                                "powercfg",
                                $"-setactive {state.OriginalPowerPlanGuid}")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };

                            Process.Start(psi)?.WaitForExit();
                            restored.Add("power plan");
                        }
                        catch
                        {
                            failed.Add("power plan");
                        }
                    }

                    try
                    {
                        if (state.GameDvrEnabled.HasValue)
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(
                                @"System\GameConfigStore", writable: true);

                            if (key != null)
                                key.SetValue(
                                    "GameDVR_Enabled",
                                    state.GameDvrEnabled.Value,
                                    RegistryValueKind.DWord);
                        }

                        if (state.AppCaptureEnabled.HasValue)
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(
                                @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                                writable: true);

                            if (key != null)
                                key.SetValue(
                                    "AppCaptureEnabled",
                                    state.AppCaptureEnabled.Value,
                                    RegistryValueKind.DWord);
                        }

                        if (state.GameDvrEnabled.HasValue || state.AppCaptureEnabled.HasValue)
                            restored.Add("Game DVR settings");
                    }
                    catch
                    {
                        failed.Add("Game DVR settings");
                    }

                    try
                    {
                        if (state.NetworkThrottlingIndexWasPresent.HasValue)
                        {
                            using var key = Registry.LocalMachine.OpenSubKey(
                                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                                writable: true);

                            if (key != null)
                            {
                                if (state.NetworkThrottlingIndexWasPresent.Value)
                                    key.SetValue(
                                        "NetworkThrottlingIndex",
                                        state.OriginalNetworkThrottlingIndex,
                                        RegistryValueKind.DWord);
                                else
                                    key.DeleteValue("NetworkThrottlingIndex", false);

                                restored.Add("network throttling setting");
                            }
                        }
                    }
                    catch
                    {
                        failed.Add("network throttling setting");
                    }

                    if (state.QosPolicyCreated)
                    {
                        try
                        {
                            var ps = new ProcessStartInfo(
                                "powershell",
                                "-NoProfile -Command " +
                                "\"Remove-NetQosPolicy -Name 'RobloxQoS' -PolicyStore ActiveStore -Confirm:$false\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = true,
                                Verb = "runas"
                            };

                            Process.Start(ps)?.WaitForExit();
                            restored.Add("Roblox QoS policy");
                        }
                        catch
                        {
                            failed.Add("Roblox QoS policy");
                        }
                    }

                    try
                    {
                        if (state.AudioLatencyWasPresent.HasValue)
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(
                                @"Software\Microsoft\Multimedia\Audio", writable: true);

                            if (key != null)
                            {
                                if (state.AudioLatencyWasPresent.Value)
                                    key.SetValue(
                                        "Latency",
                                        state.OriginalAudioLatency,
                                        RegistryValueKind.DWord);
                                else
                                    key.DeleteValue("Latency", false);

                                restored.Add("audio latency setting");
                            }
                        }
                    }
                    catch
                    {
                        failed.Add("audio latency setting");
                    }

                    foreach (var backup in state.FileBackups.AsEnumerable().Reverse())
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalPath)!);

                            if (File.Exists(backup.OriginalPath))
                                File.Delete(backup.OriginalPath);

                            if (File.Exists(backup.BackupPath))
                            {
                                File.Copy(backup.BackupPath, backup.OriginalPath, true);
                                File.Delete(backup.BackupPath);
                            }

                            restored.Add(Path.GetFileName(backup.OriginalPath));
                        }
                        catch
                        {
                            failed.Add(Path.GetFileName(backup.OriginalPath));
                        }
                    }

                    if (state.DnsChanged)
                    {
                        try
                        {
                            var psResetDns = new ProcessStartInfo(
                                "powershell",
                                "-NoProfile -Command \"Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | Set-DnsClientServerAddress -ResetServerAddresses\"")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = true,
                                Verb = "runas"
                            };
                            Process.Start(psResetDns)?.WaitForExit();
                            restored.Add("DNS settings");
                        }
                        catch
                        {
                            failed.Add("DNS settings");
                        }
                    }

                    try
                    {
                        if (Directory.Exists(BackupDirectory) &&
                            !Directory.EnumerateFileSystemEntries(BackupDirectory).Any())
                        {
                            Directory.Delete(BackupDirectory, true);
                        }
                    }
                    catch
                    {
                    }

                    DeleteState();

                    string message = restored.Count == 0
                        ? "Restore completed."
                        : "Restored:\n" + string.Join("\n", restored.Distinct());

                    if (failed.Count > 0)
                    {
                        message += "\n\nCould not restore:\n" +
                                   string.Join("\n", failed.Distinct());
                    }

                    return (failed.Count == 0, message);
                }
                catch (UnauthorizedAccessException)
                {
                    return (false,
                        "Access Denied. Please ensure you run this application as Administrator.");
                }
                catch (Exception ex)
                {
                    return (false, $"Restore error: {ex.Message}");
                }
            });
        }

        private static OptimizationState LoadState()
        {
            try
            {
                if (!File.Exists(StateFile))
                    return new OptimizationState();

                string json = File.ReadAllText(StateFile);
                return JsonSerializer.Deserialize<OptimizationState>(json)
                       ?? new OptimizationState();
            }
            catch
            {
                return new OptimizationState();
            }
        }

        private static void SaveState(OptimizationState state)
        {
            Directory.CreateDirectory(StateDirectory);

            string json = JsonSerializer.Serialize(
                state,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(StateFile, json);
        }

        private static void DeleteState()
        {
            try
            {
                if (File.Exists(StateFile))
                    File.Delete(StateFile);
            }
            catch
            {
            }
        }

        private static int? ReadDword(RegistryKey key, string valueName)
        {
            object? value = key.GetValue(valueName, null);
            return value == null ? null : Convert.ToInt32(value);
        }

        private static string? GetActivePowerPlanGuid()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var match = Regex.Match(
                    output,
                    @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");

                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        private static void BackupFileIfPresent(string originalPath, OptimizationState state)
        {
            if (!File.Exists(originalPath))
                return;

            Directory.CreateDirectory(BackupDirectory);

            string backupPath = Path.Combine(
                BackupDirectory,
                Guid.NewGuid().ToString("N") + ".bak");

            File.Copy(originalPath, backupPath, true);

            state.FileBackups.Add(new FileBackup
            {
                OriginalPath = originalPath,
                BackupPath = backupPath
            });
        }

        private static int BackupAndDeleteFiles(string directory, OptimizationState state)
        {
            if (!Directory.Exists(directory))
                return 0;

            int count = 0;

            foreach (string file in Directory.GetFiles(directory))
            {
                try
                {
                    BackupFileIfPresent(file, state);

                    if (File.Exists(file))
                        File.Delete(file);

                    count++;
                }
                catch
                {
                }
            }

            return count;
        }
    }
}
