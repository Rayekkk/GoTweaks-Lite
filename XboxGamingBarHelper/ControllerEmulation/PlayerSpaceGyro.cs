using System;
using System.Runtime.InteropServices;
using NLog;

namespace XboxGamingBarHelper.ControllerEmulation
{
    /// <summary>
    /// P/Invoke wrapper around Jibb Smart's GamepadMotionHelpers (MIT-licensed,
    /// vendored at <c>Native/GamepadMotion/GamepadMotion.hpp</c> and built into
    /// <c>GamepadMotion.dll</c> via a tiny C export shim). This is the same
    /// library JoyShockMapper and BetterJoy use for gyro-aim,
    /// so the projection we get is the battle-tested fusion-based one rather
    /// than a naive low-pass on the accelerometer.
    ///
    /// One instance per stick-gyro pipeline. <see cref="Update"/> every tick
    /// with the device's raw gyro+accel sample; the library maintains gravity
    /// and orientation internally via complementary filtering. Then call
    /// <see cref="GetPlayerSpaceGyro"/> to read the projected (horizontal,
    /// vertical) pair in deg/s, ready to drop into the rest of the pipeline.
    /// </summary>
    internal sealed class GamepadMotion : IDisposable
    {
        private const string DllName = "GamepadMotion.dll";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private IntPtr handle;
        private bool disposed;

        // JSL CalibrationMode flags (matches the enum in GamepadMotion.hpp:150-155).
        // Hybrid model: default to Stillness|SensorFusion so a fresh install
        // (no saved offset) works out of the box — JSL learns bias whenever the
        // device is briefly still. When the user runs the explicit Calibrate
        // Gyro flow we capture the learned offset, persist it, and switch to
        // Manual mode after capture. On next startup
        // the persisted offset is applied via SetCalibrationOffset and we stay
        // in Manual mode (set by the LoadJslCalibrationOffset path) so slow aim
        // doesn't get re-learned as bias.
        public const int JslCalibrationManual = 0;
        public const int JslCalibrationStillnessFusion = 3;

