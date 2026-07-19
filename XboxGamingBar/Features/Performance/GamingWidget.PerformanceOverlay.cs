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

        private void PerformanceOverlayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PerformanceOverlayComboBox != null && PerformanceOverlaySlider != null)
            {
                // Sync the hidden slider value with the selected combobox item
                int index = PerformanceOverlayComboBox.SelectedIndex;
                if (index >= 0)
                {
                    // [2.0 rebuild - code review fix] osdProvider==1 (AMD) branch must not run
                    // during a programmatic/helper-driven ComboBox update - since slice 1 made OSD
                    // level helper-authoritative, PerformanceOverlaySlider_ValueChanged now also
                    // fires from a helper push (BatchGet on connect, a hotkey toggle, etc.), setting
                    // isLoadingPerformanceOverlaySetting=true around the ComboBox.SelectedIndex
                    // assignment that triggers this handler. Without this guard, that helper-pushed
                    // RTSS-style numeric OSD level got misread as "the user wants to toggle the AMD
                    // overlay", injecting a real Ctrl+Shift+O keystroke via SendAMDOverlayToggle()
                    // on every connect/sync when the AMD provider is selected.
                    if (osdProvider == 1 && !isLoadingPerformanceOverlaySetting) // AMD
                    {
                        // For AMD: index 0 = Off, index 1-3 maps to AMD levels
                        if (index == 0 && amdOverlayLevel > 0)
                        {
                            // Turn off AMD overlay
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 0;
                            SaveAMDOverlayLevel();
                            Logger.Info("AMD Overlay toggled OFF via ComboBox");
                        }
                        else if (index > 0 && amdOverlayLevel == 0)
                        {
                            // Turn on AMD overlay (starts at level 1)
                            SendAMDOverlayToggle();
                            amdOverlayLevel = 1;
                            SaveAMDOverlayLevel();
                            Logger.Info("AMD Overlay toggled ON via ComboBox");
                        }
                        // Note: We can't set specific AMD levels directly, only cycle
                        UpdateQuickSettingsTileStates();
                    }
                    else if (osdProvider != 1) // RTSS
                    {
                        PerformanceOverlaySlider.Value = index;
                    }
                    // Save the setting (but not during initial load)
                    if (!isLoadingPerformanceOverlaySetting)
                    {
                        SavePerformanceOverlaySetting();

                        // OSD level is helper-owned and persisted by its OSD property path.
                    }
                }
            }
        }

        // [2.0 rebuild - slice 1] The helper is now the source of truth for the OSD level:
        // it persists it durably (Settings.Default.OSDLevel) and pushes it to the widget on
        // startup (BatchGet) and on every change. So the widget no longer loads OSD from its
        // own LocalSettings copy here - that would override the helper's authoritative value.
        // The ComboBox/Slider are seeded by the helper's push (osd property ->
        // PerformanceOverlaySlider -> ValueChanged -> ComboBox). Kept as a no-op shell so
        // existing call sites don't break.
        private void LoadPerformanceOverlaySetting()
        {
            // Intentionally does nothing: OSD level authority moved to the helper (slice 1).
        }

        // [2.0 rebuild - slice 1] No longer persists OSD to widget LocalSettings - the helper
        // persists it (OnScreenDisplayProperty.SaveLevel) whenever the synced value changes.
        // Kept as a shell for existing call sites; per-game profile OverlayLevel persistence
        // (still widget-side for now) stays in PerformanceOverlayComboBox_SelectionChanged.
        private void SavePerformanceOverlaySetting()
        {
            // Intentionally does nothing: OSD level is persisted by the helper (slice 1).
        }

        private void SaveAMDOverlayLevel()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["AMD_OverlayLevel"] = amdOverlayLevel;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error saving AMD overlay level: {ex.Message}");
            }
        }

        private void PerformanceOverlaySlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (PerformanceOverlaySlider != null && PerformanceOverlayComboBox != null)
            {
                // Sync the ComboBox selection when slider value changes
                // (e.g., from property loading or helper updates)
                int newIndex = (int)Math.Round(e.NewValue);

                if (PerformanceOverlayComboBox.SelectedIndex != newIndex)
                {
                    // [2.0 rebuild - slice 1] OSD is now helper-authoritative, so this path also
                    // fires on helper pushes (e.g. a controller-hotkey OSD toggle). Mark the
                    // ComboBox update as programmatic so PerformanceOverlayComboBox_SelectionChanged
                    // does NOT treat a sync-driven change as a user edit and write it into the
                    // active game profile. The visible control users touch is the ComboBox, so its
                    // own SelectionChanged still persists genuine user edits.
                    bool wasLoading = isLoadingPerformanceOverlaySetting;
                    isLoadingPerformanceOverlaySetting = true;
                    try
                    {
                        PerformanceOverlayComboBox.SelectedIndex = newIndex;
                    }
                    finally
                    {
                        isLoadingPerformanceOverlaySetting = wasLoading;
                    }
                }
            }
        }

    }
}
