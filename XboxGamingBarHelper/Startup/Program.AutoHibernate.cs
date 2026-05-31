using NLog;
using Shared.Constants;
using Shared.Data;
using Shared.IPC;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using Windows.UI.Input.Preview.Injection;
using XboxGamingBarHelper.AMD;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.ControllerEmulation;
using XboxGamingBarHelper.Devices.Libraries.GPD;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.LosslessScaling;
using XboxGamingBarHelper.OnScreenDisplay;
using XboxGamingBarHelper.Performance;
using XboxGamingBarHelper.Power;
using XboxGamingBarHelper.Profile;
using XboxGamingBarHelper.RTSS;
using XboxGamingBarHelper.Settings;
using XboxGamingBarHelper.Systems;
using XboxGamingBarHelper.AutoTDP;
using XboxGamingBarHelper.DefaultGameProfiles;
using XboxGamingBarHelper.Labs;
using Shared.Enums;

namespace XboxGamingBarHelper
{
    internal partial class Program
    {
        private static void SetAutoHibernateEnabled(bool enabled)
        {
            autoHibernateEnabled = enabled;
            if (enabled)
            {
                // Restore the persisted AC/DC power-source choice before the timer can fire.
                // The widget re-sends it on connect, but this covers the widget-never-opened
                // case so we don't hibernate on AC when the user picked "DC Only" (issue #88 bug #3).
                // Use LocalSettingsHelper (UWP LocalSettings + JSON fallback) rather than
                // Properties.Settings.Default — the legacy app.config store doesn't round-trip in
                // the MSIX-deployed helper across reboots/updates (the value read back as the
                // default 0=Always, the actual bug kayti reported).
                try
                {
                    if (Settings.LocalSettingsHelper.TryGetValue<int>("AutoHibernateMode", out int savedMode))
                    {
                        autoHibernateMode = savedMode;
                        Logger.Info($"Auto Hibernate: restored persisted mode {savedMode} from LocalSettings");
                    }
                }
                catch (Exception ex) { Logger.Warn($"Could not load persisted AutoHibernateMode: {ex.Message}"); }

                if (autoHibernateTimer == null)
                {
                    autoHibernateTimer = new System.Threading.Timer(AutoHibernateIdleCheck, null, AutoHibernateCheckIntervalMs, AutoHibernateCheckIntervalMs);
                    Logger.Info("Auto Hibernate: Started idle monitoring timer");
                }
            }
            else
            {
                if (autoHibernateTimer != null)
                {
                    autoHibernateTimer.Dispose();
                    autoHibernateTimer = null;
                    Logger.Info("Auto Hibernate: Stopped idle monitoring timer");
                }
            }
        }

        private static void UpdateAutoHibernateIdleTimeout(int minutes)
        {
            if (minutes < 1) minutes = 1;
            autoHibernateIdleTimeoutMs = minutes * 60 * 1000;
            Logger.Info($"Auto Hibernate: Idle timeout set to {minutes} minutes");
        }

        private static void AutoHibernateIdleCheck(object state)
        {
            if (!autoHibernateEnabled) return;

            try
            {
                var lastInput = new LASTINPUTINFO();
                lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);

                if (GetLastInputInfo(ref lastInput))
                {
                    uint idleMs = (uint)Environment.TickCount - lastInput.dwTime;
                    if (idleMs < autoHibernateIdleTimeoutMs)
                    {
                        return;
                    }

                    if ((DateTime.UtcNow - lastAutoHibernateAttemptUtc).TotalMilliseconds < AutoHibernateCooldownMs)
                    {
                        return;
                    }

                    // Check power source mode: 1=AC Only, 2=DC Only.
                    //
                    // BatteryStatus, not PowerSupplyStatus. PowerSupplyStatus.Adequate means
                    // "AC line is supplying adequate current right now", not "AC is plugged
                    // in". When the battery hits a charge limit (Legion Go 2 80% cap, ASUS
                    // similar) the EC pauses charge current, and Windows downgrades
                    // PowerSupplyStatus to NotPresent/Inadequate even though the cable is
                    // physically connected. Using PowerSupplyStatus as the AC gate then
                    // fired DC-Only hibernate on a plugged-in device at the charge cap (and
                    // would have done the inverse for AC-Only: skipped a legitimate fire).
                    // BatteryStatus.Discharging is the authoritative "actually on battery"
                    // signal: anything else (Idle, Charging, NotPresent) means wall power.
                    var batteryStatus = global::Windows.System.Power.PowerManager.BatteryStatus;
                    var powerSupplyStatus = global::Windows.System.Power.PowerManager.PowerSupplyStatus;
                    bool runningOnBattery = batteryStatus == global::Windows.System.Power.BatteryStatus.Discharging;
                    Logger.Info($"Auto Hibernate: gate check mode={autoHibernateMode} BatteryStatus={batteryStatus} PowerSupplyStatus={powerSupplyStatus} runningOnBattery={runningOnBattery}");
                    if (autoHibernateMode == 1 && runningOnBattery) // AC Only, currently on DC
                    {
                        Logger.Info("Auto Hibernate: skipping AC-Only on DC");
                        return;
                    }
                    else if (autoHibernateMode == 2 && !runningOnBattery) // DC Only, currently on AC
                    {
                        Logger.Info("Auto Hibernate: skipping DC-Only on AC");
                        return;
                    }

                    // Avoid hibernating while a game is in the foreground
                    if (systemManager?.RunningGame?.Value.IsValid() == true && systemManager.RunningGame.Value.IsForeground)
                    {
                        Logger.Info("Auto Hibernate: Skipping - game is in foreground");
                        return;
                    }

                    lastAutoHibernateAttemptUtc = DateTime.UtcNow;
                    Logger.Info($"Auto Hibernate: Idle for {idleMs}ms, hibernating now");

                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown",
                            Arguments = "/h",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Auto Hibernate: Failed to initiate hibernate: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Auto Hibernate: Idle check failed: {ex.Message}");
            }
        }

    }
}