        public GamepadMotion()
        {
            try
            {
                handle = CreateGamepadMotion();
                SetCalibrationMode(handle, JslCalibrationStillnessFusion);
            }
            catch (DllNotFoundException ex)
            {
                Logger.Error($"GamepadMotion: native DLL load failed ({ex.Message}). " +
                             "Player Space gyro will fall back to raw input.");
                handle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Set the active calibration mode. <see cref="JslCalibrationManual"/> for
        /// normal use; <see cref="JslCalibrationStillnessFusion"/> while running
        /// the user-triggered calibration capture.
        /// </summary>
        public void SetCalibrationMode(int mode)
        {
            if (handle == IntPtr.Zero) return;
            SetCalibrationMode(handle, mode);
        }

        /// <summary>Read JSL's currently-applied gyro bias offset.</summary>
        public void GetCalibrationOffset(out float xOffset, out float yOffset, out float zOffset)
        {
            if (handle == IntPtr.Zero) { xOffset = 0; yOffset = 0; zOffset = 0; return; }
            GetCalibrationOffset(handle, out xOffset, out yOffset, out zOffset);
        }

        /// <summary>Apply a gyro bias offset (subtracted on every sample).</summary>
        public void SetCalibrationOffset(float xOffset, float yOffset, float zOffset, int weight)
        {
            if (handle == IntPtr.Zero) return;
            SetCalibrationOffset(handle, xOffset, yOffset, zOffset, weight);
        }

        /// <summary>Reset JSL's continuous-calibration accumulator.</summary>
        public void ResetContinuousCalibration()
        {
            if (handle == IntPtr.Zero) return;
            ResetContinuousCalibration(handle);
        }

        /// <summary>0–1 confidence score for JSL's auto-calibration (1.0 = converged).</summary>
        public float GetAutoCalibrationConfidence()
        {
            return handle == IntPtr.Zero ? 0.0f : GetAutoCalibrationConfidence(handle);
        }

        public bool IsAvailable => handle != IntPtr.Zero;

        public void Reset()
        {
            if (handle == IntPtr.Zero) return;
            ResetGamepadMotion(handle);
        }

        /// <summary>
        /// Push a new IMU sample. Call every tick (or whenever a fresh sample
        /// arrives). <paramref name="deltaSeconds"/> is the time since the last
        /// call — the fusion's accuracy depends on this being roughly correct.
        /// Gyro is in deg/s, accel in g, both in the device's IMU frame.
        /// </summary>
        public void Update(float gyroX, float gyroY, float gyroZ,
                           float accelX, float accelY, float accelZ,
                           float deltaSeconds)
        {
            if (handle == IntPtr.Zero) return;
            ProcessMotion(handle, gyroX, gyroY, gyroZ, accelX, accelY, accelZ, deltaSeconds);
        }

        /// <summary>
        /// Player-space projection of the most recent sample. Output is in
        /// deg/s in the same units as the gyro inputs. Falls back to raw
        /// (gyroY, gyroX) — i.e. legacy "Yaw" mode — if the native library
        /// failed to load.
        /// </summary>
        /// <remarks>
        /// JSL's native signature returns (x=pitch, y=worldYaw) — opposite
        /// orientation from our pipeline, which expects (horizontal=yaw,
        /// vertical=pitch). We swap, AND we negate the yaw output:
        /// <c>new Vector2(-playerY, playerX)</c>. Without that
        /// negation the horizontal direction comes out reversed (yaw right →
        /// camera looks left), which the user had to compensate for via the
        /// Invert-X toggle. Folding it into the wrapper keeps the toggle for
        /// in-game preference where it belongs.
        /// </remarks>
        public void GetPlayerSpaceGyro(out float horizontal, out float vertical,
                                       float gyroY, float gyroX,
                                       float yawRelaxFactor = DefaultYawRelaxFactor)
        {
            if (handle == IntPtr.Zero)
            {
                horizontal = gyroY;
                vertical = gyroX;
                return;
            }
            GetPlayerSpaceGyro(handle, out float jslPitch, out float jslYaw, yawRelaxFactor);
            horizontal = -jslYaw;
            vertical = jslPitch;
        }

        /// <summary>
        /// World-space projection — pitch axis projected onto the gravity-perpendicular
        /// plane. Better than player space when the device is held at extreme
        /// angles, but introduces a "side reduction" zone at near-vertical hold
        /// (controlled by <paramref name="sideReductionThreshold"/>).
        /// </summary>
        /// <remarks>
        /// Same axis swap + yaw negation as <see cref="GetPlayerSpaceGyro"/>:
        /// JSL returns (x=pitch, y=yaw); we return (horizontal=-yaw, vertical=pitch).
        /// </remarks>
        public void GetWorldSpaceGyro(out float horizontal, out float vertical,
                                      float gyroY, float gyroX,
                                      float sideReductionThreshold = DefaultSideReductionThreshold)
        {
            if (handle == IntPtr.Zero)
            {
                horizontal = gyroY;
                vertical = gyroX;
                return;
            }
            GetWorldSpaceGyro(handle, out float jslPitch, out float jslYaw, sideReductionThreshold);
            horizontal = -jslYaw;
            vertical = jslPitch;
        }

        public void GetGravity(out float x, out float y, out float z)
        {
            if (handle == IntPtr.Zero) { x = 0; y = -1; z = 0; return; }
            GetGravity(handle, out x, out y, out z);
        }

        /// <summary>
        /// Most recent gyro reading after JSL's calibration offset has been
        /// subtracted. Same source the gravity-projected modes pull from
        /// internally — using this for Mode 0/1/2 puts every conversion mode
        /// on the same input feed.
        /// </summary>
        public void GetCalibratedGyro(out float x, out float y, out float z)
        {
            if (handle == IntPtr.Zero) { x = 0; y = 0; z = 0; return; }
            GetCalibratedGyro(handle, out x, out y, out z);
        }

        public void StartContinuousCalibration()
        {
            if (handle == IntPtr.Zero) return;
            StartContinuousCalibration(handle);
        }

        public void PauseContinuousCalibration()
        {
            if (handle == IntPtr.Zero) return;
            PauseContinuousCalibration(handle);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (handle != IntPtr.Zero)
            {
                DeleteGamepadMotion(handle);
                handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~GamepadMotion() { Dispose(); }

        // --------------------------------------------------------------------
        // Tunable defaults
        // --------------------------------------------------------------------

        // sqrt(2). Same default JSL ships with — gives a slight responsiveness
        // boost while staying bounded by physical (Y,Z) magnitude.
        public const float DefaultYawRelaxFactor = 1.41f;

        // Above this dot-product magnitude the world-space projection eases out
        // toward zero so a near-vertical hold doesn't produce huge swings.
        public const float DefaultSideReductionThreshold = 0.125f;

        // --------------------------------------------------------------------
        // Native imports (signatures mirror the C exports in
        // Native/GamepadMotion/GamepadMotionExports.cpp)
        // --------------------------------------------------------------------

        [DllImport(DllName)] private static extern IntPtr CreateGamepadMotion();
        [DllImport(DllName)] private static extern void DeleteGamepadMotion(IntPtr motion);
        [DllImport(DllName)] private static extern void ResetGamepadMotion(IntPtr motion);

        [DllImport(DllName)]
        private static extern void ProcessMotion(IntPtr motion,
            float gyroX, float gyroY, float gyroZ,
            float accelX, float accelY, float accelZ,
            float deltaTime);

        [DllImport(DllName)]
        private static extern void GetGravity(IntPtr motion, out float x, out float y, out float z);

        [DllImport(DllName)]
        private static extern void GetCalibratedGyro(IntPtr motion, out float x, out float y, out float z);

        [DllImport(DllName)]
        private static extern void GetPlayerSpaceGyro(IntPtr motion, out float x, out float y, float yawRelaxFactor);

        [DllImport(DllName)]
        private static extern void GetWorldSpaceGyro(IntPtr motion, out float x, out float y, float sideReductionThreshold);

        [DllImport(DllName)]
        private static extern void StartContinuousCalibration(IntPtr motion);

        [DllImport(DllName)]
        private static extern void PauseContinuousCalibration(IntPtr motion);

        [DllImport(DllName)]
        private static extern void SetCalibrationMode(IntPtr motion, int calibrationMode);

        [DllImport(DllName)]
        private static extern void GetCalibrationOffset(IntPtr motion, out float xOffset, out float yOffset, out float zOffset);

        [DllImport(DllName)]
        private static extern void SetCalibrationOffset(IntPtr motion, float xOffset, float yOffset, float zOffset, int weight);

        [DllImport(DllName)]
        private static extern void ResetContinuousCalibration(IntPtr motion);

        [DllImport(DllName)]
        private static extern float GetAutoCalibrationConfidence(IntPtr motion);
    }
}
