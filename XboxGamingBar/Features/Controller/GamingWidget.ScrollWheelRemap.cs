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
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // [full-audit fix, 2026-07-20 — B7] Set while ApplyScrollRemapSnapshot programmatically
        // renders a helper-pushed scroll config into the combos. Without it, setting
        // ScrollActionComboBox.SelectedIndex during hydration fired the SelectionChanged handler,
        // which auto-applied (re-sent) the just-received config for selections 0/1/4 - the
        // render-becomes-intent anti-pattern the sibling snapshots (ApplyLegionRemapSnapshot,
        // ApplyBrightnessGestureSnapshot) already guard against.
        private bool isRenderingScrollSnapshot;

        // Scroll (unified) event handlers - direction not available via Raw Input API
        private void ScrollActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!labsSectionInitialized || isRenderingScrollSnapshot) return;

            int selection = ScrollActionComboBox?.SelectedIndex ?? 0;
            // 0=Disabled, 1=Xbox Guide, 2=Keyboard Shortcut, 3=Run Command, 4=Focus GoTweaks
            bool isShortcut = selection == 2;
            bool isCommand = selection == 3;
            if (ScrollShortcutPanel != null)
                ScrollShortcutPanel.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollCommandGrid != null)
                ScrollCommandGrid.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;

            // Apply immediately for Disabled, Xbox Guide, or Focus GoTweaks
            if (selection != 2 && selection != 3)
                ApplyScrollWheelConfig("Scroll");

            UpdateScrollRemapDescription();
        }

        private void ScrollCommandApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyScrollWheelConfig("Scroll");
            UpdateScrollRemapDescription();
        }

        // Scroll Click event handlers
        private void ScrollClickActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!labsSectionInitialized || isRenderingScrollSnapshot) return;

            int selection = ScrollClickActionComboBox?.SelectedIndex ?? 0;
            bool isShortcut = selection == 2;
            bool isCommand = selection == 3;
            if (ScrollClickShortcutPanel != null)
                ScrollClickShortcutPanel.Visibility = isShortcut ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollClickCommandGrid != null)
                ScrollClickCommandGrid.Visibility = isCommand ? Visibility.Visible : Visibility.Collapsed;

            if (selection != 2 && selection != 3)
                ApplyScrollWheelConfig("Click");

            UpdateScrollRemapDescription();
        }

        private void ScrollClickCommandApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyScrollWheelConfig("Click");
            UpdateScrollRemapDescription();
        }

        private void UpdateScrollRemapDescription()
        {
            // Description text removed in consolidated Special Remapping card
        }

        private async Task RequestScrollRemapSettingsFromHelperAsync()
        {
            if (!App.IsConnected) return;
            try
            {
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Command.Get },
                    { "Function", (int)Function.Labs_LegionScrollRemap },
                });
                if (response != null && response.TryGetValue("Content", out object content) && content != null)
                    ApplyScrollRemapSnapshot(content.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get scroll remap settings from helper: {ex.Message}");
            }
        }

        private void ApplyScrollRemapSnapshot(string configJson)
        {
            isRenderingScrollSnapshot = true; // [B7] suppress the SelectionChanged auto-apply echo
            try
            {
                var config = Windows.Data.Json.JsonObject.Parse(configJson);
                int GetAction(string key) => config.TryGetValue(key, out var value) ? (int)value.GetNumber() : 0;
                string GetText(string key) => config.TryGetValue(key, out var value) ? value.GetString() ?? "" : "";

                if (ScrollActionComboBox != null) ScrollActionComboBox.SelectedIndex = GetAction("Scroll_Action");
                LoadKeysFromString("Scroll", GetText("Scroll_Shortcut"), ScrollKeyTags);
                if (ScrollCommandTextBox != null) ScrollCommandTextBox.Text = GetText("Scroll_Command");
                if (ScrollClickActionComboBox != null) ScrollClickActionComboBox.SelectedIndex = GetAction("ScrollClick_Action");
                LoadKeysFromString("ScrollClick", GetText("ScrollClick_Shortcut"), ScrollClickKeyTags);
                if (ScrollClickCommandTextBox != null) ScrollClickCommandTextBox.Text = GetText("ScrollClick_Command");
                UpdateScrollGridVisibility();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to render helper scroll remap snapshot: {ex.Message}");
            }
            finally
            {
                isRenderingScrollSnapshot = false;
            }
        }
        private void UpdateScrollGridVisibility()
        {
            int scrollSelection = ScrollActionComboBox?.SelectedIndex ?? 0;
            if (ScrollShortcutPanel != null)
                ScrollShortcutPanel.Visibility = scrollSelection == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollCommandGrid != null)
                ScrollCommandGrid.Visibility = scrollSelection == 3 ? Visibility.Visible : Visibility.Collapsed;

            int clickSelection = ScrollClickActionComboBox?.SelectedIndex ?? 0;
            if (ScrollClickShortcutPanel != null)
                ScrollClickShortcutPanel.Visibility = clickSelection == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (ScrollClickCommandGrid != null)
                ScrollClickCommandGrid.Visibility = clickSelection == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ApplyScrollWheelConfig(string direction)
        {
            if (!App.IsConnected) return;

            try
            {
                ComboBox actionComboBox = direction == "Scroll" ? ScrollActionComboBox :
                                          direction == "Click" ? ScrollClickActionComboBox :
                                          ScrollClickActionComboBox;
                string shortcutKeyName = direction == "Scroll" ? "Scroll" :
                                         direction == "Click" ? "ScrollClick" :
                                         "ScrollClick";
                TextBox commandTextBox = direction == "Scroll" ? ScrollCommandTextBox :
                                         direction == "Click" ? ScrollClickCommandTextBox :
                                         ScrollClickCommandTextBox;
                string actionName = direction == "Scroll" ? "Scroll Wheel" : $"Scroll {direction}";

                if (actionComboBox == null) return;

                int selection = actionComboBox.SelectedIndex; // 0=Disabled, 1=Xbox Guide, 2=Shortcut, 3=Command, 4=Focus GoTweaks
                bool enabled = selection != 0;
                // Convert UI selection to helper action type: 0=Xbox Guide, 1=Shortcut, 2=Command, 3=Focus GoTweaks
                int actionType = selection == 1 ? 0 : selection == 2 ? 1 : selection == 3 ? 2 : selection == 4 ? 3 : 0;

                string shortcutOrCommand = "";
                if (selection == 2)
                {
                    shortcutOrCommand = GetKeysAsString(shortcutKeyName);
                    if (string.IsNullOrEmpty(shortcutOrCommand))
                    {
                        if (ScrollRemapStatusText != null)
                        {
                            ScrollRemapStatusText.Text = $"{actionName}: Please select keys";
                            ScrollRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100));
                        }
                        return;
                    }
                }
                else if (selection == 3 && commandTextBox != null)
                {
                    shortcutOrCommand = commandTextBox.Text?.Trim() ?? "";
                    if (string.IsNullOrEmpty(shortcutOrCommand))
                    {
                        if (ScrollRemapStatusText != null)
                        {
                            ScrollRemapStatusText.Text = $"{actionName}: Please enter a command";
                            ScrollRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 100));
                        }
                        return;
                    }
                }

                var request = new Windows.Foundation.Collections.ValueSet();
                request.Add("Function", (int)Function.Labs_LegionScrollRemap);
                request.Add("Direction", direction);
                request.Add("Enabled", enabled);
                request.Add("Action", actionType);
                request.Add("Shortcut", shortcutOrCommand);

                var response = await App.SendMessageAsync(request);

                if (response != null)
                {
                    if (response.TryGetValue("Success", out object successObj))
                    {
                        bool success = Convert.ToBoolean(successObj);
                        if (ScrollRemapStatusText != null)
                        {
                            if (!enabled)
                            {
                                ScrollRemapStatusText.Text = "";
                            }
                            else if (success)
                            {
                                ScrollRemapStatusText.Text = "";
                            }
                            else
                            {
                                string errorMsg = actionType == 0 ? "usbip-win2 not installed or controller not found" : "Controller not found";
                                ScrollRemapStatusText.Text = $"{actionName}: Failed - {errorMsg}";
                                ScrollRemapStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
                                actionComboBox.SelectedIndex = 0; // Reset to Disabled
                            }
                        }

                        // The helper is authoritative: on a failed enabled request this restores
                        // the last persisted, applied configuration instead of leaving the UI at
                        // the optimistic selection.
                        if (response.TryGetValue("Content", out object content) && content != null)
                            ApplyScrollRemapSnapshot(content.ToString());

                        Logger.Info($"Scroll Wheel Remap: {direction}, Enabled={enabled}, Action={actionType}, Value={shortcutOrCommand}, Success={success}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply scroll wheel config: {ex.Message}");
            }
        }

    }
}
