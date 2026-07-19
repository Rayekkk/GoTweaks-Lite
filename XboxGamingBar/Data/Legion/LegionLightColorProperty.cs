using Shared.Enums;
using System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using muxc = Microsoft.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion light color as hex string "#RRGGBB"
    /// Uses ColorPicker for selection
    /// </summary>
    internal class LegionLightColorProperty : WidgetControlProperty<string, muxc.ColorPicker>
    {
        /// <summary>
        /// Flag to indicate when the UI is being updated programmatically (from helper sync).
        /// When true, ColorChanged events should not trigger profile saves.
        /// </summary>
        public bool IsUpdatingUI { get; private set; }

        public LegionLightColorProperty(muxc.ColorPicker inUI, Page inOwner) : base("#FFFFFF", Function.LegionLightColor, inUI, inOwner)
        {
            // Don't initialize color in constructor - let the sync from helper set it
        }

        // [audit fix - Section 1, 2.0 rebuild] Removed SetFromProfile/HasSavedProfileColor/the Sync()
        // override - together they made the widget the source of truth for lighting color ("The
        // widget is the source of truth for lighting settings, not the helper", the override's own
        // former doc comment), permanently skipping all future helper->widget color sync for the rest
        // of the session the first time any non-white color reached SendLightingToHelper. That was
        // true pre-migration (the helper didn't persist lighting); since Faza slice 6 the helper DOES
        // persist + apply lighting via RouteProfileSave, so this directly contradicted the 2.0
        // architecture (widget = pure display, never overrides the helper). The call site
        // (SendLightingToHelper) now calls the inherited SetValue(colorHex) directly, same as its 4
        // sibling lighting fields (mode/speed/brightness/power light) - real Sync() now runs
        // unconditionally, like every other property.

        /// <summary>
        /// Called from the ColorChanged event handler in code-behind
        /// </summary>
        public void OnColorChanged(Color color)
        {
            string hexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (hexColor != Value)
            {
                Logger.Info($"{Function} color picker updated to {hexColor}.");
                SetValue(hexColor);
            }
        }

        private void UpdateUIColor()
        {
            try
            {
                if (UI != null && !string.IsNullOrEmpty(Value))
                {
                    Color color = ParseHexColor(Value);
                    // Set flag to prevent ColorChanged from triggering profile saves. Also bump the
                    // shared WidgetSliderProperty.HelperSyncCount [audit fix - Section 1]:
                    // LegionColorPicker_ColorChanged (GamingWidget.LegionGo.cs) unconditionally calls
                    // ControllerSliderSettingChanged after this UI.Color write - that handler only
                    // checks HelperSyncCount (plus isApplyingHelperUpdate, which OnColorChanged's own
                    // hexColor != Value check makes redundant here anyway), not this class's own
                    // IsUpdatingUI, so without this a helper-driven color push would still fall
                    // through as a live edit and re-trigger the send-everything bug this audit fixed.
                    IsUpdatingUI = true;
                    WidgetSliderProperty.HelperSyncCount++;
                    try
                    {
                        UI.Color = color;
                    }
                    finally
                    {
                        IsUpdatingUI = false;
                        WidgetSliderProperty.HelperSyncCount--;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update color picker: {ex.Message}");
            }
        }

        private Color ParseHexColor(string hexColor)
        {
            if (string.IsNullOrEmpty(hexColor))
                return Colors.White;

            string hex = hexColor.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
            return Colors.White;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (UI != null && Owner != null)
            {
                await Owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UpdateUIColor();
                });
            }
        }
    }
}
