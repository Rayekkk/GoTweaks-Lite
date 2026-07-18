using Shared.Data;
using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Profile
{
    internal class GameProfileProperty : HelperProperty<GameProfile, ProfileManager>
    {
        public GameProfileProperty(GameProfile inValue, ProfileManager inManager) : base(inValue, null, Function.None, inManager)
        {
        }

        public int TDP
        {
            get { return value.TDP; }
            set
            {
                if (this.value.TDP != value)
                {
                    this.value.TDP = value;
                }
            }
        }

        // [TDP Custom-mode fix] Needed so CurrentProfile_PropertyChanged can read the Custom SPPT/
        // FPPT triplet (GameProfile.TDPFast/TDPPeak's getters already fall back to TDP/SPL when
        // unset, so these never read as 0).
        public int TDPFast
        {
            get { return value.TDPFast; }
            set
            {
                if (this.value.TDPFast != value)
                {
                    this.value.TDPFast = value;
                }
            }
        }

        public int TDPPeak
        {
            get { return value.TDPPeak; }
            set
            {
                if (this.value.TDPPeak != value)
                {
                    this.value.TDPPeak = value;
                }
            }
        }

        public bool CPUBoost
        {
            get { return value.CPUBoost; }
            set
            {
                if (this.value.CPUBoost != value)
                {
                    this.value.CPUBoost = value;
                }
            }
        }

        public int CPUEPP
        {
            get { return value.CPUEPP; }
            set
            {
                if (this.value.CPUEPP != value)
                {
                    this.value.CPUEPP = value;
                }
            }
        }

        public int MaxCPUState
        {
            get { return value.MaxCPUState; }
            set
            {
                if (this.value.MaxCPUState != value)
                {
                    this.value.MaxCPUState = value;
                }
            }
        }

        public int MinCPUState
        {
            get { return value.MinCPUState; }
            set
            {
                if (this.value.MinCPUState != value)
                {
                    this.value.MinCPUState = value;
                }
            }
        }

        // [2.0 rebuild - Faza C1] Performance-tab settings whose GameProfile schema already
        // existed but had no GameProfileProperty proxy yet. Nullable (code review fix) - see
        // GameProfile.cs's FPSLimit/HDREnabled comments for why.
        public int? FPSLimit
        {
            get { return value.FPSLimit; }
            set
            {
                if (this.value.FPSLimit != value)
                {
                    this.value.FPSLimit = value;
                }
            }
        }

        public bool? HDREnabled
        {
            get { return value.HDREnabled; }
            set
            {
                if (this.value.HDREnabled != value)
                {
                    this.value.HDREnabled = value;
                }
            }
        }

        public string Resolution
        {
            get { return value.Resolution; }
            set
            {
                if (this.value.Resolution != value)
                {
                    this.value.Resolution = value;
                }
            }
        }

        public int? RefreshRate
        {
            get { return value.RefreshRate; }
            set
            {
                if (this.value.RefreshRate != value)
                {
                    this.value.RefreshRate = value;
                }
            }
        }

        // [2.0 rebuild - Faza C2] AMD Radeon per-game feature toggles.
        public bool? FluidMotionFrames
        {
            get { return value.FluidMotionFrames; }
            set
            {
                if (this.value.FluidMotionFrames != value)
                {
                    this.value.FluidMotionFrames = value;
                }
            }
        }

        public bool? RadeonSuperResolution
        {
            get { return value.RadeonSuperResolution; }
            set
            {
                if (this.value.RadeonSuperResolution != value)
                {
                    this.value.RadeonSuperResolution = value;
                }
            }
        }

        public int? RadeonSuperResolutionSharpness
        {
            get { return value.RadeonSuperResolutionSharpness; }
            set
            {
                if (this.value.RadeonSuperResolutionSharpness != value)
                {
                    this.value.RadeonSuperResolutionSharpness = value;
                }
            }
        }

        public bool? ImageSharpening
        {
            get { return value.ImageSharpening; }
            set
            {
                if (this.value.ImageSharpening != value)
                {
                    this.value.ImageSharpening = value;
                }
            }
        }

        public int? ImageSharpeningSharpness
        {
            get { return value.ImageSharpeningSharpness; }
            set
            {
                if (this.value.ImageSharpeningSharpness != value)
                {
                    this.value.ImageSharpeningSharpness = value;
                }
            }
        }

        public bool? RadeonAntiLag
        {
            get { return value.RadeonAntiLag; }
            set
            {
                if (this.value.RadeonAntiLag != value)
                {
                    this.value.RadeonAntiLag = value;
                }
            }
        }

        public bool? RadeonBoost
        {
            get { return value.RadeonBoost; }
            set
            {
                if (this.value.RadeonBoost != value)
                {
                    this.value.RadeonBoost = value;
                }
            }
        }

        public int? RadeonBoostResolution
        {
            get { return value.RadeonBoostResolution; }
            set
            {
                if (this.value.RadeonBoostResolution != value)
                {
                    this.value.RadeonBoostResolution = value;
                }
            }
        }

        public bool? RadeonChill
        {
            get { return value.RadeonChill; }
            set
            {
                if (this.value.RadeonChill != value)
                {
                    this.value.RadeonChill = value;
                }
            }
        }

        public int? RadeonChillMinFPS
        {
            get { return value.RadeonChillMinFPS; }
            set
            {
                if (this.value.RadeonChillMinFPS != value)
                {
                    this.value.RadeonChillMinFPS = value;
                }
            }
        }

        public int? RadeonChillMaxFPS
        {
            get { return value.RadeonChillMaxFPS; }
            set
            {
                if (this.value.RadeonChillMaxFPS != value)
                {
                    this.value.RadeonChillMaxFPS = value;
                }
            }
        }

        // [2.0 rebuild - AC/DC persistence] DC (battery) override proxies. Same null-means-
        // no-override convention as their AC counterparts above; needed so RouteProfileSave's
        // onCurrent lambda (Action<GameProfileProperty>) can write them.
        public int? TDP_DC
        {
            get { return value.TDP_DC; }
            set
            {
                if (this.value.TDP_DC != value)
                {
                    this.value.TDP_DC = value;
                }
            }
        }

        public int? TDPFast_DC
        {
            get { return value.TDPFast_DC; }
            set
            {
                if (this.value.TDPFast_DC != value)
                {
                    this.value.TDPFast_DC = value;
                }
            }
        }

        public int? TDPPeak_DC
        {
            get { return value.TDPPeak_DC; }
            set
            {
                if (this.value.TDPPeak_DC != value)
                {
                    this.value.TDPPeak_DC = value;
                }
            }
        }

        public bool? CPUBoost_DC
        {
            get { return value.CPUBoost_DC; }
            set
            {
                if (this.value.CPUBoost_DC != value)
                {
                    this.value.CPUBoost_DC = value;
                }
            }
        }

        public int? CPUEPP_DC
        {
            get { return value.CPUEPP_DC; }
            set
            {
                if (this.value.CPUEPP_DC != value)
                {
                    this.value.CPUEPP_DC = value;
                }
            }
        }

        public int? MaxCPUState_DC
        {
            get { return value.MaxCPUState_DC; }
            set
            {
                if (this.value.MaxCPUState_DC != value)
                {
                    this.value.MaxCPUState_DC = value;
                }
            }
        }

        public int? MinCPUState_DC
        {
            get { return value.MinCPUState_DC; }
            set
            {
                if (this.value.MinCPUState_DC != value)
                {
                    this.value.MinCPUState_DC = value;
                }
            }
        }

        public int? FPSLimit_DC
        {
            get { return value.FPSLimit_DC; }
            set
            {
                if (this.value.FPSLimit_DC != value)
                {
                    this.value.FPSLimit_DC = value;
                }
            }
        }

        public bool? HDREnabled_DC
        {
            get { return value.HDREnabled_DC; }
            set
            {
                if (this.value.HDREnabled_DC != value)
                {
                    this.value.HDREnabled_DC = value;
                }
            }
        }

        public string Resolution_DC
        {
            get { return value.Resolution_DC; }
            set
            {
                if (this.value.Resolution_DC != value)
                {
                    this.value.Resolution_DC = value;
                }
            }
        }

        public int? RefreshRate_DC
        {
            get { return value.RefreshRate_DC; }
            set
            {
                if (this.value.RefreshRate_DC != value)
                {
                    this.value.RefreshRate_DC = value;
                }
            }
        }

        public bool? FluidMotionFrames_DC
        {
            get { return value.FluidMotionFrames_DC; }
            set
            {
                if (this.value.FluidMotionFrames_DC != value)
                {
                    this.value.FluidMotionFrames_DC = value;
                }
            }
        }

        public bool? RadeonSuperResolution_DC
        {
            get { return value.RadeonSuperResolution_DC; }
            set
            {
                if (this.value.RadeonSuperResolution_DC != value)
                {
                    this.value.RadeonSuperResolution_DC = value;
                }
            }
        }

        public int? RadeonSuperResolutionSharpness_DC
        {
            get { return value.RadeonSuperResolutionSharpness_DC; }
            set
            {
                if (this.value.RadeonSuperResolutionSharpness_DC != value)
                {
                    this.value.RadeonSuperResolutionSharpness_DC = value;
                }
            }
        }

        public bool? ImageSharpening_DC
        {
            get { return value.ImageSharpening_DC; }
            set
            {
                if (this.value.ImageSharpening_DC != value)
                {
                    this.value.ImageSharpening_DC = value;
                }
            }
        }

        public int? ImageSharpeningSharpness_DC
        {
            get { return value.ImageSharpeningSharpness_DC; }
            set
            {
                if (this.value.ImageSharpeningSharpness_DC != value)
                {
                    this.value.ImageSharpeningSharpness_DC = value;
                }
            }
        }

        public bool? RadeonAntiLag_DC
        {
            get { return value.RadeonAntiLag_DC; }
            set
            {
                if (this.value.RadeonAntiLag_DC != value)
                {
                    this.value.RadeonAntiLag_DC = value;
                }
            }
        }

        public bool? RadeonBoost_DC
        {
            get { return value.RadeonBoost_DC; }
            set
            {
                if (this.value.RadeonBoost_DC != value)
                {
                    this.value.RadeonBoost_DC = value;
                }
            }
        }

        public int? RadeonBoostResolution_DC
        {
            get { return value.RadeonBoostResolution_DC; }
            set
            {
                if (this.value.RadeonBoostResolution_DC != value)
                {
                    this.value.RadeonBoostResolution_DC = value;
                }
            }
        }

        public bool? RadeonChill_DC
        {
            get { return value.RadeonChill_DC; }
            set
            {
                if (this.value.RadeonChill_DC != value)
                {
                    this.value.RadeonChill_DC = value;
                }
            }
        }

        public int? RadeonChillMinFPS_DC
        {
            get { return value.RadeonChillMinFPS_DC; }
            set
            {
                if (this.value.RadeonChillMinFPS_DC != value)
                {
                    this.value.RadeonChillMinFPS_DC = value;
                }
            }
        }

        public int? RadeonChillMaxFPS_DC
        {
            get { return value.RadeonChillMaxFPS_DC; }
            set
            {
                if (this.value.RadeonChillMaxFPS_DC != value)
                {
                    this.value.RadeonChillMaxFPS_DC = value;
                }
            }
        }

        public GameId GameId
        {
            get { return value.GameId; }
        }

        public bool Use
        {
            get { return value.Use; }
            set
            {
                if (this.value.Use != value)
                {
                    this.value.Use = value;
                }
            }
        }

        public bool IsGlobalProfile
        {
            get { return value.IsGlobalProfile; }
        }

        // Legion controller remapping properties
        public string LegionButtonY1
        {
            get { return value.LegionButtonY1; }
            set
            {
                if (this.value.LegionButtonY1 != value)
                {
                    this.value.LegionButtonY1 = value;
                }
            }
        }

        public string LegionButtonY2
        {
            get { return value.LegionButtonY2; }
            set
            {
                if (this.value.LegionButtonY2 != value)
                {
                    this.value.LegionButtonY2 = value;
                }
            }
        }

        public string LegionButtonY3
        {
            get { return value.LegionButtonY3; }
            set
            {
                if (this.value.LegionButtonY3 != value)
                {
                    this.value.LegionButtonY3 = value;
                }
            }
        }

        public string LegionButtonM2
        {
            get { return value.LegionButtonM2; }
            set
            {
                if (this.value.LegionButtonM2 != value)
                {
                    this.value.LegionButtonM2 = value;
                }
            }
        }

        public string LegionButtonM3
        {
            get { return value.LegionButtonM3; }
            set
            {
                if (this.value.LegionButtonM3 != value)
                {
                    this.value.LegionButtonM3 = value;
                }
            }
        }

        public string LegionButtonDesktop
        {
            get { return value.LegionButtonDesktop; }
            set
            {
                if (this.value.LegionButtonDesktop != value)
                {
                    this.value.LegionButtonDesktop = value;
                }
            }
        }

        public string LegionButtonPage
        {
            get { return value.LegionButtonPage; }
            set
            {
                if (this.value.LegionButtonPage != value)
                {
                    this.value.LegionButtonPage = value;
                }
            }
        }

        public int? LegionGyroButton
        {
            get { return value.LegionGyroButton; }
            set
            {
                if (this.value.LegionGyroButton != value)
                {
                    this.value.LegionGyroButton = value;
                }
            }
        }

        // Additional Legion controller settings

        public bool? LegionControllerProfileEnabled
        {
            get { return value.LegionControllerProfileEnabled; }
            set
            {
                if (this.value.LegionControllerProfileEnabled != value)
                {
                    this.value.LegionControllerProfileEnabled = value;
                }
            }
        }

        public string LegionButtonM1
        {
            get { return value.LegionButtonM1; }
            set
            {
                if (this.value.LegionButtonM1 != value)
                {
                    this.value.LegionButtonM1 = value;
                }
            }
        }

        public int? LegionGyroTarget
        {
            get { return value.LegionGyroTarget; }
            set
            {
                if (this.value.LegionGyroTarget != value)
                {
                    this.value.LegionGyroTarget = value;
                }
            }
        }

        public int? LegionGyroSensitivityX
        {
            get { return value.LegionGyroSensitivityX; }
            set
            {
                if (this.value.LegionGyroSensitivityX != value)
                {
                    this.value.LegionGyroSensitivityX = value;
                }
            }
        }

        public int? LegionGyroSensitivityY
        {
            get { return value.LegionGyroSensitivityY; }
            set
            {
                if (this.value.LegionGyroSensitivityY != value)
                {
                    this.value.LegionGyroSensitivityY = value;
                }
            }
        }

        public bool? LegionGyroInvertX
        {
            get { return value.LegionGyroInvertX; }
            set
            {
                if (this.value.LegionGyroInvertX != value)
                {
                    this.value.LegionGyroInvertX = value;
                }
            }
        }

        public bool? LegionGyroInvertY
        {
            get { return value.LegionGyroInvertY; }
            set
            {
                if (this.value.LegionGyroInvertY != value)
                {
                    this.value.LegionGyroInvertY = value;
                }
            }
        }

        public int? LegionGyroMappingType
        {
            get { return value.LegionGyroMappingType; }
            set
            {
                if (this.value.LegionGyroMappingType != value)
                {
                    this.value.LegionGyroMappingType = value;
                }
            }
        }

        public int? LegionGyroActivationMode
        {
            get { return value.LegionGyroActivationMode; }
            set
            {
                if (this.value.LegionGyroActivationMode != value)
                {
                    this.value.LegionGyroActivationMode = value;
                }
            }
        }

        public int? LegionGyroDeadzone
        {
            get { return value.LegionGyroDeadzone; }
            set
            {
                if (this.value.LegionGyroDeadzone != value)
                {
                    this.value.LegionGyroDeadzone = value;
                }
            }
        }

        public int? LegionLeftStickDeadzone
        {
            get { return value.LegionLeftStickDeadzone; }
            set
            {
                if (this.value.LegionLeftStickDeadzone != value)
                {
                    this.value.LegionLeftStickDeadzone = value;
                }
            }
        }

        public int? LegionRightStickDeadzone
        {
            get { return value.LegionRightStickDeadzone; }
            set
            {
                if (this.value.LegionRightStickDeadzone != value)
                {
                    this.value.LegionRightStickDeadzone = value;
                }
            }
        }

        public int? LegionLeftTriggerStart
        {
            get { return value.LegionLeftTriggerStart; }
            set
            {
                if (this.value.LegionLeftTriggerStart != value)
                {
                    this.value.LegionLeftTriggerStart = value;
                }
            }
        }

        public int? LegionLeftTriggerEnd
        {
            get { return value.LegionLeftTriggerEnd; }
            set
            {
                if (this.value.LegionLeftTriggerEnd != value)
                {
                    this.value.LegionLeftTriggerEnd = value;
                }
            }
        }

        public int? LegionRightTriggerStart
        {
            get { return value.LegionRightTriggerStart; }
            set
            {
                if (this.value.LegionRightTriggerStart != value)
                {
                    this.value.LegionRightTriggerStart = value;
                }
            }
        }

        public int? LegionRightTriggerEnd
        {
            get { return value.LegionRightTriggerEnd; }
            set
            {
                if (this.value.LegionRightTriggerEnd != value)
                {
                    this.value.LegionRightTriggerEnd = value;
                }
            }
        }

        public bool? LegionHairTriggers
        {
            get { return value.LegionHairTriggers; }
            set
            {
                if (this.value.LegionHairTriggers != value)
                {
                    this.value.LegionHairTriggers = value;
                }
            }
        }

        public int? LegionJoystickAsMouseMode
        {
            get { return value.LegionJoystickAsMouseMode; }
            set
            {
                if (this.value.LegionJoystickAsMouseMode != value)
                {
                    this.value.LegionJoystickAsMouseMode = value;
                }
            }
        }

        public int? LegionJoystickMouseSens
        {
            get { return value.LegionJoystickMouseSens; }
            set
            {
                if (this.value.LegionJoystickMouseSens != value)
                {
                    this.value.LegionJoystickMouseSens = value;
                }
            }
        }

        public string LegionGamepadMapping
        {
            get { return value.LegionGamepadMapping; }
            set
            {
                if (this.value.LegionGamepadMapping != value)
                {
                    this.value.LegionGamepadMapping = value;
                }
            }
        }

        public bool? LegionNintendoLayout
        {
            get { return value.LegionNintendoLayout; }
            set
            {
                if (this.value.LegionNintendoLayout != value)
                {
                    this.value.LegionNintendoLayout = value;
                }
            }
        }

        public int? LegionVibration
        {
            get { return value.LegionVibration; }
            set
            {
                if (this.value.LegionVibration != value)
                {
                    this.value.LegionVibration = value;
                }
            }
        }

        public int? LegionVibrationMode
        {
            get { return value.LegionVibrationMode; }
            set
            {
                if (this.value.LegionVibrationMode != value)
                {
                    this.value.LegionVibrationMode = value;
                }
            }
        }

        public int? LegionPerformanceMode
        {
            get { return value.LegionPerformanceMode; }
            set
            {
                if (this.value.LegionPerformanceMode != value)
                {
                    this.value.LegionPerformanceMode = value;
                }
            }
        }

        public int? LegionPerformanceMode_DC
        {
            get { return value.LegionPerformanceMode_DC; }
            set
            {
                if (this.value.LegionPerformanceMode_DC != value)
                {
                    this.value.LegionPerformanceMode_DC = value;
                }
            }
        }

        // Lighting properties
        public int? LegionLightMode
        {
            get { return value.LegionLightMode; }
            set
            {
                if (this.value.LegionLightMode != value)
                {
                    this.value.LegionLightMode = value;
                }
            }
        }

        public string LegionLightColor
        {
            get { return value.LegionLightColor; }
            set
            {
                if (this.value.LegionLightColor != value)
                {
                    this.value.LegionLightColor = value;
                }
            }
        }

        public int? LegionLightBrightness
        {
            get { return value.LegionLightBrightness; }
            set
            {
                if (this.value.LegionLightBrightness != value)
                {
                    this.value.LegionLightBrightness = value;
                }
            }
        }

        public int? LegionLightSpeed
        {
            get { return value.LegionLightSpeed; }
            set
            {
                if (this.value.LegionLightSpeed != value)
                {
                    this.value.LegionLightSpeed = value;
                }
            }
        }

        public bool? LegionPowerLight
        {
            get { return value.LegionPowerLight; }
            set
            {
                if (this.value.LegionPowerLight != value)
                {
                    this.value.LegionPowerLight = value;
                }
            }
        }

    }
}
