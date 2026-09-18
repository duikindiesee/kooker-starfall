using System;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Simulates semi-diurnal coastal delta tides, modulating water levels and driving
    /// tidal mudflat exposure, crab migrations, and driftwood beaching.
    /// </summary>
    public sealed class CoastalTide : MonoBehaviour
    {
        public const float CycleDurationSeconds = 240f; // 4 minutes per complete tidal cycle
        public const float AmplitudeMetres = 0.45f;     // ±0.45m tidal range

        // Optional manual test override; when negative, uses Time.time
        public static float SimulatedTimeOverride = -1f;

        public static float ActiveTime => SimulatedTimeOverride >= 0f ? SimulatedTimeOverride : (Application.isPlaying ? Time.time : 0f);

        public static float CurrentOffset => EvaluateTide(ActiveTime);

        public static float EvaluateTide(float time)
        {
            float phase = (time % CycleDurationSeconds) / CycleDurationSeconds;
            return Mathf.Sin(phase * Mathf.PI * 2f) * AmplitudeMetres;
        }

        public static float EvaluateTideRate(float time)
        {
            float phase = (time % CycleDurationSeconds) / CycleDurationSeconds;
            return Mathf.Cos(phase * Mathf.PI * 2f) * (Mathf.PI * 2f / CycleDurationSeconds) * AmplitudeMetres;
        }

        public static string GetTidePhaseName(float time)
        {
            float offset = EvaluateTide(time);
            float rate = EvaluateTideRate(time);

            if (offset > AmplitudeMetres * 0.7f) return "Peak High Tide";
            if (offset < -AmplitudeMetres * 0.7f) return "Slack Low Tide";
            return rate > 0f ? "Flooding (Rising Tide)" : "Ebbing (Falling Tide)";
        }

        public static bool IsLowTide(float time) => EvaluateTide(time) <= -AmplitudeMetres * 0.4f;
        public static bool IsHighTide(float time) => EvaluateTide(time) >= AmplitudeMetres * 0.4f;

        private void Update()
        {
            // Modulate the attached water surface height
            Vector3 pos = transform.position;
            pos.y = CoastalWater.Level + CurrentOffset;
            transform.position = pos;
        }
    }
}
