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
        private void HotkeysExpandButton_Click(object sender, RoutedEventArgs e)
        {
            isHotkeysExpanded = !isHotkeysExpanded;

            if (HotkeysContent != null)
            {
                HotkeysContent.Visibility = isHotkeysExpanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (HotkeysExpandIcon != null)
            {
                HotkeysExpandIcon.Glyph = isHotkeysExpanded ? "\uE70E" : "\uE70D";
            }

            // Load settings when expanding for the first time
            if (isHotkeysExpanded)
            {
                LoadHotkeySettings();
            }
        }

        private async void LoadHotkeySettings()
        {
            if (!App.IsConnected) return;
            isLoadingHotkeys = true;
            try
            {
                var response = await App.SendMessageAsync(new Windows.Foundation.Collections.ValueSet
                {
                    { "Command", (int)Command.Get },
                    { "Function", (int)Function.ControllerHotkeyConfig },
                });
                if (response != null && response.TryGetValue("Content", out object content) && content != null)
                    ApplyHotkeyConfigSnapshot(content.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get controller hotkey configuration from helper: {ex.Message}");
            }
            finally
            {
                isLoadingHotkeys = false;
            }
        }

        private void ApplyHotkeyConfigSnapshot(string configJson)
        {
            var config = Windows.Data.Json.JsonObject.Parse(configJson);
            ApplyHotkeySnapshot(config, "MenuA", HotkeyMenuAComboBox, HotkeyMenuAKeyPanel, "HotkeyMenuA", HotkeyMenuAKeyTags);
            ApplyHotkeySnapshot(config, "MenuB", HotkeyMenuBComboBox, HotkeyMenuBKeyPanel, "HotkeyMenuB", HotkeyMenuBKeyTags);
            ApplyHotkeySnapshot(config, "MenuX", HotkeyMenuXComboBox, HotkeyMenuXKeyPanel, "HotkeyMenuX", HotkeyMenuXKeyTags);
            ApplyHotkeySnapshot(config, "MenuY", HotkeyMenuYComboBox, HotkeyMenuYKeyPanel, "HotkeyMenuY", HotkeyMenuYKeyTags);
            ApplyHotkeySnapshot(config, "MenuDpadUp", HotkeyMenuDpadUpComboBox, HotkeyMenuDpadUpKeyPanel, "HotkeyMenuDpadUp", HotkeyMenuDpadUpKeyTags);
            ApplyHotkeySnapshot(config, "MenuDpadDown", HotkeyMenuDpadDownComboBox, HotkeyMenuDpadDownKeyPanel, "HotkeyMenuDpadDown", HotkeyMenuDpadDownKeyTags);
            ApplyHotkeySnapshot(config, "MenuDpadLeft", HotkeyMenuDpadLeftComboBox, HotkeyMenuDpadLeftKeyPanel, "HotkeyMenuDpadLeft", HotkeyMenuDpadLeftKeyTags);
            ApplyHotkeySnapshot(config, "MenuDpadRight", HotkeyMenuDpadRightComboBox, HotkeyMenuDpadRightKeyPanel, "HotkeyMenuDpadRight", HotkeyMenuDpadRightKeyTags);
        }

        private void ApplyHotkeySnapshot(Windows.Data.Json.JsonObject config, string name, ComboBox combo, StackPanel keyPanel, string keyStore, ItemsControl tags)
        {
            int action = config.TryGetValue(name + "_Action", out var actionValue) ? (int)actionValue.GetNumber() : 0;
            string keys = config.TryGetValue(name + "_Key", out var keyValue) ? keyValue.GetString() ?? "" : "";
            SelectHotkeyComboBoxByTag(combo, action);
            LoadKeysFromString(keyStore, keys, tags);
            if (keyPanel != null) keyPanel.Visibility = action == 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        private void SelectHotkeyComboBoxByTag(ComboBox comboBox, int tagValue)
        {
            if (comboBox == null) return;

            // Find the item with matching Tag value
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && item.Tag is string tagStr)
                {
                    if (int.TryParse(tagStr, out int itemTag) && itemTag == tagValue)
                    {
                        comboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            // Default to first item if tag not found
            comboBox.SelectedIndex = 0;
        }

        private void HotkeyMenuA_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuA", HotkeyMenuAComboBox, HotkeyMenuAKeyPanel);
        }

        private void HotkeyMenuB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuB", HotkeyMenuBComboBox, HotkeyMenuBKeyPanel);
        }

        private void HotkeyMenuX_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuX", HotkeyMenuXComboBox, HotkeyMenuXKeyPanel);
        }

        private void HotkeyMenuY_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuY", HotkeyMenuYComboBox, HotkeyMenuYKeyPanel);
        }

        private void HotkeyMenuDpadUp_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadUp", HotkeyMenuDpadUpComboBox, HotkeyMenuDpadUpKeyPanel);
        }

        private void HotkeyMenuDpadDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadDown", HotkeyMenuDpadDownComboBox, HotkeyMenuDpadDownKeyPanel);
        }

        private void HotkeyMenuDpadLeft_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadLeft", HotkeyMenuDpadLeftComboBox, HotkeyMenuDpadLeftKeyPanel);
        }

        private void HotkeyMenuDpadRight_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleHotkeySelectionChanged("MenuDpadRight", HotkeyMenuDpadRightComboBox, HotkeyMenuDpadRightKeyPanel);
        }

        private void HandleHotkeySelectionChanged(string hotkeyName, ComboBox comboBox, StackPanel keyPanel)
        {
            if (isLoadingHotkeys) return;
            if (comboBox?.SelectedItem is ComboBoxItem selected && selected.Tag is string tagStr)
            {
                int action = int.Parse(tagStr);
                // Show/hide key panel based on action (1=Keyboard Shortcut)
                if (keyPanel != null)
                {
                    keyPanel.Visibility = (action == 1) ? Visibility.Visible : Visibility.Collapsed;
                }

                Logger.Info($"Hotkey {hotkeyName} action changed to {(HotkeyAction)action}");

                // Sync updated config to helper so its XInput monitor uses the new action
                SendControllerHotkeyConfigToHelper();
            }
        }
    }
}
