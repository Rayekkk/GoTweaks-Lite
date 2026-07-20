using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.AMD.Properties
{
    internal class AMDRadeonChillSupportedProperty : HelperProperty<bool, AMDManager>
    {
        public AMDRadeonChillSupportedProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillSupported, inManager)
        {
        }
    }

    internal class AMDRadeonChillEnabledProperty : HelperProperty<bool, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonChillEnabledProperty(bool inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillEnabled, inManager)
        {
        }

        // [2026-07-20 simplification] GoTweaks->AMD is now the only sync direction - always push
        // to the driver.
        public override bool SetValue(object newValue, long updatedTime = 0)
        {
            bool prev = Value;
            bool result = base.SetValue(newValue, updatedTime);
            // Display-tab fix (same as AMDFluidMotionFrameEnabledProperty) - see that class for
            // the full explanation. Push to driver whenever the result was accepted, regardless
            // of whether the cached value changed.
            if (result && prev == Value)
            {
                ApplyToDriver();
            }
            return result;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            ApplyToDriver();
        }

        private void ApplyToDriver()
        {
            LastApplySucceeded = Manager.AMDRadeonChillSetting.SetEnabled(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Chill could not be applied.";
        }
    }

    internal class AMDRadeonChillMinFPSProperty : HelperProperty<int, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonChillMinFPSProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillMinFPS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            LastApplySucceeded = Manager.AMDRadeonChillSetting.SetMinFPS(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Chill minimum FPS could not be applied.";
        }
    }

    internal class AMDRadeonChillMaxFPSProperty : HelperProperty<int, AMDManager>, IHardwareApplyResult
    {
        public bool LastApplySucceeded { get; private set; } = true;
        public string LastApplyFailureReason { get; private set; }

        public AMDRadeonChillMaxFPSProperty(int inValue, AMDManager inManager) : base(inValue, null, Function.AMDRadeonChillMaxFPS, inManager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            LastApplySucceeded = Manager.AMDRadeonChillSetting.SetMaxFPS(Value);
            LastApplyFailureReason = LastApplySucceeded ? null : "Radeon Chill maximum FPS could not be applied.";
        }
    }
}
