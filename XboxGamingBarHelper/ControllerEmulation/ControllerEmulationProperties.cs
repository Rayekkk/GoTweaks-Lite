using NLog;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.ControllerEmulation
{
    internal class ControllerEmulationAvailableProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        public ControllerEmulationAvailableProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationAvailable, manager)
        {
        }
    }

    internal class ControllerEmulationEnabledProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationEnabledProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationEnabled, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationEnabled changed to {Value}");
            Manager?.SetEnabled(Value);
        }
    }

    internal class ControllerEmulationGyroActivationModeProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationGyroActivationModeProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationGyroActivationMode, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationGyroActivationMode changed to {Value}");
            Manager?.SetGyroActivationMode(Value);
        }
    }

    internal class ControllerEmulationGyroActivationButtonProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationGyroActivationButtonProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationGyroActivationButton, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationGyroActivationButton changed to {Value}");
            Manager?.SetGyroActivationButton(Value);
        }
    }

    internal class ControllerEmulationStickInvertXProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickInvertXProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickInvertX, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickInvertX changed to {Value}");
            Manager?.SetStickInvertX(Value);
        }
    }

    internal class ControllerEmulationStickInvertYProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickInvertYProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickInvertY, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickInvertY changed to {Value}");
            Manager?.SetStickInvertY(Value);
        }
    }

    internal class ControllerEmulationStickSelectProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickSelectProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickSelect, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickSelect changed to {Value}");
            Manager?.SetStickSelect(Value);
        }
    }

    internal class ControllerEmulationCalibrateGyroProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationCalibrateGyroProperty(ControllerEmulationManager manager)
            : base(false, null, Function.ControllerEmulationCalibrateGyro, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            if (Value)
            {
                Logger.Info("ControllerEmulationCalibrateGyro triggered");
                Manager?.CalibrateGyro();
                SetValue(false);
            }
        }
    }

    internal class ControllerEmulationStickSensitivityV2Property : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickSensitivityV2Property(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickSensitivityV2, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickSensitivityV2 changed to {Value}");
            Manager?.SetStickSensitivityV2(Value);
        }
    }

    internal class ControllerEmulationStickOrientationV2Property : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickOrientationV2Property(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickOrientationV2, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickOrientationV2 changed to {Value}");
            Manager?.SetStickOrientationV2(Value);
        }
    }

    internal class ControllerEmulationStickConversionProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickConversionProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickConversion, manager)
        {
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickConversion changed to {Value}");
            Manager?.SetStickConversion(Value);
        }
    }

    internal class ControllerEmulationStickGyroAntiDeadzoneProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickGyroAntiDeadzoneProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroAntiDeadzone, manager)
        {
        }

        // updatedTime==0 must be coerced to "now" here too - the value passed to base.SetValue
        // below is statically typed int, so it binds to the protected SetValue(ValueType,long)
        // overload directly, skipping the object-overload's own coercion (see
        // LegionPerformanceModeProperty.SetValue for the failure mode this caused elsewhere).
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(30, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroAntiDeadzone changed to {Value}");
            Manager?.SetStickGyroAntiDeadzone(Value);
        }
    }

    internal class ControllerEmulationStickGyroAntiDeadzoneThresholdProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ControllerEmulationStickGyroAntiDeadzoneThresholdProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroAntiDeadzoneThreshold, manager)
        {
        }

        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(50, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroAntiDeadzoneThreshold changed to {Value}");
            Manager?.SetStickGyroAntiDeadzoneThreshold(Value);
        }
    }

    /// <summary>
    /// Helper-to-widget calibration progress channel. Value is a JSON status
    /// message; updates fire each second of the calibration window so the
    /// widget can render a countdown and final bias offset.
    /// </summary>
    internal class ControllerEmulationCalibrateGyroStatusProperty : HelperProperty<string, ControllerEmulationManager>
    {
        public ControllerEmulationCalibrateGyroStatusProperty(ControllerEmulationManager manager)
            : base(string.Empty, null, Function.ControllerEmulationCalibrateGyroStatus, manager)
        {
        }
    }

    internal class ControllerEmulationStickGyroVerticalRatioProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroVerticalRatioProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroVerticalRatio, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(10, System.Math.Min(200, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroVerticalRatio changed to {Value}");
            Manager?.SetStickGyroVerticalRatio(Value);
        }
    }

    internal class ControllerEmulationStickGyroCurvePresetProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroCurvePresetProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroCurvePreset, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(2, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroCurvePreset changed to {Value}");
            Manager?.SetStickGyroCurvePreset(Value);
        }
    }

    internal class ControllerEmulationStickGyroTightenThresholdProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroTightenThresholdProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroTightenThreshold, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(500, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroTightenThreshold changed to {Value}");
            Manager?.SetStickGyroTightenThreshold(Value);
        }
    }

    internal class ControllerEmulationStickGyroTightenGainProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroTightenGainProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroTightenGain, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(100, System.Math.Min(300, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroTightenGain changed to {Value}");
            Manager?.SetStickGyroTightenGain(Value);
        }
    }

    internal class ControllerEmulationStickGyroTouchDeactivateEnabledProperty : HelperProperty<bool, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroTouchDeactivateEnabledProperty(bool initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroTouchDeactivateEnabled, manager) { }
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroTouchDeactivateEnabled changed to {Value}");
            Manager?.SetStickGyroTouchDeactivateEnabled(Value);
        }
    }

    internal class ControllerEmulationStickGyroTouchDeactivateThresholdProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroTouchDeactivateThresholdProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroTouchDeactivateThreshold, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(50, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroTouchDeactivateThreshold changed to {Value}");
            Manager?.SetStickGyroTouchDeactivateThreshold(Value);
        }
    }

    internal class ControllerEmulationStickGyroTouchDeactivateHoldoffProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroTouchDeactivateHoldoffProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroTouchDeactivateHoldoff, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(1000, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroTouchDeactivateHoldoff changed to {Value}");
            Manager?.SetStickGyroTouchDeactivateHoldoff(Value);
        }
    }

    internal class ControllerEmulationStickGyroSmoothingProperty : HelperProperty<int, ControllerEmulationManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public ControllerEmulationStickGyroSmoothingProperty(int initialValue, ControllerEmulationManager manager)
            : base(initialValue, null, Function.ControllerEmulationStickGyroSmoothing, manager) { }
        public override bool SetValue(object newValue, long updatedTime = 0)
            => base.SetValue(System.Math.Max(0, System.Math.Min(90, System.Convert.ToInt32(newValue))), updatedTime == 0 ? System.DateTime.Now.Ticks : updatedTime);
        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ControllerEmulationStickGyroSmoothing changed to {Value}");
            Manager?.SetStickGyroSmoothing(Value);
        }
    }

    /// <summary>
    /// Helper-to-widget live gyro readings. Pushed at ~5 Hz when the widget
    /// is open so the visualizer can show real-time per-axis magnitudes
    /// + final stick output + activation gate state.
    /// </summary>
    internal class ControllerEmulationStickGyroLiveReadingsProperty : HelperProperty<string, ControllerEmulationManager>
    {
        public ControllerEmulationStickGyroLiveReadingsProperty(ControllerEmulationManager manager)
            : base(string.Empty, null, Function.ControllerEmulationStickGyroLiveReadings, manager)
        {
        }
    }
}
