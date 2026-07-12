using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonSuperResolutionSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonSuperResolutionSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionSupported, inManager)
        {
        }
    }

    internal class AMDRadeonSuperResolutionEnabledProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonSuperResolutionEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionEnabled, inManager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            bool prev = Value;
            bool result = base.SetValue(newValue, updatedTime);
            // Display-tab fix (same as AMDFluidMotionFrameEnabledProperty): GenericProperty.
            // SetValue's equality skip prevents NotifyPropertyChanged from firing when our
            // cached value matches the incoming value, so the SetEnabled call below never
            // reaches ADLX. The cache can drift from actual driver state (user toggled RSR in
            // Adrenalin directly, a prior SetEnabled silently failed, etc.), so the Display-tab
            // toggle would persist in the widget UI while the driver stays in its previous
            // state. Push to driver whenever the result was accepted, regardless of whether the
            // value changed.
            if (result && prev == Value)
            {
                Manager.AMD3DSettingsChangedListener?.NotifyRSRChanged();
                Manager.AMDRadeonSuperResolutionSetting.SetEnabled(Value);
            }
            return result;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            // Notify the listener to start cooldown before we make the change
            // This prevents the listener from reading stale values when the driver callback fires
            Manager.AMD3DSettingsChangedListener?.NotifyRSRChanged();

            Manager.AMDRadeonSuperResolutionSetting.SetEnabled(Value);
        }
    }

    internal class AMDRadeonSuperResolutionSharpnessProperty : HelperProperty<int, AMDManager>
    {
        public AMDRadeonSuperResolutionSharpnessProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonSuperResolutionSharpness, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            (int min, int max) = Manager.AMDRadeonSuperResolutionSetting.GetSharpnessRange();
            if (min == 0 && max == 100)
            {
                Manager.AMDRadeonSuperResolutionSetting.SetSharpness(Value);
            }
            else
            {
                Manager.AMDRadeonSuperResolutionSetting.SetSharpness((int)Math.Round(min + Value / 100.0f * (max - min)));
            }
        }
    }
}
