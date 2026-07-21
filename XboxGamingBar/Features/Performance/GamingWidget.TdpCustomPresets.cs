using Microsoft.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    // Fixed TDP-mode model: the dropdown has exactly Quiet / Balanced / Performance / Custom.
    // (The former user-defined "Custom TDP Presets" system and TDP Boost were removed.)
    public sealed partial class GamingWidget
    {
        // Default per-mode TDP wattage shown for the three firmware modes.
        private static readonly int[] DefaultModeTdpValues = { 8, 15, 25 };   // Quiet, Balanced, Performance
        // Legion firmware mode value per dropdown index (Custom = 255).
        private static readonly int[] DefaultLegionModes = { 1, 2, 3, 255 };  // Quiet, Balanced, Performance, Custom

        private void PopulateTdpModeComboBox()
        {
            if (TDPModeComboBox == null) return;

            int previousIndex = TDPModeComboBox.SelectedIndex;

            TDPModeComboBox.Items.Clear();
            TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Quiet", Tag = "1" });
            TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Balanced", Tag = "2" });
            TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Performance", Tag = "3" });
            TDPModeComboBox.Items.Add(new ComboBoxItem { Content = "Custom", Tag = "255" });

            // Restore previous selection if still valid, else default to Balanced (index 1).
            TDPModeComboBox.SelectedIndex = (previousIndex >= 0 && previousIndex < TDPModeComboBox.Items.Count)
                ? previousIndex : 1;
        }

        /// <summary>TDP wattage for the selected mode; -1 in Custom mode (sliders control it).</summary>
        private int GetCurrentPresetTdpValue()
        {
            int selectedIndex = TDPModeComboBox?.SelectedIndex ?? -1;
            if (selectedIndex >= 0 && selectedIndex < DefaultModeTdpValues.Length)
                return DefaultModeTdpValues[selectedIndex];
            return -1; // Custom mode
        }

        /// <summary>Legion firmware mode value for the selected dropdown index (255 = Custom).</summary>
        private int GetCurrentPresetLegionMode()
        {
            int selectedIndex = TDPModeComboBox?.SelectedIndex ?? -1;
            if (selectedIndex >= 0 && selectedIndex < DefaultLegionModes.Length)
                return DefaultLegionModes[selectedIndex];
            return 255; // Custom mode
        }

        /// <summary>True when the "Custom" (slider-controlled) mode is selected.</summary>
        private bool IsCustomTdpModeSelected()
        {
            if (TDPModeComboBox == null) return true;
            return IsCustomTdpModeIndex(TDPModeComboBox.SelectedIndex);
        }

        /// <summary>True when the given dropdown index is the "Custom" mode (last item, index 3).</summary>
        private bool IsCustomTdpModeIndex(int index)
        {
            if (index < 0) return true;
            return index == 3;
        }

    }
}
