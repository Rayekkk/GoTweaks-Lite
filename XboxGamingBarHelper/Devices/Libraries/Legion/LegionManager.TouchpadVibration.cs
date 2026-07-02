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
        private int touchpadVibrationLevel = 3;  // 1=Off, 2=Low, 3=Medium, 4=High
        private const string TouchpadVibrationKey = "TouchpadVibrationLevel";

        /// <summary>
        /// Restores the persisted touchpad vibration level into <see cref="touchpadVibrationLevel"/>.
        /// Called once at startup BEFORE the LegionTouchpadVibration property is built so the widget
        /// sees the saved value (not the hard default). The helper owns persistence here because the
        /// widget keeps no profile entry for this GLOBAL setting.
        /// </summary>
        private void LoadTouchpadVibrationLevel()
        {
            try
            {
                if (XboxGamingBarHelper.Settings.LocalSettingsHelper.TryGetValue<int>(TouchpadVibrationKey, out var saved)
                    && saved >= 1 && saved <= 4)
                {
                    touchpadVibrationLevel = saved;
                    Logger.Info($"Touchpad vibration restored from storage: level {saved}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to restore touchpad vibration level: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the touchpad vibration (haptic feedback) level.
        /// This is a GLOBAL setting, not per-game.
        /// </summary>
        /// <param name="level">1=Off, 2=Low, 3=Medium, 4=High</param>
        public void SetTouchpadVibration(int level)
        {
            try
            {
                if (level < 1 || level > 4)
                {
                    Logger.Warn($"Invalid touchpad vibration level: {level}");
                    return;
                }

                using var controller = new LegionGoController();
                if (!controller.Connect())
                {
                    Logger.Warn("Cannot set touchpad vibration: controller not connected");
                    return;
                }

                var vibLevel = (TouchpadVibrationLevel)level;
                bool success = controller.SetTouchpadVibration(vibLevel);
                if (success)
                {
                    touchpadVibrationLevel = level;
                    // Persist so the GLOBAL setting survives a console/helper restart (the widget
                    // keeps no profile entry for it, so the helper is the source of truth).
                    try { XboxGamingBarHelper.Settings.LocalSettingsHelper.SetValue(TouchpadVibrationKey, level); }
                    catch (Exception ex) { Logger.Warn($"Failed to persist touchpad vibration level: {ex.Message}"); }
                    string levelName = level switch
                    {
                        1 => "Off",
                        2 => "Low",
                        3 => "Medium",
                        4 => "High",
                        _ => "Unknown"
                    };
                    Logger.Info($"Touchpad vibration set to {levelName}");
                }
                else
                {
                    Logger.Error($"Failed to set touchpad vibration to level {level}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting touchpad vibration: {ex.Message}");
            }
        }

    }
}
