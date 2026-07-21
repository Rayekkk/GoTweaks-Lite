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
using XboxGamingBar.IPC;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // OSD configuration per level - stores which items are enabled
        // Level 1 (Basic): FPS, Battery, Time - 3 columns
        // Level 2 (Detailed): Time, FPS, Battery, CPU, GPU, Fan - 1 column
        // Level 3 (Full): All options - 1 column
        private Dictionary<int, Dictionary<string, bool>> osdLevelConfig = new Dictionary<int, Dictionary<string, bool>>
        {
            { 1, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "Time12H", false }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", false }, { "Memory", false }, { "VRAM", false }, { "CPU", false }, { "CPUClock", false }, { "GPU", false }, { "GPUClock", false }, { "FrameBudget", false }, { "Fan", false }, { "FrametimeGraph", false } } },
            { 2, new Dictionary<string, bool> { { "AppName", false }, { "Time", true }, { "Time12H", false }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", false }, { "Memory", false }, { "VRAM", false }, { "CPU", true }, { "CPUClock", false }, { "GPU", true }, { "GPUClock", false }, { "FrameBudget", true }, { "Fan", true }, { "FrametimeGraph", true } } },
            { 3, new Dictionary<string, bool> { { "AppName", true }, { "Time", true }, { "Time12H", false }, { "FPS", true }, { "Battery", true }, { "ControllerBattery", true }, { "Memory", true }, { "VRAM", true }, { "CPU", true }, { "CPUClock", true }, { "GPU", true }, { "GPUClock", true }, { "FrameBudget", true }, { "Fan", true }, { "FrametimeGraph", true } } }
        };

        private Dictionary<int, string> osdCustomTags = new Dictionary<int, string>
        {
            { 1, "" },
            { 2, "" },
            { 3, "" }
        };

        // Per-level column settings (Basic=3, Detailed=1, Full=1)
        private Dictionary<int, int> osdLevelColumns = new Dictionary<int, int>
        {
            { 1, 3 },  // Basic: 3 columns
            { 2, 1 },  // Detailed: 1 column
            { 3, 1 }   // Full: 1 column
        };

        // Current OSD customization level (1=Basic, 2=Detailed, 3=Full)
        private int osdCustomizeLevel = 1;

        // Per-level item order (list of item IDs in display order)
        private Dictionary<int, List<string>> osdLevelOrder = new Dictionary<int, List<string>>
        {
            { 1, new List<string> { "AppName", "Time", "Time12H", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "FrameBudget", "Fan", "TDPLimits", "FrametimeGraph" } },
            { 2, new List<string> { "AppName", "Time", "Time12H", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "FrameBudget", "Fan", "TDPLimits", "FrametimeGraph" } },
            { 3, new List<string> { "AppName", "Time", "Time12H", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "FrameBudget", "Fan", "TDPLimits", "FrametimeGraph" } }
        };

        // Per-level item label colors (DEFAULT = use global text color)
        private Dictionary<int, Dictionary<string, string>> osdItemLabelColors = new Dictionary<int, Dictionary<string, string>>
        {
            { 1, new Dictionary<string, string>() },
            { 2, new Dictionary<string, string>() },
            { 3, new Dictionary<string, string>() }
        };

        // Item display names for UI
        private static readonly Dictionary<string, string> osdItemDisplayNames = new Dictionary<string, string>
        {
            { "AppName", "App Name (D3D11, Vulkan, etc.)" },
            { "Time", "Time (24-hour)" },
            { "Time12H", "Time (12-hour)" },
            { "FPS", "FPS & Frametime" },
            { "Battery", "Battery" },
            { "ControllerBattery", "Controller Battery (L/R)" },
            { "Memory", "Memory (RAM)" },
            { "VRAM", "VRAM (GPU Memory)" },
            { "CPU", "CPU (Usage, Wattage, Temp)" },
            { "CPUClock", "CPU Clock Speed" },
            { "GPU", "GPU (Usage, Wattage, Temp)" },
            { "GPUClock", "GPU Clock Speed" },
            { "FrameBudget", "Frame Budget (CPU/GPU bound %)" },
            { "Fan", "Fan Speed" },
            { "TDPLimits", "TDP Limits (SPL/SPPT/FPPT)" },
            { "FrametimeGraph", "Frametime Graph" }
        };

        // Observable collection for OSD items UI
        private ObservableCollection<OSDItemViewModel> osdItemViewModels = new ObservableCollection<OSDItemViewModel>();

        // Global OSD layout settings
        private int osdTextSize = 100;    // Percentage: 50=Small, 100=Medium, 150=Large, 200=X-Large, 250=XX-Large, 300=XXX-Large
        private string osdTextColor = "DYNAMIC";  // DYNAMIC = value-based colors, or hex color code
        private string osdLabelColor = "DEFAULT";  // DEFAULT = use item-specific colors, or hex color code
        private int osdProvider = 0;  // Helper-confirmed display cache: 0=RTSS, 1=AMD
        private int amdOverlayLevel = 0;  // Helper-confirmed Radeon registry state: 0=Off, 1-4
        private bool isOSDCustomizeExpanded = false;
        private bool isProfileDetectionExpanded = false;
        private bool isProfileSettingsExpanded = false;
        private bool isTDPSettingsExpanded = false;
        private bool isColorSettingsExpanded = false;
        private bool isButtonRemappingExpanded = false;
        private bool isGyroSettingsExpanded = false;
        private bool isSavedProfilesExpanded = false;
        private bool isProfileSaveCategoriesExpanded = false;
        private bool isSpecialRemappingExpanded = false;
        private bool isStickDeadzonesExpanded = false;
        private bool isTouchpadVibrationExpanded = false;
        private bool isLightingExpanded = false;
        private bool isFanCurveExpanded = false;
        private bool isControllerEmulationExpanded = false;
        private bool fanCurveGraphInitialized = false;

        // Display and OSD settings
        private bool adaptiveBrightnessEnabled = false;
        private bool osdPositionShiftEnabled = false;
        private bool frametimeGraphPinned = false;
        private int osdOpacity = 100; // percentage 10-100
        private bool isLoadingOLEDSettings = false;
        private bool isLoadingPerformanceOverlaySetting = false;
        private readonly Windows.UI.Xaml.Shapes.Ellipse[] fanCurvePoints = new Windows.UI.Xaml.Shapes.Ellipse[10];
        private int[] currentFanCurveValues = new int[10];
        private int draggedPointIndex = -1;
        private bool isDraggingPoint = false;

        // Legion Go fan curve temperature thresholds (°C) - FIXED by EC at 10°C increments
        private static readonly int[] FanCurveTemperatures = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        // Minimum fan speeds - set to 0 since EC enforces its own thermal protection floor
        // EC override floor: 0-44°C=0%, 45°C=27%, 50°C=40%, 55°C=55%, 60°C=65%, 70°C=85%, 80+°C=100%
        private static readonly int[] FanCurveMinSpeeds = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        // EC Override Floor points: (temperature, minimum fan %) - what the EC enforces regardless of user curve
        private static readonly (int temp, int floor)[] ECFloorPoints = new[]
        {
            (10, 0), (20, 0), (30, 0), (44, 0),
            (45, 27), (50, 40), (55, 55), (60, 65), (70, 85),
            (80, 100), (90, 100), (100, 100)
        };
        // Fan curve preset definitions (values are fan % for temps 10,20,30,40,50,60,70,80,90,100°C)
        private static readonly Dictionary<string, int[]> FanCurvePresets = new Dictionary<string, int[]>
        {
            { "Silent", new int[] { 0, 0, 0, 27, 30, 40, 55, 65, 80, 100 } },       // Silent (Safe)
            { "Balanced", new int[] { 0, 0, 25, 30, 35, 45, 55, 70, 85, 100 } },    // Balanced
            { "Performance", new int[] { 30, 35, 40, 45, 50, 60, 70, 80, 90, 100 } }, // Performance
            { "MaxCooling", new int[] { 40, 45, 50, 55, 60, 70, 80, 90, 100, 100 } }  // Max Cooling
        };
        private bool isFanCurvePresetLoading = false;
        private bool isCPUExtrasExpanded = false;
        private bool isDebugExpanded = false;
        // Legacy values retained only while the reserved wire channel remains; no startup
        // load or active UI path reads or writes them.
        private bool isLoadingOSDConfig = false;

        private void OSDCustomizeLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Don't process during initialization - LoadOSDConfigFromStorage will handle it
            if (isLoadingOSDConfig) return;

            if (OSDCustomizeLevelComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int level))
                {
                    LoadOSDOptionsForLevel(level);
                    // Note: This is only for RTSS customization - AMD overlay doesn't have configurable levels
                }
            }
        }

        private async void OSDProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (OSDProviderComboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int provider))
                {
                    OSDProviderComboBox.IsEnabled = false;
                    await SendOSDProviderIntentAsync(provider);
                    OSDProviderComboBox.IsEnabled = true;
                }
            }
        }

        private void UpdateOSDProviderUI()
        {
            if (RTSSOptionsPanel != null)
            {
                RTSSOptionsPanel.Visibility = osdProvider == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AMDOptionsPanel != null)
            {
                AMDOptionsPanel.Visibility = osdProvider == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (PerformanceOverlayCustomItem != null)
            {
                PerformanceOverlayCustomItem.Visibility = osdProvider == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async Task SendOSDProviderIntentAsync(int provider)
        {
            try
            {
                if (!App.IsConnected) throw new InvalidOperationException("Helper is disconnected");
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "SetOSDProvider", provider },
                });
                ApplyOSDProviderState(response);
            }
            catch (Exception ex)
            {
                Logger.Warn($"OSD provider request failed: {ex.Message}");
                await SyncOSDProviderStateFromHelperAsync();
                ShowOSDProviderFailure(ex.Message);
            }
        }

        private async Task SendAMDOverlayLevelIntentAsync(int level)
        {
            try
            {
                if (!App.IsConnected) throw new InvalidOperationException("Helper is disconnected");
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "SetAMDOverlayLevel", level },
                });
                ApplyOSDProviderState(response);
            }
            catch (Exception ex)
            {
                Logger.Warn($"AMD overlay level request failed: {ex.Message}");
                await SyncOSDProviderStateFromHelperAsync();
                ShowOSDProviderFailure(ex.Message);
            }
        }

        private async Task SyncOSDProviderStateFromHelperAsync()
        {
            if (!App.IsConnected) return;
            try
            {
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "GetOSDProviderState", true },
                });
                ApplyOSDProviderState(response);
            }
            catch (Exception ex)
            {
                Logger.Warn($"OSD provider state sync failed: {ex.Message}");
            }
        }

        private void ApplyOSDProviderState(Windows.Foundation.Collections.ValueSet response)
        {
            if (response == null) return;
            int confirmedOSDLevel = osd?.Value ?? 0;
            bool success = response.TryGetValue("Success", out object successValue)
                           && successValue is bool successFlag && successFlag;
            string error = response.TryGetValue("Error", out object errorValue)
                ? errorValue?.ToString()
                : null;
            if (response.TryGetValue("OSDProvider", out object providerValue)
                && int.TryParse(providerValue?.ToString(), out int provider)
                && (provider == 0 || provider == 1))
                osdProvider = provider;
            if (response.TryGetValue("AMDOverlayLevel", out object levelValue)
                && int.TryParse(levelValue?.ToString(), out int level)
                && level >= 0 && level <= 4)
                amdOverlayLevel = level;
            if (response.TryGetValue("OSDLevel", out object osdLevelValue)
                && int.TryParse(osdLevelValue?.ToString(), out int parsedOSDLevel)
                && parsedOSDLevel >= 0 && parsedOSDLevel <= 3)
                confirmedOSDLevel = parsedOSDLevel;

            isLoadingOSDConfig = true;
            isLoadingPerformanceOverlaySetting = true;
            try
            {
                UpdateOSDLayoutUI();
                if (PerformanceOverlayComboBox != null)
                {
                    int visibleLevel = osdProvider == 1 ? amdOverlayLevel : confirmedOSDLevel;
                    if (visibleLevel >= 0 && visibleLevel < PerformanceOverlayComboBox.Items.Count)
                        PerformanceOverlayComboBox.SelectedIndex = visibleLevel;
                }
                UpdateQuickSettingsTileStates();
                if (OSDProviderStatusText != null)
                {
                    OSDProviderStatusText.Text = success ? "" : (error ?? "The helper could not apply the requested OSD state.");
                    OSDProviderStatusText.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            finally
            {
                isLoadingPerformanceOverlaySetting = false;
                isLoadingOSDConfig = false;
            }
        }

        private void ShowOSDProviderFailure(string message)
        {
            if (OSDProviderStatusText == null) return;
            OSDProviderStatusText.Text = string.IsNullOrWhiteSpace(message)
                ? "The helper could not apply the requested OSD state."
                : message;
            OSDProviderStatusText.Visibility = Visibility.Visible;
        }

        private void LoadOSDOptionsForLevel(int level)
        {
            if (!osdLevelConfig.ContainsKey(level)) return;

            isLoadingOSDConfig = true;
            try
            {
                // Update the current level
                osdCustomizeLevel = level;

                // Refresh the OSD items control with current level's order and states
                RefreshOSDItemsControl();

                if (OSDCustomTagsTextBox != null) OSDCustomTagsTextBox.Text = osdCustomTags.GetValueOrDefault(level, "");

                // Load columns for this level
                int columns = osdLevelColumns.GetValueOrDefault(level, 3);
                if (OSDColumnsComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDColumnsComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == columns)
                        {
                            OSDColumnsComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            finally
            {
                isLoadingOSDConfig = false;
            }
        }

        /// <summary>
        /// Refreshes the OSD items control with the current level's order and enabled states
        /// </summary>
        private void RefreshOSDItemsControl()
        {
            if (OSDItemsControl == null) return;

            int currentLevel = osdCustomizeLevel;
            if (!osdLevelOrder.ContainsKey(currentLevel)) return;

            var order = osdLevelOrder[currentLevel];
            if (!osdLevelConfig.ContainsKey(currentLevel))
            {
                osdLevelConfig[currentLevel] = new Dictionary<string, bool>();
            }
            var config = osdLevelConfig[currentLevel];

            osdItemViewModels.Clear();
            var labelColors = osdItemLabelColors.ContainsKey(currentLevel) ? osdItemLabelColors[currentLevel] : new Dictionary<string, string>();
            for (int i = 0; i < order.Count; i++)
            {
                var id = order[i];
                osdItemViewModels.Add(new OSDItemViewModel
                {
                    Id = id,
                    DisplayName = osdItemDisplayNames.ContainsKey(id) ? osdItemDisplayNames[id] : id,
                    IsEnabled = config.ContainsKey(id) && config[id],
                    CanMoveUp = i > 0,
                    CanMoveDown = i < order.Count - 1,
                    LabelColor = labelColors.ContainsKey(id) ? labelColors[id] : "DEFAULT"
                });
            }

            OSDItemsControl.ItemsSource = osdItemViewModels;
        }

        private void OSDItemCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            if (sender is CheckBox cb && cb.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                if (!osdLevelConfig.ContainsKey(currentLevel))
                {
                    osdLevelConfig[currentLevel] = new Dictionary<string, bool>();
                }
                osdLevelConfig[currentLevel][itemId] = cb.IsChecked == true;

                SaveOSDConfigToStorage();
                SendOSDConfigToHelper();
            }
        }

        private void OSDItemMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                var order = osdLevelOrder[currentLevel];
                int index = order.IndexOf(itemId);
                if (index > 0)
                {
                    order.RemoveAt(index);
                    order.Insert(index - 1, itemId);
                    RefreshOSDItemsControl();
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
        }

        private void OSDItemMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string itemId)
            {
                int currentLevel = osdCustomizeLevel;
                var order = osdLevelOrder[currentLevel];
                int index = order.IndexOf(itemId);
                if (index >= 0 && index < order.Count - 1)
                {
                    order.RemoveAt(index);
                    order.Insert(index + 1, itemId);
                    RefreshOSDItemsControl();
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
        }

        private void OSDCustomTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            SaveCurrentOSDConfig();
        }

        private void SaveCurrentOSDConfig()
        {
            int level = osdCustomizeLevel;

            // Item enabled states are already in osdLevelConfig (updated by OSDItemCheckBox_Changed)
            // Just save custom tags and columns here

            osdCustomTags[level] = OSDCustomTagsTextBox?.Text ?? "";

            // Save columns for this level
            if (OSDColumnsComboBox?.SelectedItem is ComboBoxItem colItem && colItem.Tag is string colTag)
            {
                if (int.TryParse(colTag, out int cols))
                {
                    osdLevelColumns[level] = cols;
                }
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void SaveOSDConfigToStorage()
        {
            // HelperProperty<OSDConfig> persists the accepted configuration. The widget
            // deliberately keeps only a display/edit cache.
        }

        private void LoadOSDConfigFromStorage()
        {
            // Defaults are only a temporary display while the helper snapshot arrives.
            // They must never be sent back as a startup write.
            try
            {
                var itemKeys = new[] { "AppName", "Time", "Time12H", "FPS", "Battery", "ControllerBattery", "Memory", "VRAM", "CPU", "CPUClock", "GPU", "GPUClock", "FrameBudget", "Fan", "TDPLimits", "FrametimeGraph" };

                foreach (var level in new[] { 1, 2, 3 })
                {
                    if (!osdLevelConfig.ContainsKey(level))
                    {
                        osdLevelConfig[level] = new Dictionary<string, bool>();
                    }

                    foreach (var key in itemKeys)
                    {
                        osdLevelConfig[level][key] = false;
                    }

                    osdCustomTags[level] = "";

                    if (!osdItemLabelColors.ContainsKey(level))
                    {
                        osdItemLabelColors[level] = new Dictionary<string, string>();
                    }
                }

                osdTextSize = 100;

                // Update layout UI
                UpdateOSDLayoutUI();

                Logger.Info("OSD configuration loaded from storage");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error loading OSD config: {ex.Message}");
            }
        }

        private async void SendOSDConfigToHelper()
        {
            try
            {
                if (!App.IsConnected) return;

                // Build config string to send to helper
                var configParts = new List<string>();

                // Add global layout settings
                configParts.Add($"TextSize:{osdTextSize}");
                configParts.Add($"TextColor:{osdTextColor}");
                configParts.Add($"LabelColor:{osdLabelColor}");
                configParts.Add($"Opacity:{osdOpacity}");
                configParts.Add($"FrametimeGraphPinned:{(frametimeGraphPinned ? "1" : "0")}");

                // Add per-level item configuration
                foreach (var level in osdLevelConfig.Keys)
                {
                    var config = osdLevelConfig[level];
                    var enabledItems = new List<string>();
                    foreach (var item in config)
                    {
                        if (item.Value)
                        {
                            enabledItems.Add(item.Key);
                        }
                    }
                    configParts.Add($"L{level}:{string.Join(",", enabledItems)}");

                    if (!string.IsNullOrWhiteSpace(osdCustomTags.GetValueOrDefault(level, "")))
                    {
                        configParts.Add($"L{level}_Custom:{osdCustomTags[level]}");
                    }

                    // Add per-level columns
                    configParts.Add($"L{level}_Columns:{osdLevelColumns.GetValueOrDefault(level, 3)}");

                    // Add per-level order
                    if (osdLevelOrder.ContainsKey(level))
                    {
                        configParts.Add($"L{level}_Order:{string.Join(",", osdLevelOrder[level])}");
                    }

                    // Add per-level item label colors
                    if (osdItemLabelColors.ContainsKey(level))
                    {
                        var colors = osdItemLabelColors[level];
                        foreach (var colorItem in colors)
                        {
                            if (!string.IsNullOrEmpty(colorItem.Value) && colorItem.Value != "DEFAULT")
                            {
                                configParts.Add($"L{level}_{colorItem.Key}_Color:{colorItem.Value}");
                            }
                        }
                    }
                }

                var configString = string.Join(";", configParts);
                var request = new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Set },
                    { "Function", (int)Shared.Enums.Function.OSDConfig },
                    { "Content", configString },
                    { "UpdatedTime", DateTimeOffset.Now.Ticks }
                };
                await App.SendMessageAsync(request);

                Logger.Info($"OSD config sent to helper: {configString}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sending OSD config to helper: {ex.Message}");
            }
        }

        private async Task RequestOSDConfigFromHelperAsync()
        {
            if (!App.IsConnected) return;
            try
            {
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Shared.Enums.Command.Get },
                    { "Function", (int)Shared.Enums.Function.OSDConfig },
                });
                if (response == null || !response.TryGetValue("Content", out object content) || string.IsNullOrWhiteSpace(content?.ToString())) return;
                isLoadingOSDConfig = true;
                foreach (var part in content.ToString().Split(';'))
                {
                    int separator = part.IndexOf(':');
                    if (separator < 0) continue;
                    string key = part.Substring(0, separator);
                    string value = part.Substring(separator + 1);
                    if (key == "TextSize" && int.TryParse(value, out int size)) osdTextSize = size;
                    else if (key == "TextColor") osdTextColor = value;
                    else if (key == "LabelColor") osdLabelColor = value;
                    else if (key == "Opacity" && int.TryParse(value, out int opacity)) osdOpacity = opacity;
                    else if (key == "FrametimeGraphPinned") frametimeGraphPinned = value == "1";
                    else if (key.Length >= 2 && key[0] == 'L')
                    {
                        // [full-audit fix, 2026-07-20 — B3] Parse the full per-level key space that
                        // SendOSDConfigToHelper emits, not just the bare enabled-list. Previously only
                        // `L{n}` (key.Length == 2) was read back; `L{n}_Order`, `L{n}_Columns`,
                        // `L{n}_Custom`, and `L{n}_{item}_Color` were dropped and LoadOSDConfigFromStorage
                        // reset those fields to defaults every start - so the first OSD edit rebuilt the
                        // whole config string from defaults and destroyed the helper's persisted layout.
                        int di = 1;
                        while (di < key.Length && char.IsDigit(key[di])) di++;
                        if (di > 1 && int.TryParse(key.Substring(1, di - 1), out int level))
                        {
                            string suffix = key.Substring(di); // "" | "_Custom" | "_Columns" | "_Order" | "_<item>_Color"
                            if (suffix.Length == 0)
                            {
                                if (osdLevelConfig.ContainsKey(level))
                                {
                                    foreach (var item in osdLevelConfig[level].Keys.ToList()) osdLevelConfig[level][item] = false;
                                    foreach (var item in value.Split(',')) if (osdLevelConfig[level].ContainsKey(item)) osdLevelConfig[level][item] = true;
                                }
                            }
                            else if (suffix == "_Custom")
                            {
                                osdCustomTags[level] = value;
                            }
                            else if (suffix == "_Columns" && int.TryParse(value, out int cols))
                            {
                                osdLevelColumns[level] = cols;
                            }
                            else if (suffix == "_Order")
                            {
                                osdLevelOrder[level] = value.Split(',').Where(s => !string.IsNullOrEmpty(s)).ToList();
                            }
                            else if (suffix.Length > 7 && suffix[0] == '_' && suffix.EndsWith("_Color"))
                            {
                                // suffix == "_<item>_Color" - strip the leading '_' and trailing '_Color'
                                string item = suffix.Substring(1, suffix.Length - 1 - "_Color".Length);
                                if (!osdItemLabelColors.ContainsKey(level))
                                    osdItemLabelColors[level] = new Dictionary<string, string>();
                                osdItemLabelColors[level][item] = value;
                            }
                        }
                    }
                }
                UpdateOSDLayoutUI();
                LoadOSDOptionsForLevel(osdCustomizeLevel);
            }
            catch (Exception ex) { Logger.Error($"Failed to render helper OSD configuration: {ex.Message}"); }
            finally { isLoadingOSDConfig = false; }
        }

        private void OSDCustomizeExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isOSDCustomizeExpanded = !isOSDCustomizeExpanded;

            if (OSDCustomizeContent != null)
            {
                OSDCustomizeContent.Visibility = isOSDCustomizeExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (OSDCustomizeExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                OSDCustomizeExpandIcon.Glyph = isOSDCustomizeExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void OSDOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;
            osdOpacity = (int)Math.Round(e.NewValue);
            if (OSDOpacityValue != null)
                OSDOpacityValue.Text = $"{osdOpacity}%";
            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }
        private void ProfileSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileSettingsExpanded = !isProfileSettingsExpanded;

            if (ProfileSettingsContent != null)
            {
                ProfileSettingsContent.Visibility = isProfileSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileSettingsExpandIcon.Glyph = isProfileSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ProfileDetectionExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileDetectionExpanded = !isProfileDetectionExpanded;

            if (ProfileDetectionContent != null)
            {
                ProfileDetectionContent.Visibility = isProfileDetectionExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ProfileDetectionExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ProfileDetectionExpandIcon.Glyph = isProfileDetectionExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ButtonRemappingExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isButtonRemappingExpanded = !isButtonRemappingExpanded;

            if (ButtonRemappingContent != null)
            {
                ButtonRemappingContent.Visibility = isButtonRemappingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ButtonRemappingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ButtonRemappingExpandIcon.Glyph = isButtonRemappingExpanded ? "\uE70E" : "\uE70D";
            }

            if (isButtonRemappingExpanded)
            {
                RefreshLegionEnhancedRemapUi();
            }
        }

        private void GyroSettingsExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isGyroSettingsExpanded = !isGyroSettingsExpanded;

            if (GyroSettingsContent != null)
            {
                GyroSettingsContent.Visibility = isGyroSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (GyroSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                GyroSettingsExpandIcon.Glyph = isGyroSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void StickDeadzonesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isStickDeadzonesExpanded = !isStickDeadzonesExpanded;

            if (StickDeadzonesContent != null)
            {
                StickDeadzonesContent.Visibility = isStickDeadzonesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (StickDeadzonesExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                StickDeadzonesExpandIcon.Glyph = isStickDeadzonesExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void TouchpadVibrationExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isTouchpadVibrationExpanded = !isTouchpadVibrationExpanded;

            if (TouchpadVibrationContent != null)
            {
                TouchpadVibrationContent.Visibility = isTouchpadVibrationExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TouchpadVibrationExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TouchpadVibrationExpandIcon.Glyph = isTouchpadVibrationExpanded ? "\uE70E" : "\uE70D";
            }
        }
        private void LightingExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isLightingExpanded = !isLightingExpanded;

            if (LightingContent != null)
            {
                LightingContent.Visibility = isLightingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (LightingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                LightingExpandIcon.Glyph = isLightingExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void FanCurveExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isFanCurveExpanded = !isFanCurveExpanded;

            if (FanCurveContent != null)
            {
                FanCurveContent.Visibility = isFanCurveExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FanCurveExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                FanCurveExpandIcon.Glyph = isFanCurveExpanded ? "\uE70E" : "\uE70D";
            }

            // Initialize graph on first expand
            if (isFanCurveExpanded && !fanCurveGraphInitialized)
            {
                InitializeFanCurveGraph();
            }

            // Tell helper whether to push the fan-control sensor temp (0x01) used by the
            // fan-curve graph's own info panel, and to speed up its refresh cadence. RPM and
            // the header CPU temp stream continuously regardless of this flag (see
            // LegionManager.RefreshFanSpeed), so the header never needs clearing here.
            legionFanCurveVisible?.SetVisible(isFanCurveExpanded);

            if (isFanCurveExpanded)
            {
                UpdateActiveModeLabel();
            }
        }
        private void CPUExtrasExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isCPUExtrasExpanded = !isCPUExtrasExpanded;

            if (CPUExtrasContent != null)
            {
                CPUExtrasContent.Visibility = isCPUExtrasExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (CPUExtrasExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                CPUExtrasExpandIcon.Glyph = isCPUExtrasExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void ControllerEmulationExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isControllerEmulationExpanded = !isControllerEmulationExpanded;
            LastCardExpandedBeforeHide = isControllerEmulationExpanded;

            // Show whichever backend's body the user has selected (legacy vs VIIPER).
            bool viiperActive = emulationBackend != null && emulationBackend.Value;

            if (ControllerEmulationContent != null)
            {
                ControllerEmulationContent.Visibility = (isControllerEmulationExpanded && !viiperActive)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ViiperEmulationContent != null)
            {
                ViiperEmulationContent.Visibility = (isControllerEmulationExpanded && viiperActive)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ControllerEmulationExpandIcon != null)
            {
                ControllerEmulationExpandIcon.Glyph = isControllerEmulationExpanded ? "\uE70E" : "\uE70D";
            }

            UpdateSystemControllerEmulationNavigation();
        }

        private void TDPSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isTDPSettingsExpanded = !isTDPSettingsExpanded;

            if (TDPSettingsContent != null)
            {
                TDPSettingsContent.Visibility = isTDPSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TDPSettingsExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                TDPSettingsExpandIcon.Glyph = isTDPSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void SpecialRemappingExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isSpecialRemappingExpanded = !isSpecialRemappingExpanded;

            if (SpecialRemappingContent != null)
            {
                SpecialRemappingContent.Visibility = isSpecialRemappingExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (SpecialRemappingExpandIcon != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                SpecialRemappingExpandIcon.Glyph = isSpecialRemappingExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void StickSensitivityV2Slider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (StickSensitivityV2ValueText != null)
                StickSensitivityV2ValueText.Text = $"{(e.NewValue / 100.0):0.00}x";
        }

        // Min/Max gyro speed, Min/Max output, Power curve, Deadzone, Precision speed,
        // Output mix slider value-change handlers all removed in #79 round 5
        // along with the underlying sliders.


        private void ColorSettingsExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isColorSettingsExpanded = !isColorSettingsExpanded;

            if (ColorSettingsContent != null)
            {
                ColorSettingsContent.Visibility = isColorSettingsExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ColorSettingsExpandButton != null)
            {
                // E70D = ChevronDown, E70E = ChevronUp
                ColorSettingsExpandButton.Content = isColorSettingsExpanded ? "\uE70E" : "\uE70D";
            }
        }

        private void OSDLayoutOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            // Get text size (global setting)
            if (OSDTextSizeComboBox?.SelectedItem is ComboBoxItem sizeItem && sizeItem.Tag is string sizeTag)
            {
                if (int.TryParse(sizeTag, out int size))
                {
                    osdTextSize = size;
                }
            }

            // Columns are per-level, handled by SaveCurrentOSDConfig
            SaveCurrentOSDConfig();
        }

        private void OSDTextColorDynamic_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            bool isDynamic = OSDTextColorDynamicCheckBox?.IsChecked == true;
            if (isDynamic)
            {
                osdTextColor = "DYNAMIC";
                UpdateOSDTextColorPreview();
            }
            else
            {
                // Use current color picker color
                if (OSDTextColorPicker != null)
                {
                    var color = OSDTextColorPicker.Color;
                    osdTextColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                }
                else
                {
                    osdTextColor = "FFFFFF";
                }
                UpdateOSDTextColorPreview();
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void OSDTextColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OSDTextColorPicker != null)
                {
                    bool isExpanded = OSDTextColorPicker.Visibility == Visibility.Visible;
                    OSDTextColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    if (OSDTextColorExpandButton != null)
                    {
                        OSDTextColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDTextColorExpandButton_Click: {ex.Message}");
            }
        }

        private void OSDTextColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (isLoadingOSDConfig) return;

            try
            {
                // Update preview
                if (OSDTextColorPreview != null)
                {
                    OSDTextColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                // Only update color if not in Dynamic mode
                if (OSDTextColorDynamicCheckBox?.IsChecked != true)
                {
                    osdTextColor = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDTextColorPicker_ColorChanged: {ex.Message}");
            }
        }

        private void OSDLabelColorDefault_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingOSDConfig) return;

            bool isDefault = OSDLabelColorDefaultCheckBox?.IsChecked == true;
            if (isDefault)
            {
                osdLabelColor = "DEFAULT";
                UpdateOSDLabelColorPreview();
            }
            else
            {
                // Use current color picker color
                if (OSDLabelColorPicker != null)
                {
                    var color = OSDLabelColorPicker.Color;
                    osdLabelColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                }
                else
                {
                    osdLabelColor = "00FFFF";  // Cyan default
                }
                UpdateOSDLabelColorPreview();
            }

            SaveOSDConfigToStorage();
            SendOSDConfigToHelper();
        }

        private void OSDLabelColorExpandButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OSDLabelColorPicker != null)
                {
                    bool isExpanded = OSDLabelColorPicker.Visibility == Visibility.Visible;
                    OSDLabelColorPicker.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;

                    if (OSDLabelColorExpandButton != null)
                    {
                        OSDLabelColorExpandButton.Content = isExpanded ? "\uE70D" : "\uE70E";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDLabelColorExpandButton_Click: {ex.Message}");
            }
        }

        private void OSDLabelColorPicker_ColorChanged(Microsoft.UI.Xaml.Controls.ColorPicker sender, Microsoft.UI.Xaml.Controls.ColorChangedEventArgs args)
        {
            if (isLoadingOSDConfig) return;

            try
            {
                // Update preview
                if (OSDLabelColorPreview != null)
                {
                    OSDLabelColorPreview.Background = new SolidColorBrush(args.NewColor);
                }

                // Only update color if not in Default mode
                if (OSDLabelColorDefaultCheckBox?.IsChecked != true)
                {
                    osdLabelColor = $"{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
                    SaveOSDConfigToStorage();
                    SendOSDConfigToHelper();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error in OSDLabelColorPicker_ColorChanged: {ex.Message}");
            }
        }

        private void UpdateOSDTextColorPreview()
        {
            if (OSDTextColorPreview == null) return;

            try
            {
                if (osdTextColor == "DYNAMIC")
                {
                    // Show gradient for dynamic color preview (blue to green to yellow to red)
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                    gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 128, 255), Offset = 0 });    // Blue (cold)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 0.33 });   // Green (good)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 255, 0), Offset = 0.66 }); // Yellow (warm)
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 0, 0), Offset = 1 });      // Red (hot)
                    OSDTextColorPreview.Background = gradient;
                }
                else if (osdTextColor.Length == 6)
                {
                    var color = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(osdTextColor.Substring(0, 2), 16),
                        Convert.ToByte(osdTextColor.Substring(2, 2), 16),
                        Convert.ToByte(osdTextColor.Substring(4, 2), 16));
                    OSDTextColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch { }
        }

        private void UpdateOSDLabelColorPreview()
        {
            if (OSDLabelColorPreview == null) return;

            try
            {
                if (osdLabelColor == "DEFAULT")
                {
                    // Show gradient to indicate default (each item has its own color)
                    var gradient = new LinearGradientBrush();
                    gradient.StartPoint = new Windows.Foundation.Point(0, 0);
                    gradient.EndPoint = new Windows.Foundation.Point(1, 0);
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 255), Offset = 0 });    // Cyan
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 165, 0), Offset = 0.5 });  // Orange
                    gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 255, 0), Offset = 1 });      // Green
                    OSDLabelColorPreview.Background = gradient;
                }
                else if (osdLabelColor.Length == 6)
                {
                    var color = Windows.UI.Color.FromArgb(255,
                        Convert.ToByte(osdLabelColor.Substring(0, 2), 16),
                        Convert.ToByte(osdLabelColor.Substring(2, 2), 16),
                        Convert.ToByte(osdLabelColor.Substring(4, 2), 16));
                    OSDLabelColorPreview.Background = new SolidColorBrush(color);
                }
            }
            catch { }
        }

        private void UpdateOSDLayoutUI()
        {
            isLoadingOSDConfig = true;
            try
            {
                // Set OSD provider combobox
                if (OSDProviderComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDProviderComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == osdProvider)
                        {
                            OSDProviderComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Update provider-specific UI visibility
                UpdateOSDProviderUI();

                // Columns are per-level, loaded in LoadOSDOptionsForLevel

                // Set text size combobox
                if (OSDTextSizeComboBox != null)
                {
                    foreach (ComboBoxItem item in OSDTextSizeComboBox.Items)
                    {
                        if (item.Tag is string tag && int.TryParse(tag, out int val) && val == osdTextSize)
                        {
                            OSDTextSizeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                // Set text color checkbox and color picker
                if (OSDTextColorDynamicCheckBox != null)
                {
                    OSDTextColorDynamicCheckBox.IsChecked = (osdTextColor == "DYNAMIC");
                }
                if (OSDTextColorPicker != null && osdTextColor != "DYNAMIC" && osdTextColor.Length == 6)
                {
                    try
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(osdTextColor.Substring(0, 2), 16),
                            Convert.ToByte(osdTextColor.Substring(2, 2), 16),
                            Convert.ToByte(osdTextColor.Substring(4, 2), 16));
                        OSDTextColorPicker.Color = color;
                    }
                    catch { }
                }
                UpdateOSDTextColorPreview();

                // Set label color checkbox and color picker
                if (OSDLabelColorDefaultCheckBox != null)
                {
                    OSDLabelColorDefaultCheckBox.IsChecked = (osdLabelColor == "DEFAULT");
                }
                if (OSDLabelColorPicker != null && osdLabelColor != "DEFAULT" && osdLabelColor.Length == 6)
                {
                    try
                    {
                        var color = Windows.UI.Color.FromArgb(255,
                            Convert.ToByte(osdLabelColor.Substring(0, 2), 16),
                            Convert.ToByte(osdLabelColor.Substring(2, 2), 16),
                            Convert.ToByte(osdLabelColor.Substring(4, 2), 16));
                        OSDLabelColorPicker.Color = color;
                    }
                    catch { }
                }
                UpdateOSDLabelColorPreview();

            }
            finally
            {
                isLoadingOSDConfig = false;
            }
        }

    }
}
