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
        private void InitializeButtonMappingEvents(string buttonName)
        {
            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;
            var keyCombo = FindName($"LegionButton{buttonName}KeyComboBox") as ComboBox;

            EnsureButtonGamepadComboControls(buttonName);

            if (typeCombo != null)
            {
                typeCombo.SelectionChanged += (s, e) => OnButtonTypeChanged(buttonName);
            }
            if (gamepadCombo != null)
            {
                gamepadCombo.SelectionChanged += (s, e) => OnButtonGamepadActionSelected(buttonName);
            }
            if (mouseCombo != null)
            {
                mouseCombo.SelectionChanged += (s, e) => SendButtonMappingForName(buttonName);
            }
            if (keyCombo != null)
            {
                keyCombo.SelectionChanged += (s, e) => OnKeyboardKeySelected(buttonName);
            }
        }

        private void OnButtonTypeChanged(string buttonName)
        {
            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;
            var keyboardPanel = FindName($"LegionButton{buttonName}KeyboardPanel") as StackPanel;

            if (typeCombo == null) return;
            int type = typeCombo.SelectedIndex;

            // Show/hide appropriate controls
            if (gamepadCombo != null)
                gamepadCombo.Visibility = type == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (mouseCombo != null)
                mouseCombo.Visibility = type == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (keyboardPanel != null)
                keyboardPanel.Visibility = type == 1 ? Visibility.Visible : Visibility.Collapsed;

            UpdateButtonGamepadComboControls(buttonName);

            // Update the profile and send command
            if (!isLoadingControllerProfile)
            {
                SendButtonMappingForName(buttonName);
            }
        }

        private bool IsImprovedButtonComboUiEnabled()
        {
            // The legacy "Improved Input" toggle that used to gate this (default off,
            // never turned on since VIIPER became the sole emulation backend - see
            // CLAUDE.md SS21) was removed along with the rest of the dead legacy
            // Controller Emulation panel. No UI can set it to true anymore, so this
            // preserves the original default.
            return false;
        }

        private List<int> NormalizeGamepadActions(List<int> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return new List<int>();
            }

            var normalized = new List<int>();
            for (int i = 0; i < actions.Count; i++)
            {
                int action = actions[i];
                if (action <= 0)
                {
                    continue;
                }

                if (!normalized.Contains(action))
                {
                    normalized.Add(action);
                }
            }

            return normalized;
        }

        private List<int> GetStoredGamepadComboActions(string buttonName)
        {
            if (_buttonGamepadComboActions.TryGetValue(buttonName, out var actions))
            {
                return new List<int>(actions);
            }

            return new List<int>();
        }

        private void SetStoredGamepadComboActions(string buttonName, List<int> actions)
        {
            _buttonGamepadComboActions[buttonName] = NormalizeGamepadActions(actions);
        }

        private bool GetStoredButtonTurbo(string buttonName)
        {
            return _buttonGamepadTurbo.TryGetValue(buttonName, out var turbo) && turbo;
        }

        private void SetStoredButtonTurbo(string buttonName, bool turbo)
        {
            _buttonGamepadTurbo[buttonName] = turbo;
        }

        private int GetStoredButtonGamepadMode(string buttonName)
        {
            if (_buttonGamepadMode.TryGetValue(buttonName, out int mode))
            {
                return mode == 1 ? 1 : 0;
            }

            return 0;
        }

        private void SetStoredButtonGamepadMode(string buttonName, int mode)
        {
            _buttonGamepadMode[buttonName] = mode == 1 ? 1 : 0;
        }

        private void EnsureButtonGamepadComboControls(string buttonName)
        {
            if (_buttonGamepadComboRootPanels.ContainsKey(buttonName))
            {
                return;
            }

            var keyboardPanel = FindName($"LegionButton{buttonName}KeyboardPanel") as StackPanel;
            if (!(keyboardPanel?.Parent is StackPanel container))
            {
                return;
            }

            var rootPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(keyboardPanel.Margin.Left, 8, 0, 0)
            };

            var modeRow = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var modeCombo = new ComboBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };
            modeCombo.Items.Add("Single");
            modeCombo.Items.Add("Combo");

            Grid.SetColumn(modeCombo, 0);

            var addCombo = new ComboBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0,
                Visibility = Visibility.Collapsed
            };
            addCombo.Items.Add("+ Button");

            Grid.SetColumn(addCombo, 1);

            var turboCheck = new CheckBox
            {
                Content = "Turbo",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170))
            };
            Grid.SetColumn(turboCheck, 2);

            if (Resources.TryGetValue("ModernComboBoxStyle", out object comboStyleObj) && comboStyleObj is Style comboStyle)
            {
                modeCombo.Style = comboStyle;
            }

            modeRow.Children.Add(modeCombo);
            modeRow.Children.Add(addCombo);
            modeRow.Children.Add(turboCheck);

            var comboEditorRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var comboTags = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            // Indices align with RemapAction indices (1..N) by design.
            var sourceGamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            if (sourceGamepadCombo != null)
            {
                for (int action = 1; action < sourceGamepadCombo.Items.Count; action++)
                {
                    addCombo.Items.Add(GetGamepadActionName(action));
                }
            }

            if (Resources.TryGetValue("ModernComboBoxStyle", out object addComboStyleObj) && addComboStyleObj is Style addComboStyle)
            {
                addCombo.Style = addComboStyle;
            }

            comboEditorRow.Children.Add(comboTags);

            rootPanel.Children.Add(modeRow);
            rootPanel.Children.Add(comboEditorRow);
            container.Children.Add(rootPanel);

            _buttonGamepadComboRootPanels[buttonName] = rootPanel;
            _buttonGamepadModeCombos[buttonName] = modeCombo;
            _buttonGamepadComboEditorRows[buttonName] = comboEditorRow;
            _buttonGamepadComboTags[buttonName] = comboTags;
            _buttonGamepadComboAddCombos[buttonName] = addCombo;
            _buttonGamepadTurboChecks[buttonName] = turboCheck;

            modeCombo.SelectionChanged += (s, e) => OnButtonGamepadModeChanged(buttonName);
            addCombo.SelectionChanged += (s, e) => OnButtonGamepadComboActionSelected(buttonName);
            turboCheck.Click += (s, e) => OnButtonGamepadTurboToggled(buttonName);
        }

        private void OnButtonGamepadActionSelected(string buttonName)
        {
            if (GetStoredButtonGamepadMode(buttonName) == 0)
            {
                var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
                int selectedAction = gamepadCombo?.SelectedIndex ?? 0;
                SetStoredGamepadComboActions(buttonName, selectedAction > 0
                    ? new List<int> { selectedAction }
                    : new List<int>());
                UpdateGamepadComboActionTags(buttonName, GetStoredGamepadComboActions(buttonName));
            }

            if (!isLoadingControllerProfile)
            {
                SendButtonMappingForName(buttonName);
            }
        }

        private void OnButtonGamepadModeChanged(string buttonName)
        {
            if (isUpdatingButtonComboUi)
            {
                return;
            }

            if (!_buttonGamepadModeCombos.TryGetValue(buttonName, out ComboBox modeCombo))
            {
                return;
            }

            int mode = modeCombo.SelectedIndex == 1 ? 1 : 0;
            SetStoredButtonGamepadMode(buttonName, mode);

            if (mode == 0)
            {
                var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
                int selectedAction = gamepadCombo?.SelectedIndex ?? 0;
                SetStoredGamepadComboActions(buttonName, selectedAction > 0
                    ? new List<int> { selectedAction }
                    : new List<int>());
            }
            else
            {
                var actions = GetStoredGamepadComboActions(buttonName);
                if (actions.Count == 0)
                {
                    var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
                    int selectedAction = gamepadCombo?.SelectedIndex ?? 0;
                    if (selectedAction > 0)
                    {
                        SetStoredGamepadComboActions(buttonName, new List<int> { selectedAction });
                    }
                }
            }

            UpdateButtonGamepadComboControls(buttonName);
            if (!isLoadingControllerProfile)
            {
                SendButtonMappingForName(buttonName);
            }
        }

        private void OnButtonGamepadComboActionSelected(string buttonName)
        {
            if (isUpdatingButtonComboUi)
            {
                return;
            }

            if (!_buttonGamepadComboAddCombos.TryGetValue(buttonName, out ComboBox addCombo))
            {
                return;
            }

            int action = addCombo.SelectedIndex;
            if (action <= 0)
            {
                return;
            }

            var actions = GetStoredGamepadComboActions(buttonName);
            if (!actions.Contains(action))
            {
                actions.Add(action);
                SetStoredGamepadComboActions(buttonName, actions);
                UpdateGamepadComboActionTags(buttonName, actions);

                if (!isLoadingControllerProfile)
                {
                    SendButtonMappingForName(buttonName);
                }
            }

            addCombo.SelectedIndex = 0;
        }

        private void OnButtonGamepadTurboToggled(string buttonName)
        {
            if (isUpdatingButtonComboUi)
            {
                return;
            }

            if (!_buttonGamepadTurboChecks.TryGetValue(buttonName, out CheckBox turboCheck))
            {
                return;
            }

            SetStoredButtonTurbo(buttonName, turboCheck.IsChecked == true);
            if (!isLoadingControllerProfile)
            {
                SendButtonMappingForName(buttonName);
            }
        }

        private void RemoveGamepadComboActionFromButton(string buttonName, int action)
        {
            var actions = GetStoredGamepadComboActions(buttonName);
            actions.Remove(action);
            SetStoredGamepadComboActions(buttonName, actions);
            UpdateGamepadComboActionTags(buttonName, actions);

            if (!isLoadingControllerProfile)
            {
                SendButtonMappingForName(buttonName);
            }
        }

        private void UpdateGamepadComboActionTags(string buttonName, List<int> actions)
        {
            if (!_buttonGamepadComboTags.TryGetValue(buttonName, out StackPanel tagPanel) || tagPanel == null)
            {
                return;
            }

            tagPanel.Children.Clear();
            if (actions == null)
            {
                return;
            }

            foreach (int action in NormalizeGamepadActions(actions))
            {
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var tagContent = new StackPanel { Orientation = Orientation.Horizontal };
                var text = new TextBlock
                {
                    Text = GetGamepadActionName(action),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };

                int actionToRemove = action;
                removeButton.Click += (s, e) => RemoveGamepadComboActionFromButton(buttonName, actionToRemove);

                tagContent.Children.Add(text);
                tagContent.Children.Add(removeButton);
                tagBorder.Child = tagContent;
                tagPanel.Children.Add(tagBorder);
            }
        }

        private void UpdateButtonGamepadComboControls(string buttonName)
        {
            EnsureButtonGamepadComboControls(buttonName);
            if (!_buttonGamepadComboRootPanels.TryGetValue(buttonName, out StackPanel rootPanel))
            {
                return;
            }

            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            bool isGamepadType = typeCombo?.SelectedIndex == 0;
            bool showComboUi = IsImprovedButtonComboUiEnabled() && isGamepadType;

            rootPanel.Visibility = showComboUi ? Visibility.Visible : Visibility.Collapsed;
            if (!showComboUi)
            {
                if (gamepadCombo != null && isGamepadType)
                {
                    gamepadCombo.Visibility = Visibility.Visible;
                }

                return;
            }

            int mode = GetStoredButtonGamepadMode(buttonName);
            bool comboMode = mode == 1;

            isUpdatingButtonComboUi = true;
            try
            {
                if (_buttonGamepadModeCombos.TryGetValue(buttonName, out ComboBox modeCombo) &&
                    modeCombo != null &&
                    modeCombo.SelectedIndex != mode)
                {
                    modeCombo.SelectedIndex = mode;
                }

                if (_buttonGamepadTurboChecks.TryGetValue(buttonName, out CheckBox turboCheck) &&
                    turboCheck != null &&
                    turboCheck.IsChecked != GetStoredButtonTurbo(buttonName))
                {
                    turboCheck.IsChecked = GetStoredButtonTurbo(buttonName);
                }
            }
            finally
            {
                isUpdatingButtonComboUi = false;
            }

            if (_buttonGamepadComboEditorRows.TryGetValue(buttonName, out StackPanel editorRow) && editorRow != null)
            {
                editorRow.Visibility = comboMode ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_buttonGamepadComboAddCombos.TryGetValue(buttonName, out ComboBox addCombo) && addCombo != null)
            {
                addCombo.Visibility = comboMode ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateGamepadComboActionTags(buttonName, GetStoredGamepadComboActions(buttonName));

            if (gamepadCombo != null)
            {
                gamepadCombo.Visibility = comboMode ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void OnKeyboardKeySelected(string buttonName)
        {
            var keyCombo = FindName($"LegionButton{buttonName}KeyComboBox") as ComboBox;
            if (keyCombo == null || keyCombo.SelectedIndex <= 0) return;  // 0 is "+ Key"

            // Get the key code from the dropdown index
            int keyCode = GetKeyCodeFromDropdownIndex(keyCombo.SelectedIndex);
            if (keyCode == 0) return;

            // Get current keys and add the new one (max 5)
            var keys = GetStoredKeyboardKeys(buttonName);
            if (keys.Count >= 5)
            {
                keyCombo.SelectedIndex = 0;
                return;  // Max 5 keys
            }

            if (!keys.Contains(keyCode))
            {
                keys.Add(keyCode);
                SetStoredKeyboardKeys(buttonName, keys);
                UpdateKeyboardKeyTags(buttonName, keys);

                // Trigger profile save and command send
                if (!isLoadingControllerProfile)
                {
                    SendButtonMappingForName(buttonName);
                }
            }

            // Reset dropdown
            keyCombo.SelectedIndex = 0;
        }

        private int GetKeyCodeFromDropdownIndex(int index)
        {
            // Map dropdown index to HID key code
            // Index 0 is "+ Key" placeholder
            // Index 1-26 are A-Z (0x04-0x1D)
            // Index 27-36 are 1-0 (0x1E-0x27)
            // Index 37-48 are F1-F12 (0x3A-0x45)
            // Index 49-53 are Enter, Esc, Space, Tab, Backspace (0x28-0x2C)
            // Index 54-57 are Up, Down, Left, Right (0x52, 0x51, 0x50, 0x4F)
            // Index 58-65 are modifier keys (0xE0-0xE7)
            // Index 66-76 are navigation/media keys
            // Index 77-78 are bracket keys

            if (index <= 0) return 0;
            if (index <= 26) return 0x04 + (index - 1);   // A-Z: indices 1-26 → 0x04-0x1D
            if (index <= 36) return 0x1E + (index - 27);  // 1-0: indices 27-36 → 0x1E-0x27
            if (index <= 48) return 0x3A + (index - 37);  // F1-F12: indices 37-48 → 0x3A-0x45
            if (index == 49) return 0x28;  // Enter
            if (index == 50) return 0x29;  // Esc
            if (index == 51) return 0x2C;  // Space
            if (index == 52) return 0x2B;  // Tab
            if (index == 53) return 0x2A;  // Backspace
            if (index == 54) return 0x52;  // Up
            if (index == 55) return 0x51;  // Down
            if (index == 56) return 0x50;  // Left
            if (index == 57) return 0x4F;  // Right
            // Modifier keys
            if (index == 58) return 0xE0;  // LCtrl
            if (index == 59) return 0xE1;  // LShift
            if (index == 60) return 0xE2;  // LAlt
            if (index == 61) return 0xE3;  // LWin (HID Left GUI)
            if (index == 62) return 0xE4;  // RCtrl
            if (index == 63) return 0xE5;  // RShift
            if (index == 64) return 0xE6;  // RAlt
            if (index == 65) return 0xE7;  // RWin (HID Right GUI)
            // Navigation keys
            if (index == 66) return 0x4A;  // Home
            if (index == 67) return 0x4D;  // End
            if (index == 68) return 0x4B;  // PgUp
            if (index == 69) return 0x4E;  // PgDn
            if (index == 70) return 0x49;  // Insert
            if (index == 71) return 0x4C;  // Delete
            if (index == 72) return 0x46;  // PrintScr
            if (index == 73) return 0x48;  // Pause
            // Media keys (HID Keyboard page)
            if (index == 74) return 0x80;  // VolUp
            if (index == 75) return 0x81;  // VolDown
            if (index == 76) return 0x7F;  // VolMute
            // Bracket keys
            if (index == 77) return 0x2F;  // [ LeftBracket
            if (index == 78) return 0x30;  // ] RightBracket

            return 0;
        }

        private void RemoveKeyFromButton(string buttonName, int keyCode)
        {
            var keys = GetStoredKeyboardKeys(buttonName);
            keys.Remove(keyCode);
            SetStoredKeyboardKeys(buttonName, keys);
            UpdateKeyboardKeyTags(buttonName, keys);

            // Trigger profile save and command send
            if (!isLoadingControllerProfile)
            {
                SendButtonMappingForName(buttonName);
            }
        }

        private string FormatButtonMapping(ButtonMapping mapping)
        {
            if (mapping == null) return "none";
            switch (mapping.Type)
            {
                case 0:
                    if (mapping.GamepadMode == 1)
                    {
                        string actions = mapping.GamepadActions != null && mapping.GamepadActions.Count > 0
                            ? string.Join("+", mapping.GamepadActions)
                            : mapping.GamepadAction.ToString();
                        return $"GP:Combo[{actions}] {(mapping.Turbo ? "Turbo" : "")}".Trim();
                    }
                    return $"GP:{mapping.GamepadAction}{(mapping.Turbo ? " Turbo" : "")}";
                case 1: return $"KB:[{string.Join(",", mapping.KeyboardKeys)}]";
                case 2: return $"MS:{mapping.MouseButton}";
                default: return "?";
            }
        }

        private void ApplyButtonMappingToUI(string buttonName, ButtonMapping mapping)
        {
            // Find the controls by name using reflection-like approach
            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;
            var keyboardPanel = FindName($"LegionButton{buttonName}KeyboardPanel") as StackPanel;
            EnsureButtonGamepadComboControls(buttonName);

            if (mapping == null) mapping = new ButtonMapping();

            if (mapping.GamepadActions == null || mapping.GamepadActions.Count == 0)
            {
                if (mapping.GamepadAction > 0)
                {
                    mapping.GamepadActions = new List<int> { mapping.GamepadAction };
                }
                else
                {
                    mapping.GamepadActions = new List<int>();
                }
            }

            SetStoredGamepadComboActions(buttonName, mapping.GamepadActions);
            SetStoredButtonTurbo(buttonName, mapping.Turbo);
            int effectiveMode = mapping.GamepadMode == 1 || (mapping.GamepadActions?.Count ?? 0) > 1 ? 1 : 0;
            SetStoredButtonGamepadMode(buttonName, effectiveMode);

            // Set type dropdown
            if (typeCombo != null)
                typeCombo.SelectedIndex = mapping.Type;

            // Show/hide appropriate controls
            if (gamepadCombo != null)
            {
                gamepadCombo.Visibility = mapping.Type == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (mapping.Type == 0)
                    gamepadCombo.SelectedIndex = mapping.GamepadAction;
            }
            if (mouseCombo != null)
            {
                mouseCombo.Visibility = mapping.Type == 2 ? Visibility.Visible : Visibility.Collapsed;
                if (mapping.Type == 2)
                    mouseCombo.SelectedIndex = mapping.MouseButton;
            }
            if (keyboardPanel != null)
            {
                keyboardPanel.Visibility = mapping.Type == 1 ? Visibility.Visible : Visibility.Collapsed;
                if (mapping.Type == 1)
                    UpdateKeyboardKeyTags(buttonName, mapping.KeyboardKeys);
            }

            if (_buttonGamepadModeCombos.TryGetValue(buttonName, out ComboBox modeCombo) && modeCombo != null)
            {
                int mode = GetStoredButtonGamepadMode(buttonName);
                if (modeCombo.SelectedIndex != mode)
                {
                    modeCombo.SelectedIndex = mode;
                }
            }

            if (_buttonGamepadTurboChecks.TryGetValue(buttonName, out CheckBox turboCheck) && turboCheck != null)
            {
                bool turbo = GetStoredButtonTurbo(buttonName);
                if (turboCheck.IsChecked != turbo)
                {
                    turboCheck.IsChecked = turbo;
                }
            }

            UpdateGamepadComboActionTags(buttonName, GetStoredGamepadComboActions(buttonName));
            UpdateButtonGamepadComboControls(buttonName);
        }

        /// <summary>
        /// [2.0 rebuild - slice 7] Reflects a helper-pushed button remap into the composite UI.
        /// The eight Legion button-remap properties are now helper-authoritative: the helper
        /// persists + applies + pushes each mapping's JSON, and the widget rebuilds the per-button
        /// UI from it (the property is not auto-bound to a single control). Guarded by
        /// isLoadingControllerProfile so the ComboBox/tag updates don't fire ControllerSettingChanged
        /// back into a save/send. Called from LegionButtonMappingProperty.OnValueSyncedFromHelper
        /// on both sync paths (startup batch sync + individual Set push on game switch).
        /// </summary>
        internal void ApplyButtonMappingFromHelper(Function function, string json)
        {
            string buttonName = ButtonNameForFunction(function);
            if (buttonName == null) return;

            var mapping = ButtonMapping.FromJson(json);

            bool prev = isLoadingControllerProfile;
            isLoadingControllerProfile = true;
            try
            {
                // Seed the stored per-button metadata GetButtonMappingFromUI reads on later user
                // edits. ApplyButtonMappingToUI sets the gamepad-combo/turbo/mode stores from the
                // mapping but not the keyboard-key store, so set that explicitly here (mirrors
                // Helper-synchronized mapping data is applied without triggering a new user intent.
                SetStoredKeyboardKeys(buttonName, mapping.KeyboardKeys ?? new List<int>());
                ApplyButtonMappingToUI(buttonName, mapping);
            }
            finally
            {
                isLoadingControllerProfile = prev;
            }
        }

        private static string ButtonNameForFunction(Function function)
        {
            switch (function)
            {
                case Function.LegionButtonY1: return "Y1";
                case Function.LegionButtonY2: return "Y2";
                case Function.LegionButtonY3: return "Y3";
                case Function.LegionButtonM1: return "M1";
                case Function.LegionButtonM2: return "M2";
                case Function.LegionButtonM3: return "M3";
                case Function.LegionButtonDesktop: return "Desktop";
                case Function.LegionButtonPage: return "Page";
                default: return null;
            }
        }

        /// <summary>
        /// [2.0 rebuild - slice 8] Reflects a helper-pushed gamepad-button-mapping dict (the
        /// Nintendo-layout / Desktop-Controls preset expansions) into the in-memory
        /// gamepadButtonMappings dict. No UI is bound to this dict directly (§29 removed the
        /// standalone "Gamepad Buttons" arbitrary-remap section), so unlike
        /// ApplyButtonMappingFromHelper there's no composite UI to rebuild and no
        /// isLoadingControllerProfile guard needed - nothing reacts to a dict mutation by itself.
        /// Called from LegionGamepadMappingProperty.OnValueSyncedFromHelper (already dispatched to
        /// the UI thread there).
        /// </summary>
        internal void ApplyGamepadButtonMappingsFromHelper(string json)
        {
            gamepadButtonMappings = DeserializeGamepadButtonMappings(json);
        }

        private void UpdateKeyboardKeyTags(string buttonName, List<int> keys)
        {
            var keyTags = FindName($"LegionButton{buttonName}KeyTags") as StackPanel;
            if (keyTags == null) return;

            keyTags.Children.Clear();
            if (keys == null) return;

            foreach (var key in keys)
            {
                // Create a tag with the key name and X button to remove
                var tagBorder = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var tagPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var keyText = new TextBlock
                {
                    Text = GetKeyDisplayName(key),
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var removeButton = new Button
                {
                    Content = "×",
                    FontSize = 10,
                    Padding = new Thickness(4, 0, 0, 0),
                    Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 180, 180)),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0,
                    MinHeight = 0
                };

                // Capture the key code for the click handler
                int keyCode = key;
                string btnName = buttonName;
                removeButton.Click += (s, e) => RemoveKeyFromButton(btnName, keyCode);

                tagPanel.Children.Add(keyText);
                tagPanel.Children.Add(removeButton);
                tagBorder.Child = tagPanel;
                keyTags.Children.Add(tagBorder);
            }
        }

        private string GetKeyDisplayName(int keyCode)
        {
            // Map key codes to display names
            var keyNames = new Dictionary<int, string>
            {
                { 0x04, "A" }, { 0x05, "B" }, { 0x06, "C" }, { 0x07, "D" }, { 0x08, "E" },
                { 0x09, "F" }, { 0x0A, "G" }, { 0x0B, "H" }, { 0x0C, "I" }, { 0x0D, "J" },
                { 0x0E, "K" }, { 0x0F, "L" }, { 0x10, "M" }, { 0x11, "N" }, { 0x12, "O" },
                { 0x13, "P" }, { 0x14, "Q" }, { 0x15, "R" }, { 0x16, "S" }, { 0x17, "T" },
                { 0x18, "U" }, { 0x19, "V" }, { 0x1A, "W" }, { 0x1B, "X" }, { 0x1C, "Y" },
                { 0x1D, "Z" }, { 0x1E, "1" }, { 0x1F, "2" }, { 0x20, "3" }, { 0x21, "4" },
                { 0x22, "5" }, { 0x23, "6" }, { 0x24, "7" }, { 0x25, "8" }, { 0x26, "9" },
                { 0x27, "0" }, { 0x28, "Enter" }, { 0x29, "Esc" }, { 0x2A, "Backspace" },
                { 0x2B, "Tab" }, { 0x2C, "Space" }, { 0x2D, "-" }, { 0x2E, "=" },
                { 0x2F, "[" }, { 0x30, "]" }, { 0x31, "\\" }, { 0x33, ";" }, { 0x34, "'" },
                { 0x35, "`" }, { 0x36, "," }, { 0x37, "." }, { 0x38, "/" }, { 0x39, "CapsLock" },
                { 0x3A, "F1" }, { 0x3B, "F2" }, { 0x3C, "F3" }, { 0x3D, "F4" }, { 0x3E, "F5" },
                { 0x3F, "F6" }, { 0x40, "F7" }, { 0x41, "F8" }, { 0x42, "F9" }, { 0x43, "F10" },
                { 0x44, "F11" }, { 0x45, "F12" }, { 0x46, "PrtSc" }, { 0x47, "ScrLk" },
                { 0x48, "Pause" }, { 0x49, "Ins" }, { 0x4A, "Home" }, { 0x4B, "PgUp" },
                { 0x4C, "Del" }, { 0x4D, "End" }, { 0x4E, "PgDn" }, { 0x4F, "Right" },
                { 0x50, "Left" }, { 0x51, "Down" }, { 0x52, "Up" },
                // Modifier keys
                { 0xE0, "LCtrl" }, { 0xE1, "LShift" }, { 0xE2, "LAlt" }, { 0xE3, "LWin" },
                { 0xE4, "RCtrl" }, { 0xE5, "RShift" }, { 0xE6, "RAlt" }, { 0xE7, "RWin" },
                // Media keys
                { 0x7F, "VolMute" }, { 0x80, "VolUp" }, { 0x81, "VolDown" }
            };
            return keyNames.TryGetValue(keyCode, out var name) ? name : $"0x{keyCode:X2}";
        }

        private ButtonMapping GetButtonMappingFromUI(string buttonName)
        {
            var mapping = new ButtonMapping();

            var typeCombo = FindName($"LegionButton{buttonName}TypeComboBox") as ComboBox;
            var gamepadCombo = FindName($"LegionButton{buttonName}ComboBox") as ComboBox;
            var mouseCombo = FindName($"LegionButton{buttonName}MouseComboBox") as ComboBox;

            mapping.Type = typeCombo?.SelectedIndex ?? 0;
            mapping.MouseButton = mouseCombo?.SelectedIndex ?? 0;
            mapping.GamepadMode = GetStoredButtonGamepadMode(buttonName);
            mapping.Turbo = GetStoredButtonTurbo(buttonName);

            int singleAction = gamepadCombo?.SelectedIndex ?? 0;
            if (mapping.Type == 0)
            {
                if (mapping.GamepadMode == 1)
                {
                    var comboActions = GetStoredGamepadComboActions(buttonName);
                    mapping.GamepadActions = comboActions;
                    mapping.GamepadAction = comboActions.Count > 0 ? comboActions[0] : singleAction;
                }
                else
                {
                    mapping.GamepadAction = singleAction;
                    mapping.GamepadActions = singleAction > 0 ? new List<int> { singleAction } : new List<int>();
                }
            }
            else
            {
                mapping.GamepadAction = singleAction;
                mapping.GamepadActions = new List<int>();
                mapping.GamepadMode = 0;
                mapping.Turbo = false;
            }

            // Get keyboard keys from the stored list (maintained separately)
            var keyList = GetStoredKeyboardKeys(buttonName);
            mapping.KeyboardKeys = keyList;

            return mapping;
        }

        private static readonly string[] LegionRemapButtonNames = new[] { "Y1", "Y2", "Y3", "M1", "M2", "M3", "Desktop", "Page" };
        private readonly Dictionary<string, List<int>> _buttonKeyboardKeys = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, List<int>> _buttonGamepadComboActions = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, bool> _buttonGamepadTurbo = new Dictionary<string, bool>();
        private readonly Dictionary<string, int> _buttonGamepadMode = new Dictionary<string, int>();
        private readonly Dictionary<string, StackPanel> _buttonGamepadComboRootPanels = new Dictionary<string, StackPanel>();
        private readonly Dictionary<string, ComboBox> _buttonGamepadModeCombos = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, StackPanel> _buttonGamepadComboEditorRows = new Dictionary<string, StackPanel>();
        private readonly Dictionary<string, StackPanel> _buttonGamepadComboTags = new Dictionary<string, StackPanel>();
        private readonly Dictionary<string, ComboBox> _buttonGamepadComboAddCombos = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, CheckBox> _buttonGamepadTurboChecks = new Dictionary<string, CheckBox>();
        private bool isUpdatingButtonComboUi = false;

        private List<int> GetStoredKeyboardKeys(string buttonName)
        {
            if (_buttonKeyboardKeys.TryGetValue(buttonName, out var keys))
                return new List<int>(keys);
            return new List<int>();
        }

        private void SetStoredKeyboardKeys(string buttonName, List<int> keys)
        {
            _buttonKeyboardKeys[buttonName] = new List<int>(keys ?? new List<int>());
        }

                // Use 2 second window since HID commands take ~1.5s to complete (50ms per button × ~30 buttons)
        private ControllerProfile GetCurrentControllerProfileFromUI()
        {
            // This object is an ephemeral intent payload. UI values are helper-confirmed
            // display cache, so never fall back to a separate widget profile.
            byte colorR = LegionColorPicker != null ? LegionColorPicker.Color.R : (byte)255;
            byte colorG = LegionColorPicker != null ? LegionColorPicker.Color.G : (byte)255;
            byte colorB = LegionColorPicker != null ? LegionColorPicker.Color.B : (byte)255;

            return new ControllerProfile
            {
                ButtonY1 = GetButtonMappingFromUI("Y1"),
                ButtonY2 = GetButtonMappingFromUI("Y2"),
                ButtonY3 = GetButtonMappingFromUI("Y3"),
                ButtonM1 = GetButtonMappingFromUI("M1"),
                ButtonM2 = GetButtonMappingFromUI("M2"),
                ButtonM3 = GetButtonMappingFromUI("M3"),
                ButtonDesktop = GetButtonMappingFromUI("Desktop"),
                ButtonPage = GetButtonMappingFromUI("Page"),
                NintendoLayout = LegionNintendoLayoutToggle?.IsOn ?? false,
                VibrationLevel = LegionVibrationComboBox?.SelectedIndex ?? 2,
                VibrationMode = (LegionVibrationModeComboBox?.SelectedIndex ?? 0) + 1, // Index is 0-based, mode is 1-based
                // Gyro settings
                GyroTarget = LegionGyroTargetComboBox?.SelectedIndex ?? 0,
                GyroSensitivityX = (int)(LegionGyroSensitivityXSlider?.Value ?? 50),
                GyroSensitivityY = (int)(LegionGyroSensitivityYSlider?.Value ?? 50),
                GyroInvertX = LegionGyroInvertXToggle?.IsOn ?? false,
                GyroInvertY = LegionGyroInvertYToggle?.IsOn ?? false,
                GyroMappingType = LegionGyroMappingTypeComboBox?.SelectedIndex ?? 0,
                GyroActivationMode = LegionGyroActivationModeComboBox?.SelectedIndex ?? 0,
                GyroActivationButton = LegionGyroActivationButtonComboBox?.SelectedIndex ?? 0,
                // Advanced gyro settings
                GyroDeadzone = (int)(LegionGyroDeadzoneSlider?.Value ?? 10),
                // Stick deadzones
                LeftStickDeadzone = (int)(LegionLeftStickDeadzoneSlider?.Value ?? 4),
                RightStickDeadzone = (int)(LegionRightStickDeadzoneSlider?.Value ?? 4),
                // Trigger travel
                LeftTriggerStart = (int)(LegionLeftTriggerStartSlider?.Value ?? 0),
                LeftTriggerEnd = (int)(LegionLeftTriggerEndSlider?.Value ?? 0),
                RightTriggerStart = (int)(LegionRightTriggerStartSlider?.Value ?? 0),
                RightTriggerEnd = (int)(LegionRightTriggerEndSlider?.Value ?? 0),
                HairTriggers = LegionHairTriggersToggle?.IsOn ?? false,
                // Joystick as mouse
                JoystickAsMouseMode = LegionJoystickAsMouseComboBox?.SelectedIndex ?? 0,
                JoystickMouseSens = (int)(LegionJoystickMouseSensSlider?.Value ?? 50),
                // Gamepad button mappings
                GamepadButtonMappings = gamepadButtonMappings.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Clone()),
                // Desktop Controls preset
                DesktopControlsEnabled = LegionDesktopControlsToggle?.IsOn ?? false,
                // Lighting - preserve existing color if color picker not available
                LightMode = LegionLightModeComboBox?.SelectedIndex ?? 1,
                LightColorR = colorR,
                LightColorG = colorG,
                LightColorB = colorB,
                LightSpeed = (int)(LegionSpeedSlider?.Value ?? 50),
                LightBrightness = (int)(LegionBrightnessSlider?.Value ?? 50),
                PowerLight = LegionPowerLightToggle?.IsOn ?? true,
                HasExplicitLighting = true  // Mark as having explicit lighting since we're capturing from UI
            };
        }

        private void LegionControllerProfileToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // WidgetToggleProperty already relays this direct user intent to the helper.
            // The helper validates the active game, persists the profile flag and applies
            // the resulting scope. Never load/create/switch a widget-local profile here.
            if (isApplyingHelperUpdate)
                return;

            if (!HasValidGame(currentGameName))
            {
                Logger.Warn("Ignoring controller-profile enable request without a valid game");
                return;
            }

        }

        // Every regular controller control (combobox/toggle/slider) is already paired with its
        // own WidgetProperty, whose direct value IS the complete user intent - these two shared
        // handlers exist only to keep the on-screen slider value labels current; they never
        // rebuild or resend a controller/lighting snapshot (an earlier version did, via
        // ApplyControllerSettingChange, and that path caused a real bug where a deadzone-slider
        // drag could permanently block future helper->widget lighting-color sync - see git
        // history / memory controller-status-card-redesign.md if resurrecting anything similar).
        private void ControllerSettingChanged(object sender, object e)
        {
            UpdateControllerSliderDisplays(sender);
        }

        private void ControllerSliderSettingChanged(object sender, object e)
        {
            UpdateControllerSliderDisplays(sender);
        }

        private void SendButtonMappingForName(string buttonName)
        {
            if (isLoadingControllerProfile || isUnloading || isApplyingHelperUpdate
                || WidgetSliderProperty.HelperSyncCount > 0) return;
            var profile = GetCurrentControllerProfileFromUI();
            ButtonMapping mapping = null;
            LegionButtonMappingProperty property = null;
            switch (buttonName)
            {
                case "Y1": mapping = profile.ButtonY1; property = legionButtonY1; break;
                case "Y2": mapping = profile.ButtonY2; property = legionButtonY2; break;
                case "Y3": mapping = profile.ButtonY3; property = legionButtonY3; break;
                case "M1": mapping = profile.ButtonM1; property = legionButtonM1; break;
                case "M2": mapping = profile.ButtonM2; property = legionButtonM2; break;
                case "M3": mapping = profile.ButtonM3; property = legionButtonM3; break;
                case "Desktop": mapping = profile.ButtonDesktop; property = legionButtonDesktop; break;
                case "Page": mapping = profile.ButtonPage; property = legionButtonPage; break;
            }
            if (mapping != null && property != null) property.SendMapping(mapping.ToJson());
        }

        /// <summary>
        /// Sends only the gamepad-button-mapping dictionary (face/bumper/etc. arbitrary remaps +
        /// Nintendo/Desktop presets) to the helper. LegionGamepadButtonMapping itself is
        /// helper-authoritative (see WidgetProperties.NeverSyncFromHelper) - this method is purely
        /// a LIVE-EDIT send path (called from GamepadButtonRemapping's handlers and from
        /// SaveAndSendGamepadMappings for the Nintendo/Desktop-preset toggle handlers), not a seed.
        /// </summary>
        private void SendGamepadButtonMappingsToHelper(ControllerProfile profile)
        {
            // During profile loading, use gamepadButtonMappings (includes desktop control changes)
            // Otherwise use profile.GamepadButtonMappings
            var mappingsToSend = isLoadingControllerProfile ? gamepadButtonMappings : profile.GamepadButtonMappings;
            if (mappingsToSend != null && mappingsToSend.Count > 0)
            {
                legionGamepadMapping?.SetValue(SerializeGamepadButtonMappings(mappingsToSend));
            }
            else
            {
                legionGamepadMapping?.SetValue("");
            }
        }

        /// <summary>
        /// Updates the display text for controller setting sliders
        /// </summary>
        private void UpdateControllerSliderDisplays(object sender)
        {
            try
            {
                // Gyro sensitivity sliders
                if (sender == LegionGyroSensitivityXSlider && LegionGyroSensitivityXValue != null)
                    LegionGyroSensitivityXValue.Text = ((int)LegionGyroSensitivityXSlider.Value).ToString();
                else if (sender == LegionGyroSensitivityYSlider && LegionGyroSensitivityYValue != null)
                    LegionGyroSensitivityYValue.Text = ((int)LegionGyroSensitivityYSlider.Value).ToString();
                // Advanced gyro sliders
                else if (sender == LegionGyroDeadzoneSlider && LegionGyroDeadzoneValue != null)
                    LegionGyroDeadzoneValue.Text = ((int)LegionGyroDeadzoneSlider.Value).ToString();
                // Stick deadzone sliders
                else if (sender == LegionLeftStickDeadzoneSlider && LegionLeftStickDeadzoneValue != null)
                    LegionLeftStickDeadzoneValue.Text = $"{(int)LegionLeftStickDeadzoneSlider.Value}%";
                else if (sender == LegionRightStickDeadzoneSlider && LegionRightStickDeadzoneValue != null)
                    LegionRightStickDeadzoneValue.Text = $"{(int)LegionRightStickDeadzoneSlider.Value}%";
                // Trigger travel sliders
                else if (sender == LegionLeftTriggerStartSlider && LegionLeftTriggerStartValue != null)
                    LegionLeftTriggerStartValue.Text = $"{(int)LegionLeftTriggerStartSlider.Value}%";
                else if (sender == LegionLeftTriggerEndSlider && LegionLeftTriggerEndValue != null)
                    LegionLeftTriggerEndValue.Text = $"{(int)LegionLeftTriggerEndSlider.Value}%";
                else if (sender == LegionRightTriggerStartSlider && LegionRightTriggerStartValue != null)
                    LegionRightTriggerStartValue.Text = $"{(int)LegionRightTriggerStartSlider.Value}%";
                else if (sender == LegionRightTriggerEndSlider && LegionRightTriggerEndValue != null)
                    LegionRightTriggerEndValue.Text = $"{(int)LegionRightTriggerEndSlider.Value}%";
                // Joystick as mouse sensitivity slider
                else if (sender == LegionJoystickMouseSensSlider && LegionJoystickMouseSensValue != null)
                    LegionJoystickMouseSensValue.Text = ((int)LegionJoystickMouseSensSlider.Value).ToString();
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error updating controller slider display: {ex.Message}");
            }
        }
    }
}
