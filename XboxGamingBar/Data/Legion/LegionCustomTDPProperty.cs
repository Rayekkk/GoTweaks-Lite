using Shared.Enums;

namespace XboxGamingBar.Data
{
    // These three properties are the WIRE channels that carry the ABSOLUTE Custom power limits
    // (SPL / SPPT / FPPT in watts) to the helper, which writes them straight through Lenovo WMI.
    //
    // They are intentionally NOT WidgetSliderProperty (no slider binding, no 500ms debounce): the
    // Custom-mode UI shows "TDP / SPPT Boost / FPPT Boost" sliders whose values are the base SPL and
    // two *relative* boosts. The widget (GamingWidget.LegionGo.cs) computes the absolute SPL/SPPT/FPPT
    // from those sliders and pushes them here via ForceSetValue on every slider change, so the limits
    // apply live through WMI while dragging. The absolute values are what gets persisted per profile
    // (GameProfile.TDP/TDPFast/TDPPeak), keeping wire + profile format backwards compatible.

    /// <summary>Absolute Custom SPL (base TDP) in watts.</summary>
    internal class LegionCustomTDPSlowProperty : WidgetProperty<int>
    {
        public LegionCustomTDPSlowProperty() : base(15, null, Function.LegionCustomTDPSlow)
        {
        }
    }

    /// <summary>Absolute Custom SPPT (= SPL + SPPT Boost) in watts.</summary>
    internal class LegionCustomTDPFastProperty : WidgetProperty<int>
    {
        public LegionCustomTDPFastProperty() : base(25, null, Function.LegionCustomTDPFast)
        {
        }
    }

    /// <summary>Absolute Custom FPPT (= SPL + FPPT Boost) in watts.</summary>
    internal class LegionCustomTDPPeakProperty : WidgetProperty<int>
    {
        public LegionCustomTDPPeakProperty() : base(30, null, Function.LegionCustomTDPPeak)
        {
        }
    }
}
