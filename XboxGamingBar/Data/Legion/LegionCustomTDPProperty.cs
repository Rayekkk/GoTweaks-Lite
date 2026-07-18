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
    //
    // [2.0 rebuild - TDP helper-authoritative] OnValueSyncedFromHelper reflects a HELPER-INITIATED
    // change (autonomous AC/DC reapply, profile switch on game launch/close, etc. - anything NOT
    // driven by this widget's own slider drag) back into the sliders via
    // GamingWidget.ApplyCustomTDPFromHelper, so the widget stops needing its own independent
    // recompute-and-push on those events (see LoadProfileSettings). SetCustomTDPSlidersSilent
    // already guards its slider writes with isUpdatingCustomTDPSliders, which
    // CustomTDPSlow/Fast/PeakSlider_ValueChanged already check - no separate echo guard needed here.

    /// <summary>Absolute Custom SPL (base TDP) in watts.</summary>
    internal class LegionCustomTDPSlowProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public LegionCustomTDPSlowProperty(GamingWidget inOwner) : base(15, null, Function.LegionCustomTDPSlow)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            if (owner == null) return;
            var ignore = owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => owner.ApplyCustomTDPFromHelper());
        }
    }

    /// <summary>Absolute Custom SPPT (= SPL + SPPT Boost) in watts.</summary>
    internal class LegionCustomTDPFastProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public LegionCustomTDPFastProperty(GamingWidget inOwner) : base(25, null, Function.LegionCustomTDPFast)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            if (owner == null) return;
            var ignore = owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => owner.ApplyCustomTDPFromHelper());
        }
    }

    /// <summary>Absolute Custom FPPT (= SPL + FPPT Boost) in watts.</summary>
    internal class LegionCustomTDPPeakProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public LegionCustomTDPPeakProperty(GamingWidget inOwner) : base(30, null, Function.LegionCustomTDPPeak)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            if (owner == null) return;
            var ignore = owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => owner.ApplyCustomTDPFromHelper());
        }
    }
}
