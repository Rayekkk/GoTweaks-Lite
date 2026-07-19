using NLog;
using Shared.Data;
using Shared.Enums;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using XboxGamingBarHelper.Core;
using XboxGamingBarHelper.Devices;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.Devices.Libraries.Legion
{
    internal partial class LegionManager
    {
        private int joystickAsMouseMode = 0;  // 0=Disabled, 1=Left Stick, 2=Right Stick
        private int joystickMouseSens = 50;   // 10-100

        /// <summary>
        /// Sets joystick as mouse mode (0=Disabled, 1=Left Stick, 2=Right Stick).
        /// </summary>
        public void SetJoystickAsMouseMode(int mode)
        {
            int previousMode = joystickAsMouseMode;
            joystickAsMouseMode = mode;

            // Disable previous stick if it was enabled
            if (previousMode == 1)
            {
                ApplyJoystickAsMouse(true, false, joystickMouseSens);
            }
            else if (previousMode == 2)
            {
                ApplyJoystickAsMouse(false, false, joystickMouseSens);
            }

            // Enable new stick if not disabled
            if (mode == 1)
            {
                ApplyJoystickAsMouse(true, true, joystickMouseSens);
            }
            else if (mode == 2)
            {
                ApplyJoystickAsMouse(false, true, joystickMouseSens);
            }
        }

        /// <summary>
        /// Sets joystick mouse sensitivity (10-100).
        /// </summary>
        public void SetJoystickMouseSens(int sensitivity)
        {
            joystickMouseSens = sensitivity;
            // Only apply if a joystick is active
            if (joystickAsMouseMode == 1)
            {
                ApplyJoystickAsMouse(true, true, sensitivity);
            }
            else if (joystickAsMouseMode == 2)
            {
                ApplyJoystickAsMouse(false, true, sensitivity);
            }
        }

        /// <summary>
        /// Applies joystick as mouse setting via HID command.
        /// </summary>
        private void ApplyJoystickAsMouse(bool isLeft, bool enabled, int sensitivity)
        {
            try
            {
                using var controller = new LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set joystick as mouse: controller not connected");
                    return;
                }

                var ctrl = isLeft ? Controller.Left : Controller.Right;
                bool success = controller.SetJoystickAsMouse(ctrl, enabled, sensitivity);
                if (success)
                {
                    Logger.Info($"{(isLeft ? "Left" : "Right")} joystick as mouse: enabled={enabled}, sensitivity={sensitivity}");
                }
                else
                {
                    Logger.Error($"Failed to set {(isLeft ? "left" : "right")} joystick as mouse");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting joystick as mouse: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies gamepad button mappings from JSON.
        /// JSON format: {"LSClick":{"Type":1,"GamepadAction":3,"KeyboardKeys":[],"MouseButton":0},...}
        /// </summary>
        public void ApplyGamepadButtonMappings(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Logger.Debug("No gamepad button mappings to apply");
                return;
            }

            try
            {
                using var controller = new LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot apply gamepad button mappings: controller not connected");
                    return;
                }

                // Parse the outer JSON to get button->mapping entries
                // Format: {"LSClick":{...},"A":{...},...}
                var buttonMatches = System.Text.RegularExpressions.Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*(\\{[^}]+\\})");

                foreach (System.Text.RegularExpressions.Match match in buttonMatches)
                {
                    string buttonName = match.Groups[1].Value;
                    string mappingJson = match.Groups[2].Value;

                    // Try to parse the button name to GamepadButton enum
                    if (!Enum.TryParse<GamepadButton>(buttonName, out var button))
                    {
                        Logger.Warn($"Unknown gamepad button: {buttonName}");
                        continue;
                    }

                    // Parse the mapping using existing ButtonMappingParser
                    var (type, gamepadAction, keyboardKeys, mouseButton) = ButtonMappingParser.Parse(mappingJson);

                    // Apply the mapping
                    if (type == 0 && gamepadAction == 0)
                    {
                        // Reset button to default: first clear, then remap to itself
                        // Step 1: Clear the existing mapping
                        controller.ClearGamepadButtonMapping(button);
                        System.Threading.Thread.Sleep(HID_COMMAND_DELAY_MS); // Delay between clear and remap

                        // Step 2: Map button to itself (default behavior)
                        // This is needed for all buttons including sticks to restore axis properly
                        controller.SetGamepadButtonMappingAdvanced(button, MappingType.Gamepad, new byte[] { (byte)button });
                        System.Threading.Thread.Sleep(HID_COMMAND_DELAY_MS); // Delay after remap before next button (fixes stick range issues)
                        Logger.Info($"Reset gamepad button {buttonName} to default (cleared then mapped to self: 0x{(byte)button:X2})");
                    }
                    else
                    {
                        var mappingType = (MappingType)(type + 1); // 0->Gamepad(1), 1->Keyboard(2), 2->Mouse(3)
                        byte[] mappings;

                        if (type == 0)
                        {
                            // Gamepad: use RemapActionHelper to convert dropdown index to HID button code
                            var action = RemapActionHelper.GetByIndex(gamepadAction);
                            mappings = new byte[] { (byte)action };
                        }
                        else if (type == 1)
                        {
                            // Keyboard: use raw key codes
                            mappings = keyboardKeys?.Select(k => (byte)k).ToArray() ?? Array.Empty<byte>();
                        }
                        else
                        {
                            // Mouse: convert dropdown index (0=Left, 1=Right, 2=Middle) to HID code (0x01, 0x02, 0x03)
                            mappings = new byte[] { (byte)(mouseButton + 1) };
                        }

                        if (mappings.Length > 0)
                        {
                            controller.SetGamepadButtonMappingAdvanced(button, mappingType, mappings);
                            System.Threading.Thread.Sleep(HID_COMMAND_DELAY_MS); // Delay after each mapping for firmware to process
                            Logger.Info($"Applied gamepad button {buttonName} mapping: type={mappingType}, values=[{string.Join(",", mappings.Select(b => $"0x{b:X2}"))}]");
                        }
                    }
                }

                Logger.Info($"Applied gamepad button mappings successfully");
            }
            catch (Exception ex)
            {
                Logger.Error($"Error applying gamepad button mappings: {ex.Message}");
            }
        }

        public void ApplyDesktopControlsPreset(bool enabled)
        {
            const string enabledMappings = "{\"DPadUp\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[82],\"MouseButton\":0},\"DPadDown\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[81],\"MouseButton\":0},\"DPadLeft\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[80],\"MouseButton\":0},\"DPadRight\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[79],\"MouseButton\":0},\"LSUp\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[82],\"MouseButton\":0},\"LSDown\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[81],\"MouseButton\":0},\"LSClick\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[227],\"MouseButton\":0},\"A\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[40],\"MouseButton\":0},\"B\":{\"Type\":1,\"GamepadAction\":0,\"KeyboardKeys\":[41],\"MouseButton\":0},\"LB\":{\"Type\":2,\"GamepadAction\":0,\"KeyboardKeys\":[],\"MouseButton\":0},\"LT\":{\"Type\":2,\"GamepadAction\":0,\"KeyboardKeys\":[],\"MouseButton\":1}}";
            const string disabledMappings = "{\"DPadUp\":{\"Type\":0,\"GamepadAction\":0},\"DPadDown\":{\"Type\":0,\"GamepadAction\":0},\"DPadLeft\":{\"Type\":0,\"GamepadAction\":0},\"DPadRight\":{\"Type\":0,\"GamepadAction\":0},\"LSUp\":{\"Type\":0,\"GamepadAction\":0},\"LSDown\":{\"Type\":0,\"GamepadAction\":0},\"LSClick\":{\"Type\":0,\"GamepadAction\":0},\"A\":{\"Type\":0,\"GamepadAction\":0},\"B\":{\"Type\":0,\"GamepadAction\":0},\"LB\":{\"Type\":0,\"GamepadAction\":0},\"LT\":{\"Type\":0,\"GamepadAction\":0}}";

            LegionJoystickAsMouseMode.SetValue(enabled ? 2 : 0);
            LegionGamepadMapping.SetValue(enabled ? enabledMappings : disabledMappings);
        }

    }
}
