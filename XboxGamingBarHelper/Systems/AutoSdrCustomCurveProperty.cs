using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Systems
{
    /// <summary>
    /// The Custom preset's brightness->SDR curve, as a Go2HDR-compatible flat JSON array. Set
    /// from the widget when the user edits a point (+/-1 spinner) or imports a curve file.
    /// Persistence + validation are routed through SystemManager, same split as
    /// AutoSdrEnabledProperty.
    /// </summary>
    internal class AutoSdrCustomCurveProperty : HelperProperty<string, SystemManager>
    {
        public AutoSdrCustomCurveProperty(string inValue, SystemManager inManager)
            : base(inValue, null, Function.AutoSdrCustomCurve, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.SetAutoSdrCustomCurve(Value);
        }
    }
}
