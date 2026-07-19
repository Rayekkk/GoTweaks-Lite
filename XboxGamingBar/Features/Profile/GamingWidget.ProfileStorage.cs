using Shared.Data;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // The catalog is a helper-confirmed display cache.  It replaces the old
        // LocalSettings Profile_* persistence completely.
        private void LoadProfileFromStorage(string profileName, PerformanceProfile profile)
        {
            if (!helperProfileCatalog.TryGetValue(profileName, out var confirmed))
                return;

            var copy = confirmed.Clone();
            profile.TDP = copy.TDP; profile.TDPFast = copy.TDPFast; profile.TDPPeak = copy.TDPPeak;
            profile.CPUBoost = copy.CPUBoost; profile.CPUEPP = copy.CPUEPP;
            profile.MaxCPUState = copy.MaxCPUState; profile.MinCPUState = copy.MinCPUState;
            profile.LegionPerformanceMode = copy.LegionPerformanceMode;
            profile.FPSLimitEnabled = copy.FPSLimitEnabled; profile.FPSLimitValue = copy.FPSLimitValue;
            profile.OSPowerMode = copy.OSPowerMode; profile.HDREnabled = copy.HDREnabled;
            profile.Resolution = copy.Resolution; profile.RefreshRate = copy.RefreshRate;
            profile.FluidMotionFrames = copy.FluidMotionFrames;
            profile.RadeonSuperResolution = copy.RadeonSuperResolution;
            profile.ImageSharpening = copy.ImageSharpening;
            profile.RadeonAntiLag = copy.RadeonAntiLag;
            profile.RadeonBoost = copy.RadeonBoost; profile.RadeonChill = copy.RadeonChill;
        }
    }
}
