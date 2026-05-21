using System;
using System.Threading;
using NLog;
using XboxGamingBarHelper.Devices.Libraries.Legion;
using XboxGamingBarHelper.Labs;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>Which physical controller source drives the VIIPER forwarder.</summary>
    internal enum ViiperInputSourceKind
    {
        XInput = 0,
        LegionHid = 1,
    }

    /// <summary>How a Guide/Mode press is handled by the forwarder.</summary>
    internal enum ViiperGuideButtonMode
    {
        /// <summary>Forward to the emulated device's native Guide/PS button.</summary>
        Native = 0,
        /// <summary>Suppress the native Guide press and fire Win+G to open Xbox Game Bar.</summary>
        GameBar = 1,
    }

    /// <summary>Which IMU source (if any) feeds the gyro bytes of the VIIPER wire format.</summary>
    internal enum ViiperGyroSourceKind
    {
        None = 0,
        Left = 1,
        Right = 2,
        // Built-in handheld IMU. Currently no separate Windows sensor wiring,
        // so we treat Handheld the same as Mixed (left+right merged) which is
        // the closest analogue on Legion Go / Go 2 / Go S.
        Handheld = 3,
        // Both controllers averaged with the right side mirror-inverted so axes
        // agree before the merge. Falls back to whichever single side is
        // currently reporting if only one is available.
        Mixed = 4,
    }

    /// <summary>
    /// Bitmasks for Legion Go auxiliary buttons (Y1/Y2/Y3, M3, Mode, Share, front-top/bot).
    /// Matches LegionButtonMonitor's AuxButtons output.
    /// </summary>
    internal static class LegionAux
    {
        public const ushort Y1     = 0x0001;
        public const ushort Y2     = 0x0002;
        public const ushort Y3     = 0x0004;
        public const ushort M3     = 0x0008;
        public const ushort M1     = 0x0010;
        public const ushort M2     = 0x0020;
        public const ushort Mode   = 0x0040;
        public const ushort Share  = 0x0080;
        public const ushort FrTop  = 0x0100;
        public const ushort FrBot  = 0x0200;
    }

    /// <summary>
    /// Phase 5a: minimal XInput -> VIIPER forwarding loop.
    /// Polls XInput for a single physical controller and forwards the state to a
    /// VIIPER virtual device at ~250 Hz. Currently supports Xbox 360, DualShock 4,
    /// DualSense Edge, and Xbox Elite 2 target types. Gyro, Legion HID input,
    /// button remap, and rumble feedback come in 5b/5c/5d.
    /// </summary>
    internal sealed class ViiperInputForwarder : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ViiperService service;
        private readonly LegionManager legionManager;
        private Thread pollThread;
        private volatile bool running;
        private volatile bool paused;

        private uint physicalIndex;
        private uint busId;
        private uint deviceId;
        // Monotonic frame counter written into the steamdeck wire format's frame
        // field (bytes 4-7). libviiper's steamdeck device tracks it for sequence/
        // deduplication purposes — we just need it to advance per call.
        private uint steamDeckFrameCounter;

        // Monotonic frame counter for the steamcontroller (Gordon V1) wire format.
        // Same role as steamDeckFrameCounter — Gordon uses its own per-device
        // sequence number at bytes 4-7 of the 64-byte report.
        private uint gordonFrameCounter;

        // First-sighting set of steamdeck OutputState command IDs (data[0]). Used
        // to log new command bytes once each so "rumble doesn't work" reports can
        // be triaged: if no 0x8F / 0xEA / 0xEB shows up here, Steam Input is not
        // sending rumble to the virtual device at all.
        private readonly System.Collections.Generic.HashSet<byte> seenSteamDeckCommandIds = new System.Collections.Generic.HashSet<byte>();

        // Steam sends 0x8F TriggerHapticPulse separately per motor side (Left or
        // Right), and the XInput SetState we pass to the physical pad requires
        // BOTH motor strengths in every call. Track the most-recent per-side
        // strength so a "right motor only" pulse doesn't unintentionally silence
        // the left motor (and vice-versa).
        private byte steamDeckMotorLeft;
        private byte steamDeckMotorRight;
        // Per-side expiration ticks (DateTime.UtcNow.Ticks). Steam Deck haptic
        // pulses are FINITE — each 0x8F plays for Count * (Duration + Interval)
        // microseconds and then ends naturally on the real Deck. To create
        // continuous rumble Steam re-sends pulse trains; to stop rumble Steam
        // simply stops sending them, relying on the train's natural end.
        // XInput motors don't auto-stop, so without an explicit SetState(0,0)
        // the physical controller would buzz forever at the last commanded
        // strength. The poll loop checks these expirations every tick and
        // forces a zero SetState when they pass. Value 0 = no pending pulse.
        private long steamDeckMotorLeftExpiresTicks;
        private long steamDeckMotorRightExpiresTicks;

        // Throttle LED writes — Legion's WMI/USB write path is slow; don't re-send the same color.
        private long lastLedPacked = -1;
        private long lastLedWriteTicks;
        private const long LedWriteMinIntervalTicks = TimeSpan.TicksPerSecond / 8; // 125 ms
        private string targetType = "xbox360";
        private volatile ViiperInputSourceKind inputSource = ViiperInputSourceKind.XInput;
        private volatile ViiperGyroSourceKind gyroSource = ViiperGyroSourceKind.None;
        private volatile ViiperGuideButtonMode guideMode = ViiperGuideButtonMode.Native;
        private volatile bool swapRumbleMotors;
        // Percent ×10 so we can apply it with integer math (1000 == unity). Range 0..2000.
        private volatile int rumbleIntensityScaled = 1000;
        // When false, suppress mirroring the emulated lightbar to the Legion stick lights
        // so the user's saved color stays untouched. Default false — the toggle has to be
        // explicitly enabled. Prior behavior (always-on) caused users to lose their picker
        // color the moment VIIPER asserted the DS Edge idle blue.
        private volatile bool mirrorLightbar;

        // IMU axis remap: each output axis reads source[src] × sign. Packed (src, sign) per
        // axis. Identity: X→(0,+1), Y→(1,+1), Z→(2,+1). Accel uses the same map as gyro
        // (the reference app keeps them locked together; only the 3 gyro selectors are
        // exposed in the UI).
        private volatile int axisMapXSrc = 0, axisMapXSign = 1;
        private volatile int axisMapYSrc = 1, axisMapYSign = 1;
        private volatile int axisMapZSrc = 2, axisMapZSign = 1;

        // Synthesizes a right-stick override from gyro for target types whose wire
        // format has no native motion field (xbox360, xboxelite2, switchpro family).
        // For DS4 / DSE the wire format already carries IMU bytes via TryBuildImuCounts,
        // so this processor stays dormant on those targets.
        private readonly ViiperStickGyroProcessor stickGyro = new ViiperStickGyroProcessor();

        // Edge-detection state for the Guide/Mode -> Win+G shortcut. We only fire the
        // shortcut on press-transition, not on every poll while the button is held.
        private bool guideWasPressed;
        private long lastGuideShortcutTicks;
        private const long GuideShortcutMinIntervalTicks = TimeSpan.TicksPerSecond;

        // When a Labs action (e.g. Legion L -> XboxGuide) intercepts a press that in Native
        // mode should become the emulated device's Guide/PS button, we track the press
        // state explicitly. The wire builders OR Guide into the buttons while the physical
        // button is held. A short minimum-hold covers press/release callbacks that don't
        // fire in strict order (e.g. ultra-short taps).
        private bool labsGuideHeld;
        private long labsGuideMinReleaseTicks;
        private const int LabsGuideMinHoldMilliseconds = 60;

        // Singleton so Labs-side code can reach the active forwarder without circular DI.
        private static ViiperInputForwarder activeInstance;
        internal static ViiperInputForwarder ActiveInstance => activeInstance;
        internal ViiperStickGyroProcessor StickGyroProcessor => stickGyro;

        // Rumble feedback counters. Incremented on the libviiper callback thread inside
        // OnFeedbackReceived; drained and logged from PollLoop's 5s stats window. Used
        // to triage "rumble silent" reports — distinguishes (a) game never sent rumble
        // events, (b) events arrived but ViiperXInput.SetState rejected them, (c) events
        // forwarded fine but the user can't feel them (then look at where physicalIndex
        // resolved to in DetectPhysicalXInputIndex's log line).
        private int statsRumbleEventsReceived;
        private int statsRumbleForwardedOk;
        private int statsRumbleForwardedErr;

        // Latest aux buttons sampled from Legion HID this cycle (0 when the active source
        // doesn't expose them, e.g. plain XInput). Read by the DS4/DSE wire builders to map
        // Legion Y1/Y2/Y3/M3/Mode/Share onto the virtual device's extended button bits.
        private ushort currentAuxButtons;

        // Latest touchpad sample from Legion HID, written into the DS4/DSE wire format.
        private bool currentTouchActive;
        private bool currentTouchPressed;
        private ushort currentTouchX;
        private ushort currentTouchY;

        // IMU axis counts/second and counts/G for Legion Go BMI323:
        //   gyro: 16 counts per deg/sec
        //   accel: 4096 counts per G (BMI323 ±8g)
        // DS4 host apps expect 8192 counts/g on accel → multiply by 2.
        private const float GyroDpsToRawCounts = 16.0f;
        private const float AccelGToRawCounts = 4096.0f;

        // Rumble delivery — uses XInputSetState via xinput1_4.dll, which on
        // the Legion ends up at xusb22.sys (bound to MI_00). The helper is on
        // HidHide's per-application allowlist, so even with MI_00 in the
        // suppression list xinput1_4's internal opens succeed for us. Mode 1
        // keeps the XInput child enumerable; mode 3 broke this path (rumble
        // would route via the now-hidden 045E:028E sibling and fail). The
        // duplicate "Controller (Xbox 360 for Windows)" entry users see in
        // joy.cpl is the cost of keeping the rumble channel open.

        public ViiperInputForwarder(ViiperService inService, LegionManager inLegionManager)
        {
            service = inService;
            legionManager = inLegionManager;
            if (service != null)
            {
                service.FeedbackReceived += OnFeedbackReceived;
            }
        }

        /// <summary>
        /// Called by the native thread when the virtual device receives a rumble/LED report
        /// from the consuming application. We parse the relevant motor bytes based on the
        /// current device type and forward them to the physical XInput controller.
        /// </summary>
        /// <summary>
        /// Zero out per-side steamdeck motor strengths whose pulse-train timer has
        /// elapsed. Returns true if any side just decayed from non-zero to zero,
        /// which the caller can use to know it needs to flush a SetState(0,0) to
        /// the physical pad (XInput motors don't auto-stop).
        /// </summary>
        private bool DecayExpiredSteamDeckRumble()
        {
            long now = DateTime.UtcNow.Ticks;
            bool changed = false;
            if (steamDeckMotorLeft != 0 && now >= steamDeckMotorLeftExpiresTicks)
            {
                steamDeckMotorLeft = 0;
                steamDeckMotorLeftExpiresTicks = 0;
                changed = true;
            }
            if (steamDeckMotorRight != 0 && now >= steamDeckMotorRightExpiresTicks)
            {
                steamDeckMotorRight = 0;
                steamDeckMotorRightExpiresTicks = 0;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Pushes the current per-side steamdeck motor strengths to the physical
        /// XInput controller. Mirrors the rumble-emit code path in
        /// OnFeedbackReceived so the poll-loop decay timer can stop a rumble
        /// without piggy-backing on an incoming feedback event.
        /// </summary>
        private void FlushSteamDeckRumbleToXInput()
        {
            try
            {
                byte large = steamDeckMotorLeft;
                byte small = steamDeckMotorRight;
                if (swapRumbleMotors)
                {
                    byte tmp = large; large = small; small = tmp;
                }
                int scaled = rumbleIntensityScaled;
                int leftSpeed = (large * 257 * scaled) / 1000;
                int rightSpeed = (small * 257 * scaled) / 1000;
                if (leftSpeed > 65535) leftSpeed = 65535;
                if (rightSpeed > 65535) rightSpeed = 65535;
                var vib = new ViiperXInputVibration
                {
                    LeftMotorSpeed = (ushort)leftSpeed,
                    RightMotorSpeed = (ushort)rightSpeed,
                };
                ViiperXInput.SetState(physicalIndex, ref vib);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Steamdeck rumble decay XInput SetState failed: {ex.Message}");
            }
        }

        private bool IsSteamDeckLikeTarget()
        {
            switch (targetType)
            {
                case "steamdeck":
                case "steam-deck":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                case "steam-controller-v1":
                case "steamcontroller-v1":
                case "gordon":
                case "legion-go":
                case "legion-go-s":
                case "legion-go-2":
                case "msi-claw":
                case "rog-ally":
                case "zotac-zone":
                    return true;
                default:
                    return false;
            }
        }

        private void LogSteamDeckCommandIdOnce(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            byte id = data[0];
            bool isNew;
            lock (seenSteamDeckCommandIds)
            {
                isNew = seenSteamDeckCommandIds.Add(id);
            }
            if (!isNew) return;
            string name;
            switch (id)
            {
                case 0x80: name = "SetDigitalMappings"; break;
                case 0x81: name = "ClearDigitalMappings"; break;
                case 0x83: name = "GetAttributesValues"; break;
                case 0x87: name = "SetSettingsValues"; break;
                case 0x88: name = "ClearSettingsValues"; break;
                case 0x8D: name = "SetControllerMode"; break;
                case 0x8F: name = "TriggerHapticPulse"; break;
                case 0xA1: name = "GetDeviceInfo"; break;
                case 0xCE: name = "ResetIMU"; break;
                case 0xEA: name = "TriggerHapticCommand (LRA rumble)"; break;
                case 0xEB: name = "TriggerRumbleCommand (motor rumble)"; break;
                default:   name = "(unknown)"; break;
            }
            Logger.Info($"VIIPER steamdeck OutputState first sighting: cmd=0x{id:X2} ({name}) len={data.Length}");
        }

        private void OnFeedbackReceived(uint cbBusId, uint cbDeviceId, byte[] data)
        {
            if (!running || data == null || data.Length == 0) return;
            // Ignore late events from a hot-swapped-out device.
            if (cbBusId != busId || cbDeviceId != deviceId) return;
            System.Threading.Interlocked.Increment(ref statsRumbleEventsReceived);

            byte rumbleLarge = 0;
            byte rumbleSmall = 0;
            bool haveLed = false;
            byte ledR = 0, ledG = 0, ledB = 0;
            switch (targetType)
            {
                case "xbox360":
                    if (data.Length >= 2) { rumbleLarge = data[0]; rumbleSmall = data[1]; }
                    break;
                case "dualshock4":
                    // DS4 report: data[0]=rumbleSmall, data[1]=rumbleLarge, data[2..4]=LED RGB,
                    // data[5]=flashOn, data[6]=flashOff.
                    if (data.Length >= 2) { rumbleSmall = data[0]; rumbleLarge = data[1]; }
                    if (data.Length >= 5) { haveLed = true; ledR = data[2]; ledG = data[3]; ledB = data[4]; }
                    break;
                case "dualsenseedge":
                case "dualsense-edge":
                case "dualsense":
                case "ds5":
                    // DSE/DS5 report: data[0]=rumbleSmall, data[1]=rumbleLarge, data[2..4]=LED RGB,
                    // data[5]=playerLeds (DSE only — non-Edge stops at 5 bytes).
                    if (data.Length >= 2) { rumbleSmall = data[0]; rumbleLarge = data[1]; }
                    if (data.Length >= 5) { haveLed = true; ledR = data[2]; ledG = data[3]; ledB = data[4]; }
                    break;
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                    if (data.Length >= 2) { rumbleLarge = data[0]; rumbleSmall = data[1]; }
                    break;
                // libviiper 32c8ed3 + clib rework — steam-handheld targets now use
                // the steamdeck OutputState container (64-byte command framing).
                // Multiple command types are multiplexed on data[0]. In the wild
                // Steam Input emits ONLY 0x8F TriggerHapticPulse for game rumble
                // (verified from helper_2026-05-17_20.log first-sighting trace);
                // 0xEA/0xEB are spec'd but unused on current Steam Deck virtual
                // device. The 0x8F handler is primary, 0xEA/0xEB kept for safety.
                //
                // 0x8F TriggerHapticPulse layout (per device/steamdeck/inputstate.go
                // AsHapticPulse):
                //   [0] = 0x8F
                //   [2] = Side (0=Left, 1=Right, 2=Both)
                //   [3..5] = Duration µs (u16 LE)   — pulse ON time
                //   [5..7] = Interval µs (u16 LE)   — pulse OFF time
                //   [7..9] = Count (u16 LE)         — number of pulses (or 0xFFFF/0 = continuous)
                //   [9]    = Gain (int8 dB)         — amplitude (0 = full)
                //
                // Steam Input emits one 0x8F per motor side independently, so we
                // must remember the OTHER motor's most-recent strength across
                // commands. Otherwise a "right motor only" pulse would arrive
                // with the left motor implicitly silenced at the bottom of this
                // method (XInput SetState needs BOTH motor speeds).
                //
                // Rumble strength heuristic: duty cycle of the pulse train
                // (Duration / (Duration + Interval)) scaled to 0..255. Steam's
                // typical continuous-rumble emission has Duration ≈ 600µs and
                // Interval ≈ 400µs (duty ~60%) at full strength, with Count set
                // to a large value. A "stop" command has Count == 0 OR Duration
                // == 0. Gain in dB scales further: each -6 dB ≈ halve strength.
                case "steamdeck":
                case "steam-deck":
                case "steamdeck-generic":
                case "steam-generic":
                case "steam-controller":
                case "steam-controller-v1":
                case "steamcontroller-v1":
                case "gordon":
                case "legion-go":
                case "legion-go-s":
                case "legion-go-2":
                case "msi-claw":
                case "rog-ally":
                case "zotac-zone":
                    LogSteamDeckCommandIdOnce(data);
                    if (data.Length >= 10 && data[0] == 0x8F)
                    {
                        byte side = data[2];
                        ushort duration = (ushort)(data[3] | (data[4] << 8));
                        ushort interval = (ushort)(data[5] | (data[6] << 8));
                        ushort count    = (ushort)(data[7] | (data[8] << 8));
                        sbyte gainDb    = (sbyte)data[9];

                        byte strength;
                        if (count == 0 || duration == 0)
                        {
                            strength = 0;
                        }
                        else
                        {
                            int total = duration + interval;
                            int dutyScaled = (total > 0) ? (duration * 255) / total : 0;
                            // Gain attenuation — each -6 dB drop halves amplitude.
                            // Positive gain (rare from Steam) treated as 0 dB cap.
                            if (gainDb < 0)
                            {
                                int dropSteps = (-gainDb + 3) / 6;
                                if (dropSteps > 8) dropSteps = 8;
                                dutyScaled >>= dropSteps;
                            }
                            if (dutyScaled < 0) dutyScaled = 0;
                            if (dutyScaled > 255) dutyScaled = 255;
                            strength = (byte)dutyScaled;
                        }

                        // Compute auto-stop time. Pulse train plays for
                        // Count * (Duration + Interval) microseconds; convert
                        // to .NET ticks (1µs = 10 ticks). PollLoop zeros the
                        // motor + sends XInput SetState(0,0) once we pass it.
                        long totalPlayUs = (long)count * ((long)duration + interval);
                        long stopTicks = DateTime.UtcNow.Ticks + totalPlayUs * 10;
                        if (side == 0)      { steamDeckMotorLeft  = strength; steamDeckMotorLeftExpiresTicks  = stopTicks; }
                        else if (side == 1) { steamDeckMotorRight = strength; steamDeckMotorRightExpiresTicks = stopTicks; }
                        else                { steamDeckMotorLeft  = strength; steamDeckMotorLeftExpiresTicks  = stopTicks;
                                              steamDeckMotorRight = strength; steamDeckMotorRightExpiresTicks = stopTicks; }
                    }
                    else if (data.Length >= 9 && data[0] == 0xEB)
                    {
                        // FeatureTriggerRumbleCommand — direct motor speeds.
                        // No natural end; keep the strength latched until
                        // Steam sends another 0xEB to update it.
                        ushort steamLeft  = (ushort)(data[5] | (data[6] << 8));
                        ushort steamRight = (ushort)(data[7] | (data[8] << 8));
                        steamDeckMotorLeft  = (byte)(steamLeft  / 257);
                        steamDeckMotorRight = (byte)(steamRight / 257);
                        steamDeckMotorLeftExpiresTicks  = long.MaxValue;
                        steamDeckMotorRightExpiresTicks = long.MaxValue;
                    }
                    else if (data.Length >= 5 && data[0] == 0xEA)
                    {
                        // FeatureTriggerHapticCommand — newer command class,
                        // intensity ladder 0..4. Kept as a fallback channel.
                        // Treat as latched (no natural end) like 0xEB.
                        byte side = data[2];
                        byte hapticType = data[3];
                        byte intensity = data[4];
                        byte strength = (hapticType == 0) ? (byte)0 :
                            (intensity >= 4) ? (byte)255 : (byte)(intensity * 64);
                        if (side == 0)      { steamDeckMotorLeft  = strength; steamDeckMotorLeftExpiresTicks  = long.MaxValue; }
                        else if (side == 1) { steamDeckMotorRight = strength; steamDeckMotorRightExpiresTicks = long.MaxValue; }
                        else                { steamDeckMotorLeft  = strength; steamDeckMotorLeftExpiresTicks  = long.MaxValue;
                                              steamDeckMotorRight = strength; steamDeckMotorRightExpiresTicks = long.MaxValue; }
                    }
                    // Decay any per-side strength whose pulse train has elapsed
                    // since the last command arrived — Steam often coalesces a
                    // burst of pulses then goes silent expecting natural decay.
                    DecayExpiredSteamDeckRumble();
                    // Always populate rumbleLarge/Small from persistent per-side
                    // state — even on a command we don't recognize, the existing
                    // motor state must reach the XInput SetState below or the
                    // physical pad will receive (0, 0) and stop vibrating.
                    rumbleLarge = steamDeckMotorLeft;
                    rumbleSmall = steamDeckMotorRight;
                    break;
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                case "joycon-pair":
                    if (data.Length >= 2) { rumbleLarge = data[0]; rumbleSmall = data[1]; }
                    break;
                default:
                    return;
            }

            // Always forward rumble to the physical XInput controller. The user's pad is
            // almost always XInput-visible (Legion Go controllers expose XInput alongside
            // their native HID), so rumble should reach the hardware regardless of which
            // input source we're READING gamepad state from. Previously this was gated on
            // inputSource == XInput, which meant Legion-HID users got no rumble at all.
            try
            {
                // Apply user-configurable swap and intensity multiplier. Swap first so
                // intensity scales whichever motor ends up on each side.
                byte large = rumbleLarge;
                byte small = rumbleSmall;
                if (swapRumbleMotors)
                {
                    byte tmp = large; large = small; small = tmp;
                }
                int scaled = rumbleIntensityScaled; // snapshot to avoid volatile read race
                int leftSpeed = (large * 257 * scaled) / 1000;
                int rightSpeed = (small * 257 * scaled) / 1000;
                if (leftSpeed > 65535) leftSpeed = 65535;
                if (rightSpeed > 65535) rightSpeed = 65535;
                var vib = new ViiperXInputVibration
                {
                    LeftMotorSpeed = (ushort)leftSpeed,
                    RightMotorSpeed = (ushort)rightSpeed,
                };
                uint rc = ViiperXInput.SetState(physicalIndex, ref vib);
                if (rc == ViiperXInput.ErrorSuccess)
                {
                    System.Threading.Interlocked.Increment(ref statsRumbleForwardedOk);
                }
                else
                {
                    System.Threading.Interlocked.Increment(ref statsRumbleForwardedErr);
                }
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.Increment(ref statsRumbleForwardedErr);
                Logger.Debug($"XInput SetState rumble failed: {ex.Message}");
            }

            // LED color forwarding — the emulated device's RGB lightbar is pushed to the
            // Legion Go stick lights. Throttle: skip when unchanged and rate-limit writes.
            //
            // Suppress the initial (0,0,0) state: most games default the emulated DS/DS Edge
            // lightbar to black until they explicitly assert a color, and forwarding (0,0,0)
            // at startup overwrites the user's saved Legion Go stick color (we observed this
            // at 16:40:37 in the helper logs — VIIPER started, immediately wrote
            // SetLightColor("#000000") with no game asserting anything). Once the game
            // sends ANY non-zero color, start forwarding everything — including subsequent
            // explicit (0,0,0) "off" requests.
            if (haveLed && legionManager != null && mirrorLightbar)
            {
                long packed = ((long)ledR << 16) | ((long)ledG << 8) | ledB;
                long now = DateTime.UtcNow.Ticks;
                if (packed != lastLedPacked && (now - lastLedWriteTicks) >= LedWriteMinIntervalTicks)
                {
                    if (lastLedPacked < 0 && packed == 0)
                    {
                        // First observation is black — treat as "no game assertion yet" and
                        // record without forwarding so the user's stick color stays put.
                        lastLedPacked = 0;
                    }
                    else
                    {
                        lastLedPacked = packed;
                        lastLedWriteTicks = now;
                        try
                        {
                            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", ledR, ledG, ledB);
                            legionManager.SetLightColor(hex);
                        }
                        catch (Exception ex) { Logger.Debug($"Legion SetLightColor failed: {ex.Message}"); }
                    }
                }
            }
        }

        /// <summary>Discover which XInput index (0-3) has a connected physical controller.</summary>
        public static uint DetectPhysicalXInputIndex()
        {
            // Log every slot's state, not just the first hit. When a user reports
            // "no input after toggle" or "rumble silent" we need to know whether
            // (a) the helper's XInput sees the physical Legion at all (HidHide
            //     allowlist working), and
            // (b) the picked slot is the physical or VIIPER's virtual device.
            // Without this, debug requires an OS-level XInput probe outside the app.
            var state = new ViiperXInputState();
            uint pick = 0;
            bool picked = false;
            var connected = new System.Collections.Generic.List<uint>();
            for (uint i = 0; i < 4; i++)
            {
                if (ViiperXInput.GetState(i, ref state) == ViiperXInput.ErrorSuccess)
                {
                    connected.Add(i);
                    if (!picked) { pick = i; picked = true; }
                }
            }
            string slots = connected.Count == 0 ? "(none)" : string.Join(",", connected);
            Logger.Info($"DetectPhysicalXInputIndex: connectedSlots=[{slots}], picked={(picked ? pick.ToString() : "0 (default — none connected)")}");
            return picked ? pick : 0u;
        }

        public void Start(uint inPhysicalIndex, uint inBusId, uint inDeviceId, string inTargetType)
        {
            if (running) return;

            physicalIndex = inPhysicalIndex;
            busId = inBusId;
            deviceId = inDeviceId;
            targetType = string.IsNullOrEmpty(inTargetType) ? "xbox360" : inTargetType;

            // Clear any stale filter / toggle state from a previous Start cycle.
            // Without this, a backend swap (e.g. xbox360 → DS4 → xbox360 again)
            // would carry over an old filter value and produce a sudden jump on
            // the first poll under the new device.
            stickGyro.Reset();

            activeInstance = this;
            running = true;
            pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "ViiperInputForwarder",
                Priority = ThreadPriority.AboveNormal,
            };
            pollThread.Start();
            Logger.Info($"VIIPER forwarder started (xinput={physicalIndex}, bus={busId}, dev={deviceId}, type={targetType})");
        }

        public void UpdateTarget(uint newBusId, uint newDeviceId, string newTypeName)
        {
            busId = newBusId;
            deviceId = newDeviceId;
            targetType = string.IsNullOrEmpty(newTypeName) ? "xbox360" : newTypeName;
            Logger.Info($"VIIPER forwarder target updated: bus={busId}, dev={deviceId}, type={targetType}");
        }

        /// <summary>
        /// Updates the XInput user index the forwarder reads from and writes rumble to.
        /// Used post-Start by ViiperEmulationManager to re-pin to the physical Legion's
        /// slot once Labs/LegionButtonMonitor has disposed its dedicated Guide-only
        /// ViGEm pad — that disposal can only happen after Start sets running=true
        /// (so CanHandleExternalGuide flips true), so the index detected pre-Start
        /// often points at the about-to-be-disposed Labs pad and goes ERROR_DEVICE_NOT_
        /// CONNECTED a few hundred ms later.
        /// </summary>
        public void UpdatePhysicalIndex(uint newPhysicalIndex)
        {
            uint old = physicalIndex;
            physicalIndex = newPhysicalIndex;
            Logger.Info($"VIIPER forwarder physicalIndex updated: {old} -> {newPhysicalIndex}");
        }

        /// <summary>
        /// Gates the poll loop from pushing input while a hot-swap is in flight. Between
        /// RemoveDevice() and AddDevice() (which can take ~2 seconds on the USBIP side)
        /// the virtual device doesn't exist, so continuing to send packets floods the
        /// log with "invalid input size" warnings and wastes CPU. Also kicks in when the
        /// new device's wire format differs from the old one so we never deliver a
        /// wrong-format packet to the new device before UpdateTarget switches builders.
        /// </summary>
        public void SetPaused(bool paused)
        {
            if (this.paused == paused) return;
            this.paused = paused;
            Logger.Info($"VIIPER forwarder paused -> {paused}");
        }

        public void SetInputSource(ViiperInputSourceKind kind)
        {
            if (inputSource == kind) return;
            inputSource = kind;
            Logger.Info($"VIIPER forwarder input source -> {kind}");
        }

        public void SetGyroSource(ViiperGyroSourceKind kind)
        {
            if (gyroSource == kind) return;
            gyroSource = kind;
            Logger.Info($"VIIPER forwarder gyro source -> {kind}");
        }

        public void SetGuideButtonMode(ViiperGuideButtonMode mode)
        {
            if (guideMode == mode) return;
            guideMode = mode;
            Logger.Info($"VIIPER forwarder guide-button mode -> {mode}");
        }

        public void SetSwapRumbleMotors(bool swap)
        {
            if (swapRumbleMotors == swap) return;
            swapRumbleMotors = swap;
            Logger.Info($"VIIPER forwarder swap-rumble-motors -> {swap}");
        }

        /// <summary>Enable/disable mirroring the emulated DS4/DSEdge lightbar onto the Legion stick lights.</summary>
        public void SetMirrorLightbarToStick(bool enabled)
        {
            if (mirrorLightbar == enabled) return;
            mirrorLightbar = enabled;
            // Forget the last forwarded color so the next observation re-evaluates from
            // scratch under the new policy (avoids stale dedup state if the user toggles
            // mid-game).
            lastLedPacked = -1;
            Logger.Info($"VIIPER forwarder mirror-lightbar-to-stick -> {enabled}");
        }

        /// <summary>Sets the rumble intensity multiplier (percent, 0..200).</summary>
        public void SetRumbleIntensity(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 200) percent = 200;
            int scaled = percent * 10; // Keep one decimal of resolution in integer math.
            if (rumbleIntensityScaled == scaled) return;
            rumbleIntensityScaled = scaled;
            Logger.Info($"VIIPER forwarder rumble-intensity -> {percent}%");
        }

        /// <summary>Sets the IMU axis remap. Each arg is "X", "Y", "Z", "-X", "-Y", or "-Z".</summary>
        public void SetGyroAxisMapping(string mapX, string mapY, string mapZ)
        {
            int sx, sgnX, sy, sgnY, sz, sgnZ;
            ParseAxisMap(mapX, out sx, out sgnX);
            ParseAxisMap(mapY, out sy, out sgnY);
            ParseAxisMap(mapZ, out sz, out sgnZ);
            axisMapXSrc = sx; axisMapXSign = sgnX;
            axisMapYSrc = sy; axisMapYSign = sgnY;
            axisMapZSrc = sz; axisMapZSign = sgnZ;
            Logger.Info($"VIIPER forwarder gyro-axis-map -> X={mapX}, Y={mapY}, Z={mapZ}");
        }

        private static void ParseAxisMap(string value, out int src, out int sign)
        {
            switch (value)
            {
                case "X":  src = 0; sign =  1; return;
                case "Y":  src = 1; sign =  1; return;
                case "Z":  src = 2; sign =  1; return;
                case "-X": src = 0; sign = -1; return;
                case "-Y": src = 1; sign = -1; return;
                case "-Z": src = 2; sign = -1; return;
                default:   src = 0; sign =  1; return;
            }
        }

        /// <summary>
        /// Called from the Labs Legion-button action path when a user-mapped "Xbox Guide"
        /// press or release fires on a physical Legion button. Routes the event through
        /// the user's current VIIPER Guide-button mode:
        ///   • Native  → holds the emulated device's Guide/PS button while physically held
        ///   • GameBar → fires a single Win+G on press-edge (release is a no-op)
        /// Returns true if VIIPER consumed the event so the caller skips its legacy path.
        /// </summary>
        /// <summary>
        /// True when the VIIPER forwarder is up and will consume a Labs Guide press
        /// (either by routing it to the emulated device's Guide/PS button in Native mode
        /// or by firing Win+G in GameBar mode). LegionButtonMonitor uses this to decide
        /// whether a dedicated Guide-only ViGEm controller is still needed.
        /// </summary>
        public static bool CanHandleExternalGuide()
        {
            var instance = activeInstance;
            return instance != null && instance.running;
        }

        public static bool TryHandleGuideButtonFromLabs(bool pressed)
        {
            var instance = activeInstance;
            if (instance == null || !instance.running) return false;

            if (instance.guideMode == ViiperGuideButtonMode.Native)
            {
                if (pressed)
                {
                    instance.labsGuideHeld = true;
                    instance.labsGuideMinReleaseTicks = DateTime.UtcNow.Ticks
                        + TimeSpan.FromMilliseconds(LabsGuideMinHoldMilliseconds).Ticks;
                    Logger.Info("VIIPER guide-press handled from Labs (Native, hold while pressed)");
                }
                else
                {
                    instance.labsGuideHeld = false;
                    Logger.Info("VIIPER guide-release handled from Labs (Native)");
                }
                return true;
            }

            if (instance.guideMode == ViiperGuideButtonMode.GameBar)
            {
                if (!pressed) return true; // consume release, nothing to do
                long now = DateTime.UtcNow.Ticks;
                if ((now - instance.lastGuideShortcutTicks) >= GuideShortcutMinIntervalTicks)
                {
                    instance.lastGuideShortcutTicks = now;
                    try
                    {
                        Logger.Info("VIIPER guide-press handled from Labs (GameBar, firing Win+G)");
                        Program.SendKeyboardShortcut("Win+G");
                    }
                    catch (Exception ex) { Logger.Warn($"Labs guide Win+G failed: {ex.Message}"); }
                }
                return true;
            }

            return false;
        }

        /// <summary>Legacy single-shot press entry point — kept for callers that only
        /// fire on press (e.g. scroll actions). Emits a synthetic short hold.</summary>
        public static bool TryHandleGuidePressFromLabs()
        {
            bool handled = TryHandleGuideButtonFromLabs(true);
            if (handled)
            {
                // Auto-release after the minimum hold window on a background thread so
                // scroll-style one-shot actions still produce a clean press+release.
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(LabsGuideMinHoldMilliseconds);
                    TryHandleGuideButtonFromLabs(false);
                });
            }
            return handled;
        }

        private bool IsGuideHoldActive()
            => labsGuideHeld || DateTime.UtcNow.Ticks < labsGuideMinReleaseTicks;

        /// <summary>
        /// Translates LegionButtonMonitor.AuxButtons (its own bitmap layout) into the
        /// reference VIIPER LegionAux layout used by all wire builders in this file.
        /// Without this translation, every paddle, Mode, and Share bit would alias to
        /// the wrong LegionAux value and the DSE/DS4/Elite2/Switch/etc. paddle mappings
        /// would be completely scrambled.
        /// </summary>
        private static ushort TranslateMonitorAuxToLegionAux(ushort monitorAux)
        {
            // LegionButtonMonitor bitmap (from LegionButtonMonitor.cs:68-75):
            //   MODE=0x0001, SHARE=0x0002, EXTRA_L1=0x0004 (back L upper = Y1),
            //   EXTRA_L2=0x0008 (back L lower = Y2), EXTRA_R1=0x0010 (back R upper = Y3),
            //   EXTRA_RM1=0x0020 (M1 side grip L), EXTRA_R2=0x0040 (back R lower = M3),
            //   EXTRA_R3=0x0080 (M2 side grip R).
            const ushort MON_MODE   = 0x0001;
            const ushort MON_SHARE  = 0x0002;
            const ushort MON_Y1     = 0x0004;
            const ushort MON_Y2     = 0x0008;
            const ushort MON_Y3     = 0x0010;
            const ushort MON_M1     = 0x0020;
            const ushort MON_M3     = 0x0040;
            const ushort MON_M2     = 0x0080;

            ushort result = 0;
            if ((monitorAux & MON_Y1)    != 0) result |= LegionAux.Y1;
            if ((monitorAux & MON_Y2)    != 0) result |= LegionAux.Y2;
            if ((monitorAux & MON_Y3)    != 0) result |= LegionAux.Y3;
            if ((monitorAux & MON_M3)    != 0) result |= LegionAux.M3;
            if ((monitorAux & MON_M1)    != 0) result |= LegionAux.M1;
            if ((monitorAux & MON_M2)    != 0) result |= LegionAux.M2;
            if ((monitorAux & MON_MODE)  != 0) result |= LegionAux.Mode;
            if ((monitorAux & MON_SHARE) != 0) result |= LegionAux.Share;
            return result;
        }

        /// <summary>
        /// Fetches the current gyro/accel sample from the selected source, converted to DS4
        /// wire-format int16 counts. Returns false when no source is selected, the source
        /// has no fresh data, or the source is "Handheld" (not wired yet).
        /// </summary>
        private bool TryBuildImuCounts(out short gyroXRaw, out short gyroYRaw, out short gyroZRaw,
                                        out short accelXRaw, out short accelYRaw, out short accelZRaw)
        {
            gyroXRaw = gyroYRaw = gyroZRaw = 0;
            accelXRaw = accelYRaw = accelZRaw = 0;

            var src = gyroSource;
            if (src == ViiperGyroSourceKind.None)
            {
                return false;
            }

            float gXdps, gYdps, gZdps, aXg, aYg, aZg;
            if (src == ViiperGyroSourceKind.Mixed || src == ViiperGyroSourceKind.Handheld)
            {
                bool hasLeft = LegionButtonMonitor.TryGetLatestGyroSample(true, out LegionGyroSample left);
                bool hasRight = LegionButtonMonitor.TryGetLatestGyroSample(false, out LegionGyroSample right);
                if (!hasLeft && !hasRight)
                {
                    return false;
                }
                if (hasLeft && hasRight)
                {
                    // Shared mirror-inversion + average; same convention the legacy
                    // Mixed adapter uses so both backends agree on axis signs.
                    GyroSample merged = LegionMixedGyroMerge.Merge(left, right);
                    gXdps = merged.GyroXDegPerSecond;
                    gYdps = merged.GyroYDegPerSecond;
                    gZdps = merged.GyroZDegPerSecond;
                    aXg = merged.AccelXG;
                    aYg = merged.AccelYG;
                    aZg = merged.AccelZG;
                }
                else
                {
                    // Single-side fallback: pass the available side through untouched.
                    LegionGyroSample one = hasLeft ? left : right;
                    gXdps = one.GyroXDegPerSecond;
                    gYdps = one.GyroYDegPerSecond;
                    gZdps = one.GyroZDegPerSecond;
                    aXg = one.AccelXG;
                    aYg = one.AccelYG;
                    aZg = one.AccelZG;
                }
            }
            else
            {
                bool useLeft = src == ViiperGyroSourceKind.Left;
                if (!LegionButtonMonitor.TryGetLatestGyroSample(useLeft, out LegionGyroSample sample))
                {
                    return false;
                }
                gXdps = sample.GyroXDegPerSecond;
                gYdps = sample.GyroYDegPerSecond;
                gZdps = sample.GyroZDegPerSecond;
                aXg = sample.AccelXG;
                aYg = sample.AccelYG;
                aZg = sample.AccelZG;
            }

            short gX = SaturateToShort(gXdps * GyroDpsToRawCounts);
            short gY = SaturateToShort(gYdps * GyroDpsToRawCounts);
            short gZ = SaturateToShort(gZdps * GyroDpsToRawCounts);
            short aX = SaturateToShort(aXg * AccelGToRawCounts);
            short aY = SaturateToShort(aYg * AccelGToRawCounts);
            short aZ = SaturateToShort(aZg * AccelGToRawCounts);

            // Apply user-selectable axis remap. Each output channel pulls from source[src]
            // and optionally flips sign. Accel tracks the same map as gyro so the vectors
            // stay coherent (matches the reference VIIPER Controller app behavior).
            int xSrc = axisMapXSrc, xSign = axisMapXSign;
            int ySrc = axisMapYSrc, ySign = axisMapYSign;
            int zSrc = axisMapZSrc, zSign = axisMapZSign;
            gyroXRaw = SignedClampToShort(PickAxis(gX, gY, gZ, xSrc) * xSign);
            gyroYRaw = SignedClampToShort(PickAxis(gX, gY, gZ, ySrc) * ySign);
            gyroZRaw = SignedClampToShort(PickAxis(gX, gY, gZ, zSrc) * zSign);
            accelXRaw = SignedClampToShort(PickAxis(aX, aY, aZ, xSrc) * xSign);
            accelYRaw = SignedClampToShort(PickAxis(aX, aY, aZ, ySrc) * ySign);
            accelZRaw = SignedClampToShort(PickAxis(aX, aY, aZ, zSrc) * zSign);
            return true;
        }

        private static short PickAxis(short x, short y, short z, int src)
        {
            switch (src)
            {
                case 0:  return x;
                case 1:  return y;
                default: return z;
            }
        }

        private static short SignedClampToShort(int value)
        {
            if (value > short.MaxValue) return short.MaxValue;
            if (value < short.MinValue) return short.MinValue;
            return (short)value;
        }

        private static short SaturateToShort(float value)
        {
            if (value > short.MaxValue) return short.MaxValue;
            if (value < short.MinValue) return short.MinValue;
            return (short)value;
        }

        private static short ScaleDs4Accel(short raw)
        {
            int scaled = raw * 2;
            if (scaled > short.MaxValue) return short.MaxValue;
            if (scaled < short.MinValue) return short.MinValue;
            return (short)scaled;
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            if (activeInstance == this) activeInstance = null;
            try
            {
                if (pollThread != null && pollThread.IsAlive)
                {
                    pollThread.Join(500);
                }
            }
            catch (Exception ex) { Logger.Warn($"VIIPER forwarder join threw: {ex.Message}"); }
            pollThread = null;

            // Tear down the stick-gyro adapter so the LegionButtonMonitor reference is
            // released. EnsureGyroAdapter rebuilds on next Start.
            stickGyro.Shutdown();

            // Clear any lingering rumble on the physical controller.
            try
            {
                var zero = new ViiperXInputVibration();
                ViiperXInput.SetState(physicalIndex, ref zero);
            }
            catch { }

            Logger.Info("VIIPER forwarder stopped");
        }

        public void Dispose()
        {
            Stop();
            if (service != null)
            {
                service.FeedbackReceived -= OnFeedbackReceived;
            }
        }

        private void PollLoop()
        {
            var xiState = new ViiperXInputState();
            uint lastPacket = unchecked((uint)-1);
            long lastLegionTicks = 0;
            int errorCount = 0;

            // Diagnostic counters: emit a periodic stats line so we can tell the
            // difference between "no input arriving" (legion sample stale / XInput
            // packet not advancing), "input arriving but not forwarded" (reports sent
            // is zero), and "forwarded but not visible to Windows" (reports sent > 0
            // yet user reports no input — see issue #79 vvalente30). Without this,
            // SetInput failures are logged but successes are silent, and the LegionHid
            // path simply skips on stale samples without any trace.
            int statsLegionFreshSamples = 0;
            int statsLegionStaleSamples = 0;
            int statsLegionMissingSamples = 0;
            int statsXInputFreshPackets = 0;
            int statsXInputStalePackets = 0;
            int statsXInputErrors = 0;
            int statsReportsSent = 0;
            int statsReportsFailed = 0;
            long statsWindowStartTicks = DateTime.UtcNow.Ticks;
            const long StatsWindowTicks = TimeSpan.TicksPerSecond * 5;

            while (running)
            {
                long nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - statsWindowStartTicks >= StatsWindowTicks)
                {
                    int rumbleRx = System.Threading.Interlocked.Exchange(ref statsRumbleEventsReceived, 0);
                    int rumbleOk = System.Threading.Interlocked.Exchange(ref statsRumbleForwardedOk, 0);
                    int rumbleErr = System.Threading.Interlocked.Exchange(ref statsRumbleForwardedErr, 0);
                    bool anyActivity = statsReportsSent > 0
                        || statsReportsFailed > 0
                        || statsLegionFreshSamples > 0
                        || statsLegionStaleSamples > 0
                        || statsLegionMissingSamples > 0
                        || statsXInputFreshPackets > 0
                        || statsXInputStalePackets > 0
                        || statsXInputErrors > 0
                        || rumbleRx > 0
                        || rumbleOk > 0
                        || rumbleErr > 0;
                    if (anyActivity)
                    {
                        Logger.Info(
                            "VIIPER forwarder 5s stats: source={0}, type={1}, physicalIdx={10}, " +
                            "reportsSent={2}, reportsFailed={3}, " +
                            "legionFresh={4}, legionStale={5}, legionMissing={6}, " +
                            "xinputFresh={7}, xinputStale={8}, xinputErrors={9}, " +
                            "rumbleRx={11}, rumbleOk={12}, rumbleErr={13}",
                            inputSource, targetType,
                            statsReportsSent, statsReportsFailed,
                            statsLegionFreshSamples, statsLegionStaleSamples, statsLegionMissingSamples,
                            statsXInputFreshPackets, statsXInputStalePackets, statsXInputErrors,
                            physicalIndex,
                            rumbleRx, rumbleOk, rumbleErr);
                    }
                    statsLegionFreshSamples = 0;
                    statsLegionStaleSamples = 0;
                    statsLegionMissingSamples = 0;
                    statsXInputFreshPackets = 0;
                    statsXInputStalePackets = 0;
                    statsXInputErrors = 0;
                    statsReportsSent = 0;
                    statsReportsFailed = 0;
                    statsWindowStartTicks = nowTicks;
                }

                // Steamdeck rumble auto-stop. Steam Deck haptic pulses are finite
                // (one 0x8F plays for Count * (Duration + Interval) microseconds);
                // when the train ends, Steam expects the physical Deck's LRAs to
                // fall silent naturally. XInput motors do NOT auto-stop, so we
                // detect expiration here and force a SetState(0,0) once the
                // pulse train has elapsed. Without this the physical pad buzzes
                // indefinitely after any single rumble event.
                if (IsSteamDeckLikeTarget() && DecayExpiredSteamDeckRumble())
                {
                    FlushSteamDeckRumbleToXInput();
                }

                try
                {
                    // Pause gate: while the manager is hot-swapping the virtual device,
                    // skip pumping input. This avoids thousands of "invalid input size"
                    // warnings during the 1-2 second window between RemoveDevice() and
                    // AddDevice() completing, and prevents the first post-swap packet
                    // from being built with the old targetType.
                    if (paused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    if (inputSource == ViiperInputSourceKind.LegionHid)
                    {
                        LegionGamepadSample sample;
                        if (!LegionButtonMonitor.TryGetLatestGamepadSample(out sample))
                        {
                            statsLegionMissingSamples++;
                            Thread.Sleep(8);
                            continue;
                        }
                        if (sample.TimestampTicksUtc == lastLegionTicks)
                        {
                            statsLegionStaleSamples++;
                            Thread.Sleep(4);
                            continue;
                        }
                        lastLegionTicks = sample.TimestampTicksUtc;
                        statsLegionFreshSamples++;
                        // LegionButtonMonitor uses its own AuxButtons bitmap — translate into
                        // the reference LegionAux layout so downstream wire builders see the
                        // correct paddle/Mode/Share bits.
                        currentAuxButtons = TranslateMonitorAuxToLegionAux(sample.AuxButtons);

                        // Guide-button mode: if the user wants Mode/Guide to open Xbox Game
                        // Bar instead of the emulated PS/Guide button, fire Win+G on press
                        // edge and strip the press from the outgoing state.
                        bool guidePressed = ((sample.Buttons & ViiperXInput.Guide) != 0)
                                         || ((currentAuxButtons & LegionAux.Mode) != 0);
                        ApplyGuideModeEdge(guidePressed);
                        if (guideMode == ViiperGuideButtonMode.GameBar)
                        {
                            currentAuxButtons &= unchecked((ushort)~LegionAux.Mode);
                        }

                        // Latest right-touchpad state from Legion HID (DS4/DSE wire format
                        // expects touchpad coordinates as 12-bit packed values).
                        LegionTouchpadSample touch;
                        if (LegionButtonMonitor.TryGetLatestRightTouchpadSample(out touch))
                        {
                            currentTouchActive = touch.IsTouching;
                            currentTouchPressed = touch.IsPressed;
                            currentTouchX = touch.RawX;
                            currentTouchY = touch.RawY;
                        }
                        else
                        {
                            currentTouchActive = false;
                            currentTouchPressed = false;
                        }

                        var gp = ConvertLegionToXInputGamepad(sample);
                        if (guideMode == ViiperGuideButtonMode.GameBar)
                        {
                            gp.Buttons &= unchecked((ushort)~ViiperXInput.Guide);
                        }
                        else if (IsGuideHoldActive())
                        {
                            gp.Buttons |= ViiperXInput.Guide;
                        }
                        // Stick-gyro override: for target types with no native motion
                        // field, synthesize a stick contribution from gyro motion so
                        // users get the same Mode-1 ("Xbox / Stick") feel they had on
                        // the legacy ViGEm backend. LegionHid path provides the raw
                        // aux buttons for activation gating (M1/M2/M3/Y1/Y2/Y3). The
                        // processor honors the user's "Send to joystick" choice via
                        // stickGyro.RoutesToLeftStick (Left vs Right).
                        if (ViiperStickGyroProcessor.IsApplicableForTarget(targetType))
                        {
                            short physX = stickGyro.RoutesToLeftStick ? gp.ThumbLX : gp.ThumbRX;
                            short physY = stickGyro.RoutesToLeftStick ? gp.ThumbLY : gp.ThumbRY;
                            if (stickGyro.TryComputeStickOverride(gp.Buttons, gp.LeftTrigger, gp.RightTrigger,
                                    sample.AuxButtons, physX, physY, out short sgX, out short sgY))
                            {
                                if (stickGyro.RoutesToLeftStick)
                                {
                                    ViiperStickGyroProcessor.MergeStickVectors(gp.ThumbLX, gp.ThumbLY, sgX, sgY,
                                        out short mergedX, out short mergedY);
                                    gp.ThumbLX = mergedX;
                                    gp.ThumbLY = mergedY;
                                }
                                else
                                {
                                    ViiperStickGyroProcessor.MergeStickVectors(gp.ThumbRX, gp.ThumbRY, sgX, sgY,
                                        out short mergedX, out short mergedY);
                                    gp.ThumbRX = mergedX;
                                    gp.ThumbRY = mergedY;
                                }
                            }
                        }
                        byte[] data = BuildDeviceInput(gp);
                        if (data != null && data.Length > 0)
                        {
                            if (service.SetInput(busId, deviceId, data)) statsReportsSent++;
                            else statsReportsFailed++;
                        }
                    }
                    else // XInput
                    {
                        var rc = ViiperXInput.GetState(physicalIndex, ref xiState);
                        if (rc != ViiperXInput.ErrorSuccess)
                        {
                            statsXInputErrors++;
                            if (errorCount++ < 5 && Logger.IsDebugEnabled)
                            {
                                Logger.Debug($"XInput.GetState({physicalIndex}) rc=0x{rc:X8}");
                            }
                            Thread.Sleep(16);
                            continue;
                        }
                        errorCount = 0;

                        if (xiState.PacketNumber == lastPacket)
                        {
                            statsXInputStalePackets++;
                            Thread.Sleep(4);
                            continue;
                        }
                        lastPacket = xiState.PacketNumber;
                        statsXInputFreshPackets++;
                        currentAuxButtons = 0;  // XInput has no Legion aux buttons.
                        currentTouchActive = false;
                        currentTouchPressed = false;

                        bool guidePressed = (xiState.Gamepad.Buttons & ViiperXInput.Guide) != 0;
                        ApplyGuideModeEdge(guidePressed);

                        var gp = xiState.Gamepad;
                        if (guideMode == ViiperGuideButtonMode.GameBar)
                        {
                            gp.Buttons &= unchecked((ushort)~ViiperXInput.Guide);
                        }
                        else if (IsGuideHoldActive())
                        {
                            gp.Buttons |= ViiperXInput.Guide;
                        }
                        // Stick-gyro override on the XInput input path. No Legion aux
                        // buttons are available here (XInput hardware can't see them),
                        // so activation buttons 17-22 will read 0 and never trigger.
                        if (ViiperStickGyroProcessor.IsApplicableForTarget(targetType))
                        {
                            short physX2 = stickGyro.RoutesToLeftStick ? gp.ThumbLX : gp.ThumbRX;
                            short physY2 = stickGyro.RoutesToLeftStick ? gp.ThumbLY : gp.ThumbRY;
                            if (stickGyro.TryComputeStickOverride(gp.Buttons, gp.LeftTrigger, gp.RightTrigger,
                                    0 /* no aux on XInput path */, physX2, physY2, out short sgX, out short sgY))
                            {
                                if (stickGyro.RoutesToLeftStick)
                                {
                                    ViiperStickGyroProcessor.MergeStickVectors(gp.ThumbLX, gp.ThumbLY, sgX, sgY,
                                        out short mergedXl, out short mergedYl);
                                    gp.ThumbLX = mergedXl;
                                    gp.ThumbLY = mergedYl;
                                }
                                else
                                {
                                    ViiperStickGyroProcessor.MergeStickVectors(gp.ThumbRX, gp.ThumbRY, sgX, sgY,
                                        out short mergedX, out short mergedY);
                                    gp.ThumbRX = mergedX;
                                    gp.ThumbRY = mergedY;
                                }
                            }
                        }
                        byte[] data = BuildDeviceInput(gp);
                        if (data != null && data.Length > 0)
                        {
                            if (service.SetInput(busId, deviceId, data)) statsReportsSent++;
                            else statsReportsFailed++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"VIIPER forwarder poll error: {ex.Message}");
                    Thread.Sleep(100);
                }

                // Block until LegionButtonMonitor signals a new sample (or 16ms
                // timeout as a fallback for non-Legion sources / XInput-only).
                // Replaces the old unconditional Thread.Sleep(4) which paced
                // the loop at ~64Hz independent of the 125Hz HID arrival rate
                // — overwrote half the samples in the cache before we could
                // read them. Event-driven design: zero CPU when idle, catches
                // every HID report at its arrival.
                if (inputSource == ViiperInputSourceKind.LegionHid)
                {
                    LegionButtonMonitor.WaitForNewSample(16);
                }
                else
                {
                    Thread.Sleep(4);
                }
            }
        }

        /// <summary>
        /// Detects a press-edge on the Guide/Mode button. In GameBar mode, fires a
        /// single Win+G keystroke via the helper's input-injector path. Rate-limited
        /// so a held button doesn't spam the shortcut.
        /// </summary>
        private void ApplyGuideModeEdge(bool isPressed)
        {
            bool edge = isPressed && !guideWasPressed;
            guideWasPressed = isPressed;
            if (!edge) return;
            if (guideMode != ViiperGuideButtonMode.GameBar) return;

            long now = DateTime.UtcNow.Ticks;
            if ((now - lastGuideShortcutTicks) < GuideShortcutMinIntervalTicks) return;
            lastGuideShortcutTicks = now;

            try
            {
                Logger.Info("VIIPER guide-button: firing Win+G");
                Program.SendKeyboardShortcut("Win+G");
            }
            catch (Exception ex) { Logger.Warn($"Guide Win+G failed: {ex.Message}"); }
        }

        /// <summary>
        /// Adapts a Legion Go HID gamepad sample to the XInput-shaped struct the
        /// wire-format builders already consume. The Buttons bitfield from the Legion
        /// monitor is already XInput-compatible.
        /// </summary>
        private static ViiperXInputGamepad ConvertLegionToXInputGamepad(LegionGamepadSample s)
        {
            return new ViiperXInputGamepad
            {
                Buttons = s.Buttons,
                LeftTrigger = s.LeftTrigger,
                RightTrigger = s.RightTrigger,
                ThumbLX = s.LeftStickX,
                ThumbLY = s.LeftStickY,
                ThumbRX = s.RightStickX,
                ThumbRY = s.RightStickY,
            };
        }

        private byte[] BuildDeviceInput(ViiperXInputGamepad gp)
        {
            switch (targetType)
            {
                case "xbox360":
                    return BuildXbox360Input(gp);
                case "dualshock4":
                    return BuildDualShock4Input(gp);
                case "dualsenseedge":
                case "dualsense-edge":
                    return BuildDualSenseEdgeInput(gp);
                case "dualsense":
                case "ds5":
                    return BuildDualSenseInput(gp);
                case "xboxelite2":
                case "xbox-one":
                case "xbox-elite":
                    return BuildXboxElite2Input(gp);
                // libviiper xboxgip target — 3-interface USB GIP device.
                // Wire format is the same 20-byte InputState as xbox360
                // (per device/xboxgip/inputstate.go); libviiper remaps to
                // GIP buttons internally and ships paddles via the
                // Reserved[0]/Reserved[1] bytes when PID=0x0B00. We use
                // the xbox360 builder here for now (no paddle bytes yet);
                // dedicated paddle path can be added once descriptor +
                // metadata path is verified by USB tree dump.
                case "xboxgip":
                    return BuildXbox360Input(gp);
                // libviiper 32c8ed3 + clib rework split Steam-handheld targets off into
                // a dedicated steamdeck device with a 64-byte native wire format. The
                // old steam-generic / steamdeck-generic aliases now route to steamdeck
                // (with deprecation warnings) and any per-handheld alias (legion-go-2,
                // rog-ally, etc.) does the same with a VID/PID override. All of them
                // need the new wire format; sending the prior 33-byte elite2 format
                // fails UnmarshalBinary inside libviiper and the virtual pad receives
                // no input.
                case "steamdeck":
                case "steam-deck":
                case "steamdeck-generic":
                case "steam-generic":
                case "legion-go":
                case "legion-go-s":
                case "legion-go-2":
                case "msi-claw":
                case "rog-ally":
                case "zotac-zone":
                    return BuildSteamDeckInput(gp);
                // Steam Controller V1 (Gordon) — separate libviiper device from
                // steamdeck, separate 64-byte wire layout. Routed here via the
                // Steam sub-device combo (Tag="gordon") which ViiperEmulationManager
                // translates to targetType = "gordon" in ResolveDeviceTargets.
                case "gordon":
                case "steam-controller-v1":
                case "steamcontroller-v1":
                case "steam-controller":
                    return BuildSteamControllerInput(gp);
                case "switchpro":
                case "joycon-left":
                case "joycon-right":
                    return BuildSwitchProInput(gp);
                default:
                    return BuildXbox360Input(gp);
            }
        }

        /// <summary>
        /// Switch Pro / Joy-Con wire format (24 bytes). libviiper's switchpro InputState
        /// requires ≥24 bytes; falling through to BuildXbox360Input sent only 20 bytes
        /// and every poll logged "switchpro: input state too short: 20 &lt; 24".
        /// Button positions are mapped positionally (Xbox A → Switch B since both are
        /// the bottom face button).
        /// </summary>
        private static byte[] BuildSwitchProInput(ViiperXInputGamepad gp)
        {
            var data = new byte[24];
            uint buttons = 0;

            // Face buttons — positional.
            if ((gp.Buttons & ViiperXInput.A) != 0) buttons |= 0x000004;  // B (bottom)
            if ((gp.Buttons & ViiperXInput.B) != 0) buttons |= 0x000008;  // A (right)
            if ((gp.Buttons & ViiperXInput.X) != 0) buttons |= 0x000001;  // Y (left)
            if ((gp.Buttons & ViiperXInput.Y) != 0) buttons |= 0x000002;  // X (top)
            if ((gp.Buttons & ViiperXInput.LB) != 0) buttons |= 0x400000; // L
            if ((gp.Buttons & ViiperXInput.RB) != 0) buttons |= 0x000040; // R

            // Switch has no analog triggers — map >50% press to ZL/ZR digital.
            if (gp.LeftTrigger > 128) buttons |= 0x800000;  // ZL
            if (gp.RightTrigger > 128) buttons |= 0x000080; // ZR

            if ((gp.Buttons & ViiperXInput.Back) != 0) buttons |= 0x000100;       // Minus
            if ((gp.Buttons & ViiperXInput.Start) != 0) buttons |= 0x000200;      // Plus
            if ((gp.Buttons & ViiperXInput.Guide) != 0) buttons |= 0x001000;      // Home
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) buttons |= 0x000800;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) buttons |= 0x000400;

            if ((gp.Buttons & ViiperXInput.DPadUp) != 0)    buttons |= 0x020000;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0)  buttons |= 0x010000;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0)  buttons |= 0x080000;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) buttons |= 0x040000;

            WriteU32(data, 0, buttons);

            WriteI16(data, 4, gp.ThumbLX);
            WriteI16(data, 6, gp.ThumbLY);
            WriteI16(data, 8, gp.ThumbRX);
            WriteI16(data, 10, gp.ThumbRY);

            // Bytes 12-23 are IMU (gyro XYZ + accel XYZ as int16). Leave zeroed here;
            // if the caller wants gyro on Switch output, TryBuildImuCounts can populate
            // these exactly like the DSE path. Future enhancement.
            return data;
        }

        // -------------------------------------------------------------------
        // Wire format builders (ported from ViiperController reference impl)
        // -------------------------------------------------------------------

        private static byte[] BuildXbox360Input(ViiperXInputGamepad gp)
        {
            var data = new byte[20];
            WriteU32(data, 0, gp.Buttons);
            data[4] = gp.LeftTrigger;
            data[5] = gp.RightTrigger;
            WriteI16(data, 6, gp.ThumbLX);
            WriteI16(data, 8, gp.ThumbLY);
            WriteI16(data, 10, gp.ThumbRX);
            WriteI16(data, 12, gp.ThumbRY);
            return data;
        }

        private byte[] BuildDualShock4Input(ViiperXInputGamepad gp)
        {
            var data = new byte[31];
            // DS4 sticks are int8. Y-axis: XInput positive=UP, DS4 positive=DOWN → negate.
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            ushort ds4Buttons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) ds4Buttons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) ds4Buttons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) ds4Buttons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) ds4Buttons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) ds4Buttons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) ds4Buttons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) ds4Buttons |= 0x1000;
            if ((gp.Buttons & ViiperXInput.Start) != 0) ds4Buttons |= 0x2000;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) ds4Buttons |= 0x4000;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) ds4Buttons |= 0x8000;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) ds4Buttons |= 0x0001;

            // Legion back buttons -> DS4 extensions (preserve the extra-button channel the
            // user gets from Legion HID, so they don't go to waste in the DS4 mapping).
            ushort aux = currentAuxButtons;
            if ((aux & LegionAux.Mode) != 0) ds4Buttons |= 0x0001;   // Mode -> PS
            if ((aux & LegionAux.Share) != 0) ds4Buttons |= 0x0002;  // Share -> Touchpad click
            WriteU16(data, 4, ds4Buttons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[6] = dpad;

            data[7] = gp.LeftTrigger;
            data[8] = gp.RightTrigger;

            // Touchpad bytes (DS4): X at 9-10, Y at 11-12, active flag at 13.
            if (currentTouchActive)
            {
                ushort tx = ScaleTouchAxis(currentTouchX, 1919);
                ushort ty = ScaleTouchAxis(currentTouchY, 942);
                WriteU16(data, 9, tx);
                WriteU16(data, 11, ty);
                data[13] = 1;
            }

            // IMU bytes at offsets 19-30 (gyroX,Y,Z then accelX,Y,Z as int16).
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 19, gx);
                WriteI16(data, 21, gy);
                WriteI16(data, 23, gz);
                WriteI16(data, 25, ScaleDs4Accel(ax));
                WriteI16(data, 27, ScaleDs4Accel(ay));
                WriteI16(data, 29, ScaleDs4Accel(az));
            }
            return data;
        }

        private byte[] BuildDualSenseEdgeInput(ViiperXInputGamepad gp)
        {
            var data = new byte[33];
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            uint dseButtons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) dseButtons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) dseButtons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) dseButtons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) dseButtons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) dseButtons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) dseButtons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) dseButtons |= 0x1000;
            if ((gp.Buttons & ViiperXInput.Start) != 0) dseButtons |= 0x2000;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) dseButtons |= 0x4000;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) dseButtons |= 0x8000;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) dseButtons |= 0x00010000;

            // Legion back paddles -> DSE Edge paddle buttons. DSE paddle bit masks:
            //   ExtraL2=0x00100000, ExtraL1=0x00400000,
            //   ExtraR1=0x00200000, ExtraL3=0x00800000.
            ushort aux = currentAuxButtons;
            if ((aux & LegionAux.Y1) != 0) dseButtons |= 0x00100000;    // Y1 (left upper)  -> ExtraL2
            if ((aux & LegionAux.Y2) != 0) dseButtons |= 0x00400000;    // Y2 (left lower)  -> ExtraL1
            if ((aux & LegionAux.Y3) != 0) dseButtons |= 0x00200000;    // Y3 (right upper) -> ExtraR1
            if ((aux & LegionAux.M3) != 0) dseButtons |= 0x00800000;    // M3 (right lower) -> ExtraL3
            if ((aux & LegionAux.Mode) != 0) dseButtons |= 0x00010000;  // Mode  -> PS
            if ((aux & LegionAux.Share) != 0) dseButtons |= 0x00020000; // Share -> Touchpad click
            WriteU32(data, 4, dseButtons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[8] = dpad;

            data[9] = gp.LeftTrigger;
            data[10] = gp.RightTrigger;

            // Touchpad bytes (DSE): X at 11-12, Y at 13-14, active flag at 15.
            if (currentTouchActive)
            {
                ushort tx = ScaleTouchAxis(currentTouchX, 1920);
                ushort ty = ScaleTouchAxis(currentTouchY, 1080);
                WriteU16(data, 11, tx);
                WriteU16(data, 13, ty);
                data[15] = 1;
            }

            // DSE IMU bytes at offsets 21..32.
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 21, gx);
                WriteI16(data, 23, gy);
                WriteI16(data, 25, gz);
                WriteI16(data, 27, ScaleDs4Accel(ax));
                WriteI16(data, 29, ScaleDs4Accel(ay));
                WriteI16(data, 31, ScaleDs4Accel(az));
            }
            return data;
        }

        /// <summary>
        /// Steam Controller V1 (Gordon, PID 0x1102) wire format from libviiper
        /// device/steamcontroller/inputstate.go. 64-byte native report (matches
        /// the physical wired Steam Controller's USB report) — same framing as
        /// steamdeck (0x01 / 0x00 / ReportID / PayloadLen / u32 frame at 0..7)
        /// but the button bits, axis layout, and the lack of a right stick all
        /// differ. Gordon hardware has: ABXY/L1/R1, L2/R2 analog triggers,
        /// Menu/Steam/Options, dpad, L3, left+right grip buttons, twin clickable
        /// touchpads (no L4/L5/R4/R5/QuickAccess like the Deck), one analog
        /// stick on the LEFT only, and IMU.
        ///
        /// XInput → Gordon mapping:
        ///   A/B/X/Y, LB/RB        → byte 8 (A=0x80 X=0x40 B=0x20 Y=0x10
        ///                                   L1=0x08 R1=0x04 L2=0x02 R2=0x01)
        ///   LT/RT > deadzone      → L2/R2 digital bits in byte 8
        ///   DPad                  → byte 9 (Up=0x01 Right=0x02 Left=0x04 Down=0x08)
        ///   Start/Back/Guide      → Menu (0x10) / Options (0x40) / Steam (0x20) byte 9
        ///   LeftThumb (L3)        → byte 10 bit 0x40
        ///   LStick                → bytes 54..58 (LStickX/Y i16 LE)
        ///   RStick                → bytes 20..24 (RPadX/Y — Gordon has no right
        ///                           stick; Steam treats the right touchpad as
        ///                           the virtual right stick, so XInput RStick
        ///                           maps there for natural game compat)
        ///   LT/RT analog          → byte 11/12 (single-byte) AND bytes 24..28
        ///                           (u16 LE, scaled ×257). Gordon writes both
        ///                           slots; consumers may read either.
        ///   Legion paddles        → grip / pad-press: Y1 → LPadPress (byte 10
        ///                           bit 0x02), Y2 → LGrip (byte 9 bit 0x80),
        ///                           Y3 → RPadPress (byte 10 bit 0x04),
        ///                           M3 → RGrip (byte 10 bit 0x01). Mode → Steam,
        ///                           Share → unused (no DS-style touch click).
        ///
        /// Legion's right touchpad coords feed RPadX/Y (Steam Controller's
        /// right touchpad). LPadX/Y stays at zero — Gordon's left touchpad has
        /// no physical analog on the Legion.
        /// </summary>
        private byte[] BuildSteamControllerInput(ViiperXInputGamepad gp)
        {
            var data = new byte[64];
            data[0] = 0x01;
            data[1] = 0x00;
            data[2] = 0x01;     // InputReportID — Gordon's input report ID
            data[3] = 0x3C;     // payload length placeholder (60); device parses by offset
            unchecked { gordonFrameCounter++; }
            WriteU32(data, 4, gordonFrameCounter);

            ushort aux = currentAuxButtons;

            // Byte 8 — A/X/B/Y/L1/R1/L2digital/R2digital.
            byte b8 = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) b8 |= 0x80;
            if ((gp.Buttons & ViiperXInput.X) != 0) b8 |= 0x40;
            if ((gp.Buttons & ViiperXInput.B) != 0) b8 |= 0x20;
            if ((gp.Buttons & ViiperXInput.Y) != 0) b8 |= 0x10;
            if ((gp.Buttons & ViiperXInput.LB) != 0) b8 |= 0x08;
            if ((gp.Buttons & ViiperXInput.RB) != 0) b8 |= 0x04;
            if (gp.LeftTrigger > 30) b8 |= 0x02;
            if (gp.RightTrigger > 30) b8 |= 0x01;
            data[8] = b8;

            // Byte 9 — Up/Right/Left/Down/Menu/Steam/Options/LGrip.
            byte b9 = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) b9 |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) b9 |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) b9 |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) b9 |= 0x08;
            if ((gp.Buttons & ViiperXInput.Start) != 0) b9 |= 0x10;             // Menu
            if (((gp.Buttons & ViiperXInput.Guide) != 0)
                || ((aux & LegionAux.Mode) != 0)) b9 |= 0x20;                   // Steam
            if ((gp.Buttons & ViiperXInput.Back) != 0) b9 |= 0x40;              // Options
            if ((aux & LegionAux.Y2) != 0) b9 |= 0x80;                          // LGrip (Y2 = back L lower)
            data[9] = b9;

            // Byte 10 — RGrip/LPadPress/RPadPress/LPadTouch/RPadTouch/L3/LPadAndJoy.
            byte b10 = 0;
            if ((aux & LegionAux.M3) != 0) b10 |= 0x01;                         // RGrip (M3 = back R lower)
            if ((aux & LegionAux.Y1) != 0) b10 |= 0x02;                         // LPadPress (Y1 = back L upper)
            if ((aux & LegionAux.Y3) != 0) b10 |= 0x04;                         // RPadPress (Y3 = back R upper)
            if (currentTouchActive)        b10 |= 0x10;                         // RPadTouch (Legion's pad → Gordon's right pad)
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) b10 |= 0x40;        // L3
            // LPadAndJoy (0x80) stays off — we route XInput LStick to the LStick slot, not the LPad slot.
            data[10] = b10;

            // Bytes 11/12 — analog trigger single-byte slots (Gordon writes them
            // alongside the 16-bit slots at 24..28).
            data[11] = gp.LeftTrigger;
            data[12] = gp.RightTrigger;

            // Bytes 20..24 — RPadX/Y (XInput right stick → right touchpad coords).
            WriteI16(data, 20, gp.ThumbRX);
            WriteI16(data, 22, gp.ThumbRY);

            // Bytes 24..28 — LTrigger/RTrigger u16 LE.
            WriteU16(data, 24, (ushort)(gp.LeftTrigger * 257));
            WriteU16(data, 26, (ushort)(gp.RightTrigger * 257));

            // Bytes 28..40 — Accel X/Y/Z then Gyro X/Y/Z (raw counts).
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 28, ax);
                WriteI16(data, 30, ay);
                WriteI16(data, 32, az);
                WriteI16(data, 34, gx);
                WriteI16(data, 36, gy);
                WriteI16(data, 38, gz);
            }
            // Bytes 40..48 — gyroQuat W/X/Y/Z left zero (we don't emit a quaternion).

            // Bytes 50..54 — duplicate trigger slots Gordon firmware also fills.
            WriteU16(data, 50, (ushort)(gp.LeftTrigger * 257));
            WriteU16(data, 52, (ushort)(gp.RightTrigger * 257));

            // Bytes 54..58 — LStickX/Y (Gordon's only physical stick).
            WriteI16(data, 54, gp.ThumbLX);
            WriteI16(data, 56, gp.ThumbLY);

            // Bytes 58..62 — LPadX/Y (left touchpad — Legion has no left pad,
            // leave zero). Bytes 62..64 = BatteryMilliVolts; leave zero (wired
            // Gordon reports zero too).
            return data;
        }

        /// <summary>
        /// DualSense (non-Edge, PID 0x0CE6) wire format from libviiper
        /// device/dualsense/inputstate.go. Same 33-byte layout as DSE through
        /// offset 15 — sticks (i8×4) + buttons (u32) + dpad (u8) + L2/R2 (u8)
        /// + touch1X/Y (u16×2) + touchActive (bool). At offsets 16..20 DSE
        /// has touch2 (5 bytes); DS5 has 5 reserved bytes there. The IMU
        /// block at 21..32 is identical (gyro X/Y/Z + accel X/Y/Z, int16
        /// each, accel pre-scaled by ScaleDs4Accel). Skipping touch2 leaves
        /// those bytes zero, which matches DS5's reserved-bytes contract.
        ///
        /// XInput → DS5 button mapping mirrors BuildDualSenseEdgeInput but
        /// without the four paddle bits (0x00100000-0x00800000) — the
        /// non-Edge DS has no paddles, so those bits are unused in its
        /// Buttons u32. Legion paddles still map onto PS/Touchpad via
        /// Mode/Share so the user keeps some back-button signal.
        /// </summary>
        private byte[] BuildDualSenseInput(ViiperXInputGamepad gp)
        {
            var data = new byte[33];
            data[0] = (byte)(gp.ThumbLX >> 8);
            data[1] = (byte)(NegateClamp(gp.ThumbLY) >> 8);
            data[2] = (byte)(gp.ThumbRX >> 8);
            data[3] = (byte)(NegateClamp(gp.ThumbRY) >> 8);

            uint dsButtons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) dsButtons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.B) != 0) dsButtons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.X) != 0) dsButtons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.Y) != 0) dsButtons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LB) != 0) dsButtons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RB) != 0) dsButtons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Back) != 0) dsButtons |= 0x1000;       // Create
            if ((gp.Buttons & ViiperXInput.Start) != 0) dsButtons |= 0x2000;      // Options
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) dsButtons |= 0x4000;  // L3
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) dsButtons |= 0x8000; // R3
            if ((gp.Buttons & ViiperXInput.Guide) != 0) dsButtons |= 0x00010000;  // PS

            ushort aux = currentAuxButtons;
            if ((aux & LegionAux.Mode) != 0) dsButtons |= 0x00010000;             // Mode  → PS
            if ((aux & LegionAux.Share) != 0) dsButtons |= 0x00020000;            // Share → Touchpad click
            WriteU32(data, 4, dsButtons);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[8] = dpad;

            data[9] = gp.LeftTrigger;
            data[10] = gp.RightTrigger;

            // DS5 single touch slot (offsets 11-15: X u16, Y u16, active bool).
            if (currentTouchActive)
            {
                ushort tx = ScaleTouchAxis(currentTouchX, 1920);
                ushort ty = ScaleTouchAxis(currentTouchY, 1080);
                WriteU16(data, 11, tx);
                WriteU16(data, 13, ty);
                data[15] = 1;
            }
            // Offsets 16..20 stay zero — reserved bytes on DS5 wire.

            // IMU at 21..32, same scaling as DSE/DS4 builders.
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 21, gx);
                WriteI16(data, 23, gy);
                WriteI16(data, 25, gz);
                WriteI16(data, 27, ScaleDs4Accel(ax));
                WriteI16(data, 29, ScaleDs4Accel(ay));
                WriteI16(data, 31, ScaleDs4Accel(az));
            }
            return data;
        }

        /// <summary>
        /// Legion touchpad raw range is 0-1023 (10-bit). DS4 host expects 0-1919x942,
        /// DSE expects 0-1920x1080. Scale linearly and clamp.
        /// </summary>
        private static ushort ScaleTouchAxis(ushort raw, int maxOut)
        {
            int scaled = raw * maxOut / 1023;
            if (scaled < 0) return 0;
            if (scaled > maxOut) return (ushort)maxOut;
            return (ushort)scaled;
        }

        /// <summary>
        /// Xbox Elite 2 wire format (33 bytes). Also used for Steam Generic, Steam Deck,
        /// and Steam Controller targets since libviiper routes those through the Elite 2
        /// InputState. Reads <see cref="currentAuxButtons"/> so Legion Go back-paddles
        /// (Y1/Y2/Y3/M3), Mode and Share reach the virtual pad — without this, Steam-
        /// target emulation was silent on every back-paddle press (issue reported via
        /// Steam Generic / Steam Deck Go S profiles).
        ///
        /// Paddle bit layout matches the reference VIIPER app (aligned with HHD):
        ///   Y1 → P1 (0x1000), Y3 → P2 (0x2000), Y2 → P3 (0x4000), M3 → P4 (0x8000).
        /// Mode → Guide (0x0400); Share → reserved bit at data[13] |= 0x01.
        /// </summary>
        private byte[] BuildXboxElite2Input(ViiperXInputGamepad gp)
        {
            var data = new byte[33];
            ushort buttons = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) buttons |= 0x0001;
            if ((gp.Buttons & ViiperXInput.B) != 0) buttons |= 0x0002;
            if ((gp.Buttons & ViiperXInput.X) != 0) buttons |= 0x0004;
            if ((gp.Buttons & ViiperXInput.Y) != 0) buttons |= 0x0008;
            if ((gp.Buttons & ViiperXInput.LB) != 0) buttons |= 0x0010;
            if ((gp.Buttons & ViiperXInput.RB) != 0) buttons |= 0x0020;
            if ((gp.Buttons & ViiperXInput.Back) != 0) buttons |= 0x0040;
            if ((gp.Buttons & ViiperXInput.Start) != 0) buttons |= 0x0080;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) buttons |= 0x0100;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) buttons |= 0x0200;
            if ((gp.Buttons & ViiperXInput.Guide) != 0) buttons |= 0x0400;

            // Legion aux → Elite 2 paddles + Guide.
            // libviiper's Steam path decodes the P-bits as:
            //   P1 → R5 (right lower), P2 → L5 (left lower),
            //   P3 → R4 (right upper), P4 → L4 (left upper).
            // To land each physical Legion paddle on its matching position we therefore
            // send Y1→P4, Y3→P3, Y2→P2, M3→P1 — both the L/R and the upper/lower pair
            // are flipped vs. the HHD order I originally ported, confirmed empirically
            // by re-testing on Steam Generic / Steam Deck / GO S targets.
            ushort aux = currentAuxButtons;
            if ((aux & LegionAux.Mode) != 0) buttons |= 0x0400; // Mode → Guide
            if ((aux & LegionAux.Y1) != 0)   buttons |= 0x8000; // Y1 (back L upper) → P4 (L4)
            if ((aux & LegionAux.Y3) != 0)   buttons |= 0x4000; // Y3 (back R upper) → P3 (R4)
            if ((aux & LegionAux.Y2) != 0)   buttons |= 0x2000; // Y2 (back L lower) → P2 (L5)
            if ((aux & LegionAux.M3) != 0)   buttons |= 0x1000; // M3 (back R lower) → P1 (R5)
            WriteU16(data, 0, buttons);

            data[2] = gp.LeftTrigger;
            data[3] = gp.RightTrigger;

            WriteI16(data, 4, gp.ThumbLX);
            WriteI16(data, 6, gp.ThumbLY);
            WriteI16(data, 8, gp.ThumbRX);
            WriteI16(data, 10, gp.ThumbRY);

            byte dpad = 0;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) dpad |= 0x01;
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) dpad |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) dpad |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) dpad |= 0x08;
            data[12] = dpad;

            // Share → reserved bit at byte 13 (matches reference app's mapping).
            if ((aux & LegionAux.Share) != 0) data[13] |= 0x01;

            // Bytes 14-25: IMU (gyro X/Y/Z + accel X/Y/Z as int16). libviiper's
            // xboxelite2 InputState already carries these on the wire for Steam
            // Deck / Steam Generic profiles (see vigemtest/viiper/device/
            // xboxelite2/inputstate.go:18-19) — we just hadn't been populating
            // them, so games seeing the device through Steam Deck native HID
            // path got zero gyro/accel. Reuses TryBuildImuCounts, the same
            // helper our DS4/DSE wire builders use.
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 14, gx);
                WriteI16(data, 16, gy);
                WriteI16(data, 18, gz);
                WriteI16(data, 20, ax);
                WriteI16(data, 22, ay);
                WriteI16(data, 24, az);
            }

            // Bytes 26-32: right-touchpad (Legion's only touchpad). libviiper's
            // xboxelite2 InputState uses signed int16 centered coords for
            // Steam Deck profiles (-32768..32767, 0 = center) per the wire
            // contract in device/xboxelite2/inputstate.go:21-24. Force/
            // pressure is a uint16 — we don't have a force sensor on Legion's
            // touchpad, so set a midrange value (0x4000 ≈ 16384) when pressed
            // and 0 when only touching, matching Steam Deck's typical wire
            // emission for a non-force-sensitive press.
            // Touchpad bytes already gated by ps4TouchpadEnabled at the source
            // (forwarder zeroes currentTouchActive when disabled) so this
            // honors the user's existing toggle.
            if (currentTouchActive)
            {
                byte flags = (byte)LibViiperTouchFlags.RightPadTouch;
                if (currentTouchPressed) flags |= (byte)LibViiperTouchFlags.RightPadPress;
                data[26] = flags;
                // Convert Legion's 10-bit raw (0..1023) to libviiper's centered int16.
                // (raw - 512) * 64 maps 0..1023 → -32768..32704 — close to the
                // full int16 range without overflow at the edges.
                short padX = (short)(((int)currentTouchX - 512) * 64);
                short padY = (short)(((int)currentTouchY - 512) * 64);
                WriteI16(data, 27, padX);
                WriteI16(data, 29, padY);
                ushort force = currentTouchPressed ? (ushort)0x4000 : (ushort)0;
                WriteU16(data, 31, force);
            }

            return data;
        }

        // Mirror of the libviiper xboxelite2 InputState touch-flag bits
        // (device/xboxelite2/inputstate.go:39-40).
        [Flags]
        private enum LibViiperTouchFlags : byte
        {
            RightPadTouch = 0x01,
            RightPadPress = 0x02,
        }

        // libviiper's steamdeck device wire format (device/steamdeck/inputstate.go +
        // const.go). 64-byte report:
        //   [0]  = 0x01           magic (zero would clear Frame in UnmarshalBinary)
        //   [1]  = 0x00
        //   [2]  = 0x09           InputReportID
        //   [3]  = 0x38           DeckInputPayloadLen (56)
        //   [4..7] = uint32 LE frame counter
        //   [8]  bits: A=0x80 X=0x40 B=0x20 Y=0x10 L1=0x08 R1=0x04 L2D=0x02 R2D=0x01
        //   [9]  bits: L5=0x80 Menu=0x40 Steam=0x20 Options=0x10 Down=0x08 Left=0x04 Right=0x02 Up=0x01
        //   [10] bits: L3=0x40 RPadTouch=0x10 LPadTouch=0x08 RPadPress=0x04 LPadPress=0x02 R5=0x01
        //   [11] bits: R3=0x04
        //   [12] = unused
        //   [13] bits: RStickTouch=0x80 LStickTouch=0x40 R4=0x04 L4=0x02
        //   [14] bits: QuickAccess=0x04
        //   [15] = Reserved15
        //   [16..23]  LPadX/Y, RPadX/Y      (i16 LE × 4)
        //   [24..29]  AccelX/Y/Z            (i16 LE × 3)
        //   [30..35]  Pitch/Yaw/Roll        (raw gyro X/Y/Z, i16 LE × 3)
        //   [36..43]  GyroQuatW/X/Y/Z       (i16 LE × 4, unused — we don't ship a quaternion)
        //   [44..47]  LTrigger, RTrigger    (u16 LE × 2; XInput byte is scaled ×257 → 0..65535)
        //   [48..55]  LStickX/Y, RStickX/Y  (i16 LE × 4, passthrough from XInput sticks)
        //   [56..63]  LPadForce, RPadForce, LStickForce, RStickForce (u16 LE × 4)
        //
        // Legion aux → SteamDeck back-button mapping mirrors the pattern we already
        // ship for BuildXboxElite2Input on the old Steam Deck profile (Y1→L4,
        // Y3→R4, Y2→L5, M3→R5) so users who relied on paddles via Steam Input on
        // the legacy emulation see no behavior change.
        private byte[] BuildSteamDeckInput(ViiperXInputGamepad gp)
        {
            var data = new byte[64];
            data[0] = 0x01;
            data[1] = 0x00;
            data[2] = 0x09;
            data[3] = 0x38;
            unchecked { steamDeckFrameCounter++; }
            WriteU32(data, 4, steamDeckFrameCounter);

            ushort aux = currentAuxButtons;

            // Byte 8 — face buttons, shoulders, digital triggers.
            byte b8 = 0;
            if ((gp.Buttons & ViiperXInput.A) != 0) b8 |= 0x80;
            if ((gp.Buttons & ViiperXInput.X) != 0) b8 |= 0x40;
            if ((gp.Buttons & ViiperXInput.B) != 0) b8 |= 0x20;
            if ((gp.Buttons & ViiperXInput.Y) != 0) b8 |= 0x10;
            if ((gp.Buttons & ViiperXInput.LB) != 0) b8 |= 0x08;
            if ((gp.Buttons & ViiperXInput.RB) != 0) b8 |= 0x04;
            if (gp.LeftTrigger > 30) b8 |= 0x02;                                // XInput trigger deadzone
            if (gp.RightTrigger > 30) b8 |= 0x01;
            data[8] = b8;

            // Byte 9 — system buttons + dpad (Steam button = Guide).
            byte b9 = 0;
            if ((aux & LegionAux.Y2) != 0) b9 |= 0x80;                          // L5 (back L lower)
            if ((gp.Buttons & ViiperXInput.Start) != 0) b9 |= 0x40;             // Menu
            if (((gp.Buttons & ViiperXInput.Guide) != 0)
                || ((aux & LegionAux.Mode) != 0)) b9 |= 0x20;                   // Steam (Mode → Steam)
            if ((gp.Buttons & ViiperXInput.Back) != 0) b9 |= 0x10;              // Options
            if ((gp.Buttons & ViiperXInput.DPadDown) != 0) b9 |= 0x08;
            if ((gp.Buttons & ViiperXInput.DPadLeft) != 0) b9 |= 0x04;
            if ((gp.Buttons & ViiperXInput.DPadRight) != 0) b9 |= 0x02;
            if ((gp.Buttons & ViiperXInput.DPadUp) != 0) b9 |= 0x01;
            data[9] = b9;

            // Byte 10 — L3, touchpad touches/presses, R5.
            byte b10 = 0;
            if ((gp.Buttons & ViiperXInput.LeftThumb) != 0) b10 |= 0x40;        // L3
            if ((aux & LegionAux.M3) != 0) b10 |= 0x01;                         // R5 (back R lower)
            if (currentTouchActive) b10 |= 0x10;                                // RPadTouch
            if (currentTouchPressed) b10 |= 0x04;                               // RPadPress
            data[10] = b10;

            // Byte 11 — R3.
            byte b11 = 0;
            if ((gp.Buttons & ViiperXInput.RightThumb) != 0) b11 |= 0x04;
            data[11] = b11;

            // Byte 13 — stick-cap touches + L4/R4 (upper paddles).
            byte b13 = 0;
            if ((aux & LegionAux.Y3) != 0) b13 |= 0x04;                         // R4 (back R upper)
            if ((aux & LegionAux.Y1) != 0) b13 |= 0x02;                         // L4 (back L upper)
            data[13] = b13;

            // Byte 14 — QuickAccess (Legion's Share button if present, else unused).
            if ((aux & LegionAux.Share) != 0) data[14] |= 0x04;

            // Touchpad coords (Legion has only the right touchpad). Left pad bytes
            // stay zero. Right pad maps to libviiper's steamdeck wire slots at
            // offsets 20-23 (RPadX/Y int16 LE).
            //
            // Y-axis sign: Steam Deck convention is +32767 = top of pad, -32768 =
            // bottom (joystick-style, Y-up). Legion's raw touchpad reports
            // screen-style coords with Y=0 at the top, Y=1023 at the bottom — so
            // an un-inverted (currentY - 512) * 64 produces NEGATIVE values when
            // the user touches the top of the pad, which Steam/SDL then interprets
            // as a downward swipe. Negate to convert screen-up → wire-up.
            //
            // Horizontal axis matches directly (both Legion and Steam Deck use
            // X=0..1023 left-to-right / -32768..+32767 left-to-right).
            if (currentTouchActive)
            {
                short padX = (short)(((int)currentTouchX - 512) * 64);
                short padY = (short)((512 - (int)currentTouchY) * 64);          // Y inverted: Legion 0=top → wire +32767
                WriteI16(data, 20, padX);
                WriteI16(data, 22, padY);
                if (currentTouchPressed)
                {
                    WriteU16(data, 58, (ushort)0x4000);                         // RPadForce midrange when pressed
                }
            }

            // IMU — accel at 24..29, raw gyro at 30..35 (the Pitch/Yaw/Roll slots).
            // Quaternion slots stay zero; Steam Input synthesizes orientation from
            // raw gyro when the quaternion is null. Same TryBuildImuCounts helper
            // the DS4/DSE/Elite2 builders use, so gyro behavior matches across
            // backends for the same Legion controller.
            short gx, gy, gz, ax, ay, az;
            if (TryBuildImuCounts(out gx, out gy, out gz, out ax, out ay, out az))
            {
                WriteI16(data, 24, ax);
                WriteI16(data, 26, ay);
                WriteI16(data, 28, az);
                WriteI16(data, 30, gx);
                WriteI16(data, 32, gy);
                WriteI16(data, 34, gz);
            }

            // Triggers — XInput is 0..255, steamdeck wire is u16. ×257 spans the
            // full 0..65535 range without rounding loss (255*257 == 65535).
            WriteU16(data, 44, (ushort)(gp.LeftTrigger * 257));
            WriteU16(data, 46, (ushort)(gp.RightTrigger * 257));

            // Sticks — XInput int16 -> wire int16 passthrough.
            WriteI16(data, 48, gp.ThumbLX);
            WriteI16(data, 50, gp.ThumbLY);
            WriteI16(data, 52, gp.ThumbRX);
            WriteI16(data, 54, gp.ThumbRY);

            return data;
        }

        // -------------------------------------------------------------------
        // Wire helpers (replacements for BitConverter.TryWriteBytes/Span which
        // aren't available on .NET Framework 4.8)
        // -------------------------------------------------------------------

        private static void WriteU16(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteI16(byte[] buf, int offset, short value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteU32(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static int NegateClamp(short value)
        {
            int neg = -(int)value;
            if (neg > short.MaxValue) neg = short.MaxValue;
            if (neg < short.MinValue) neg = short.MinValue;
            return neg;
        }
    }
}
