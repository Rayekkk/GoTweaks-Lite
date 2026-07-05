using System;
using System.Management;
using NLog;

namespace XboxGamingBarHelper.Sidebar
{
    internal static class BrightnessManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal static int GetBrightness()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT Active, CurrentBrightness FROM WmiMonitorBrightness"))
                {
                    ManagementBaseObject fallback = null;
                    foreach (var obj in searcher.Get())
                    {
                        // Prefer the ACTIVE monitor's brightness (the one Windows' own slider tracks).
                        if (IsInstanceActive(obj))
                        {
                            return Convert.ToInt32(obj["CurrentBrightness"]);
                        }
                        fallback = obj;
                    }
                    if (fallback != null)
                    {
                        return Convert.ToInt32(fallback["CurrentBrightness"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BrightnessManager: GetBrightness failed: {ex.Message}");
            }
            return 50;
        }

        // WmiMonitorBrightness still enumerates the built-in panel (with Active=false) even when it
        // is off and an external monitor is in use — so instance presence alone is NOT enough. Match
        // Windows' own brightness slider, which grays out unless there is an ACTIVE brightness monitor.
        private static bool IsInstanceActive(ManagementBaseObject obj)
        {
            try
            {
                var active = obj["Active"];
                // If the provider omits Active on some hardware, assume active (don't over-disable).
                return active == null || Convert.ToBoolean(active);
            }
            catch
            {
                return true;
            }
        }

        internal static void SetBrightness(int level)
        {
            try
            {
                level = Math.Max(0, Math.Min(100, level));
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        obj.InvokeMethod("WmiSetBrightness", new object[] { 1, level });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"BrightnessManager: SetBrightness failed: {ex.Message}");
            }
        }

        internal static bool IsSupported()
        {
            // Authoritative signal: is the built-in panel in the ACTIVE display configuration?
            // WmiMonitorBrightness keeps enumerating the internal panel's (inactive) instance even in
            // "show only on external" mode and its per-instance Active flag proved unreliable on the
            // Legion Go 2 — so gate on QueryDisplayConfig, the same source Windows' own slider uses.
            bool? internalActive = XboxGamingBarHelper.Windows.User32.IsInternalPanelActive();
            Logger.Debug($"BrightnessManager: IsInternalPanelActive={(internalActive.HasValue ? internalActive.Value.ToString() : "unknown")}");
            if (internalActive == false)
            {
                return false; // built-in panel off (e.g. external monitor only) -> not controllable
            }

            // Internal panel active (or DisplayConfig query failed -> fall back): confirm the panel
            // actually exposes WMI brightness control before enabling the slider.
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
