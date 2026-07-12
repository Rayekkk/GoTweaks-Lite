using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonAntiLagSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonAntiLagSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonAntiLagSupported, inManager)
        {
        }
    }

    internal class AMDRadeonAntiLagEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonAntiLagEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonAntiLagEnabled, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            bool prev = Value;
            bool result = base.SetValue(newValue, updatedTime);
            // Display-tab fix (same as AMDFluidMotionFrameEnabledProperty) - see that class for
            // the full explanation. Push to driver whenever the result was accepted, regardless
            // of whether the cached value changed.
            if (result && prev == Value)
            {
                Manager.AMDRadeonAntiLagSetting.SetEnabled(Value);
            }
            return result;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            Manager.AMDRadeonAntiLagSetting.SetEnabled(Value);
        }
    }
}
