using System;

namespace Shared.Data
{
    /// <summary>
    /// Pure-math stick / trigger transformer. Stateless — the caller
    /// supplies the config per call. Inspired by PadForge's per-axis
    /// shaping pipeline. Output preserves the XInput value ranges:
    /// sticks are short (-32768..32767), triggers are byte (0..255).
    ///
    /// <para>Lives in Shared so both the helper (real shaping in
    /// ViiperInputForwarder) and the widget (live preview render in
    /// the Sticks &amp; Triggers panel) run identical math against the
    /// same StickTriggerConfigBundle.</para>
    /// </summary>
    public static class StickTriggerProcessor
    {
        /// <summary>
        /// Applies deadzone shape → sensitivity curve → anti-deadzone
        /// to a raw stick input. Math runs in -1..1 space then
        /// re-quantizes to the short range on output.
        /// </summary>
        public static void TransformStick(short rawX, short rawY, in StickConfig cfg, out short outX, out short outY)
        {
            // Normalize to -1..1 (32768 divisor maps -short.MinValue exactly to -1.0).
            float x = rawX / 32768f;
            float y = rawY / 32768f;

            // --- Deadzone shape ---
            switch (cfg.Shape)
            {
                case DeadzoneShape.ScaledRadial:
                {
                    float dz = Math.Max(cfg.DeadzoneX, cfg.DeadzoneY);
                    float mag = (float)Math.Sqrt(x * x + y * y);
                    if (mag <= dz || mag <= 1e-6f)
                    {
                        x = 0f; y = 0f;
                    }
                    else
                    {
                        float scale = (mag - dz) / ((1f - dz) * mag);
                        x *= scale;
                        y *= scale;
                    }
                    break;
                }
                case DeadzoneShape.Radial:
                {
                    float dz = Math.Max(cfg.DeadzoneX, cfg.DeadzoneY);
                    float mag = (float)Math.Sqrt(x * x + y * y);
                    if (mag <= dz) { x = 0f; y = 0f; }
                    break;
                }
                case DeadzoneShape.Axial:
                {
                    if (Math.Abs(x) < cfg.DeadzoneX)
                    {
                        x = 0f;
                    }
                    else
                    {
                        x = Math.Sign(x) * (Math.Abs(x) - cfg.DeadzoneX) / Math.Max(1e-6f, 1f - cfg.DeadzoneX);
                    }
                    if (Math.Abs(y) < cfg.DeadzoneY)
                    {
                        y = 0f;
                    }
                    else
                    {
                        y = Math.Sign(y) * (Math.Abs(y) - cfg.DeadzoneY) / Math.Max(1e-6f, 1f - cfg.DeadzoneY);
                    }
                    break;
                }
            }

            // --- Sensitivity curve per axis ---
            x = ApplyCurve(x, cfg.CurveX);
            y = ApplyCurve(y, cfg.CurveY);

            // --- Anti-deadzone (minimum output magnitude when input is non-zero) ---
            if (x != 0f && cfg.AntiDeadzoneX > 0f)
            {
                x = Math.Sign(x) * (cfg.AntiDeadzoneX + (1f - cfg.AntiDeadzoneX) * Math.Abs(x));
            }
            if (y != 0f && cfg.AntiDeadzoneY > 0f)
            {
                y = Math.Sign(y) * (cfg.AntiDeadzoneY + (1f - cfg.AntiDeadzoneY) * Math.Abs(y));
            }

            // --- Clamp + denormalize ---
            if (x < -1f) x = -1f; else if (x > 1f) x = 1f;
            if (y < -1f) y = -1f; else if (y > 1f) y = 1f;
            outX = (short)(x * 32767f);
            outY = (short)(y * 32767f);
        }

        /// <summary>
        /// Applies deadzone start → curve → anti-deadzone → range cap to a
        /// raw trigger input. Math runs in 0..1 space, output in 0..255.
        /// </summary>
        public static byte TransformTrigger(byte raw, in TriggerConfig cfg)
        {
            float v = raw / 255f;
            if (v <= cfg.DeadzoneStart) return 0;
            v = (v - cfg.DeadzoneStart) / Math.Max(1e-6f, 1f - cfg.DeadzoneStart);
            v = ApplyCurve(v, cfg.Curve);
            if (cfg.AntiDeadzone > 0f)
            {
                v = cfg.AntiDeadzone + (1f - cfg.AntiDeadzone) * v;
            }
            v = v * cfg.RangeMax;
            if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
            return (byte)(v * 255f);
        }

        private static float ApplyCurve(float x, SensitivityCurve curve)
        {
            if (x == 0f) return 0f;
            float abs = Math.Abs(x);
            float sign = Math.Sign(x);
            float curved;
            switch (curve)
            {
                case SensitivityCurve.Linear:     curved = abs; break;
                case SensitivityCurve.Smooth:     curved = abs * abs; break;
                case SensitivityCurve.Aggressive: curved = (float)Math.Sqrt(abs); break;
                case SensitivityCurve.Instant:    curved = abs > 0.02f ? 1f : 0f; break;
                case SensitivityCurve.SCurve:     curved = abs * abs * (3f - 2f * abs); break;
                case SensitivityCurve.Delay:      curved = abs < 0.3f ? 0f : (abs - 0.3f) / 0.7f; break;
                default:                          curved = abs; break;
            }
            return sign * curved;
        }
    }
}
