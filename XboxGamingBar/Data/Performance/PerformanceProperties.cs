using Shared.Enums;

namespace XboxGamingBar.Data
{
    // Performance/CPU properties synced to the helper: the TDP limits readout. (Formerly lived
    // under Data\AutoTDP\ — AutoTDP itself was removed; this unrelated property stayed. The CPU
    // core parking/affinity + Force Park Mode properties were removed with the Advanced panel.)
    internal class TDPLimitsProperty : WidgetProperty<string>
    {
        public TDPLimitsProperty(string inValue) : base(inValue, null, Function.TDPLimits)
        {
        }
    }
}
