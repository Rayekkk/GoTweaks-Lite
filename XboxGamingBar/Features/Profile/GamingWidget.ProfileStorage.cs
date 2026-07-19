using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        private void SaveProfileToStorage(string profileName, PerformanceProfile profile)
        {
            Logger.Debug($"Ignoring legacy widget profile save for {profileName}; helper owns persistence");
        }

        private void LoadProfileFromStorage(string profileName, PerformanceProfile profile)
        {
            if (helperProfileCatalog.TryGetValue(profileName, out var confirmed))
            {
                var copy = confirmed.Clone();
                profile.TDP = copy.TDP; profile.TDPFast = copy.TDPFast; profile.TDPPeak = copy.TDPPeak;
                profile.CPUBoost = copy.CPUBoost; profile.CPUEPP = copy.CPUEPP; profile.MaxCPUState = copy.MaxCPUState; profile.MinCPUState = copy.MinCPUState;
                profile.LegionPerformanceMode = copy.LegionPerformanceMode; profile.FPSLimitEnabled = copy.FPSLimitEnabled; profile.FPSLimitValue = copy.FPSLimitValue;
                profile.OSPowerMode = copy.OSPowerMode; profile.HDREnabled = copy.HDREnabled; profile.Resolution = copy.Resolution; profile.RefreshRate = copy.RefreshRate;
                profile.FluidMotionFrames = copy.FluidMotionFrames; profile.RadeonSuperResolution = copy.RadeonSuperResolution; profile.ImageSharpening = copy.ImageSharpening;
                profile.RadeonAntiLag = copy.RadeonAntiLag; profile.RadeonBoost = copy.RadeonBoost; profile.RadeonChill = copy.RadeonChill;
                return;
            }
            // No LocalSettings fallback: a missing helper-cache entry is intentionally blank.
            return;
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Containers.ContainsKey($"Profile_{profileName}"))
            {
                var container = settings.Containers[$"Profile_{profileName}"];

                // When the widget-side Profile_* LocalSettings container doesn't have a TDP
                // entry, fall back to the helper's current TDP (authoritative across reboots
                // via global.xml) rather than a hardcoded 15 W — otherwise non-Legion devices
                // reset TDP to 15 on every cold start (issues #74, #79).
                profile.TDP = container.Values.ContainsKey("TDP") ? (double)container.Values["TDP"] : (tdp?.Value ?? 15);
                // SPPT/FPPT for Custom mode. Old profiles (saved before these existed) fall back to
                // TDP (SPL) so they keep the legacy "all three equal" behaviour.
                profile.TDPFast = container.Values.ContainsKey("TDPFast") ? (double)container.Values["TDPFast"] : profile.TDP;
                profile.TDPPeak = container.Values.ContainsKey("TDPPeak") ? (double)container.Values["TDPPeak"] : profile.TDP;
                // Use current system values as defaults for EPP and CPU Boost (synced from helper)
                profile.CPUBoost = container.Values.ContainsKey("CPUBoost") ? (bool)container.Values["CPUBoost"] : (cpuBoost?.Value ?? false);
                profile.CPUEPP = container.Values.ContainsKey("CPUEPP") ? (double)container.Values["CPUEPP"] : (cpuEPP?.Value ?? 80);
                profile.MaxCPUState = container.Values.ContainsKey("MaxCPUState") ? (int)container.Values["MaxCPUState"] : 100;
                profile.MinCPUState = container.Values.ContainsKey("MinCPUState") ? (int)container.Values["MinCPUState"] : 5;
                profile.FluidMotionFrames = container.Values.ContainsKey("FluidMotionFrames") ? (bool)container.Values["FluidMotionFrames"] : false;
                profile.RadeonSuperResolution = container.Values.ContainsKey("RadeonSuperResolution") ? (bool)container.Values["RadeonSuperResolution"] : false;
                profile.RadeonSuperResolutionSharpness = container.Values.ContainsKey("RadeonSuperResolutionSharpness") ? (double)container.Values["RadeonSuperResolutionSharpness"] : 80;
                profile.ImageSharpening = container.Values.ContainsKey("ImageSharpening") ? (bool)container.Values["ImageSharpening"] : false;
                profile.ImageSharpeningSharpness = container.Values.ContainsKey("ImageSharpeningSharpness") ? (double)container.Values["ImageSharpeningSharpness"] : 80;
                profile.RadeonAntiLag = container.Values.ContainsKey("RadeonAntiLag") ? (bool)container.Values["RadeonAntiLag"] : false;
                profile.RadeonBoost = container.Values.ContainsKey("RadeonBoost") ? (bool)container.Values["RadeonBoost"] : false;
                profile.RadeonBoostResolution = container.Values.ContainsKey("RadeonBoostResolution") ? (double)container.Values["RadeonBoostResolution"] : 0;
                profile.RadeonChill = container.Values.ContainsKey("RadeonChill") ? (bool)container.Values["RadeonChill"] : false;
                profile.RadeonChillMinFPS = container.Values.ContainsKey("RadeonChillMinFPS") ? (double)container.Values["RadeonChillMinFPS"] : 30;
                profile.RadeonChillMaxFPS = container.Values.ContainsKey("RadeonChillMaxFPS") ? (double)container.Values["RadeonChillMaxFPS"] : 60;
                profile.FPSLimitEnabled = container.Values.ContainsKey("FPSLimitEnabled") ? (bool)container.Values["FPSLimitEnabled"] : false;
                profile.FPSLimitValue = container.Values.ContainsKey("FPSLimitValue") ? (int)container.Values["FPSLimitValue"] : 60;
                profile.OSPowerMode = container.Values.ContainsKey("OSPowerMode") ? (int)container.Values["OSPowerMode"] : 1;
                // Only load LegionPerformanceMode if it exists in storage - keep profile's existing value otherwise
                // This preserves the default (Balanced=2) for new profiles but doesn't override if storage key is missing
                if (container.Values.ContainsKey("LegionPerformanceMode"))
                {
                    profile.LegionPerformanceMode = (int)container.Values["LegionPerformanceMode"];
                }
                // Load TDPModeIndex for custom presets (-1 means use LegionPerformanceMode to determine index)
                profile.TDPModeIndex = container.Values.ContainsKey("TDPModeIndex") ? (int)container.Values["TDPModeIndex"] : -1;
                profile.HDREnabled = container.Values.ContainsKey("HDREnabled") ? (bool)container.Values["HDREnabled"] : false;
                profile.Resolution = container.Values.ContainsKey("Resolution") ? (string)container.Values["Resolution"] : "";
                profile.RefreshRate = container.Values.ContainsKey("RefreshRate") ? (int?)container.Values["RefreshRate"] : null;
                profile.OverlayLevel = container.Values.ContainsKey("OverlayLevel") ? (int)container.Values["OverlayLevel"] : 0;

                Logger.Info($"Loaded {profileName} profile from storage");
            }
        }

    }
}
