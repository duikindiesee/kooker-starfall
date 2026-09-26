using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Adds restrained, natural wind sway to riparian sedges, reeds, and grass clumps.
    /// Uses global Starfall wind vector and varied phase offsets for organic desynchronization.
    /// </summary>
    public sealed class RiverbankFoliageWind : MonoBehaviour
    {
        public float SwayDegrees = 2.2f;
        public float WindFrequency = 1.8f;
        public float PhaseOffset = 0f;

        private Quaternion baseRotation;
        private Vector3 basePosition;

        private void Start()
        {
            baseRotation = transform.localRotation;
            basePosition = transform.localPosition;
            if (PhaseOffset == 0f)
            {
                // Unique deterministic phase based on world position
                Vector3 p = transform.position;
                PhaseOffset = Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.5453f % (Mathf.PI * 2f);
            }
        }

        private void Update()
        {
            Vector3 wind = Shader.GetGlobalVector("_StarfallWind");
            float windSpeed = wind.magnitude;
            if (windSpeed < 0.01f) windSpeed = 1.0f; // Default gentle breeze

            float time = Time.time * WindFrequency + PhaseOffset;
            // Dual-frequency organic sway
            float primarySway = Mathf.Sin(time) * 0.72f;
            float secondaryGust = Mathf.Sin(time * 2.13f + 1.2f) * 0.28f;
            float totalSway = (primarySway + secondaryGust) * SwayDegrees * Mathf.Clamp(windSpeed * 0.8f, 0.4f, 1.8f);

            // Subtle angular tilt primarily in wind direction plus cross-wind flutter
            Vector3 swayAxis = wind.magnitude > 0.1f 
                ? Vector3.Cross(Vector3.up, wind.normalized) 
                : Vector3.forward;

            transform.localRotation = baseRotation * Quaternion.AngleAxis(totalSway, swayAxis) * Quaternion.AngleAxis(secondaryGust * 1.2f, Vector3.right);
        }
    }
}
