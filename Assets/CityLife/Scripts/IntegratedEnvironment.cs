using UnityEngine;
using Starfall.EnvironmentFoundation;
using Starfall.EnvironmentZones;

namespace CityLife.World
{
    public sealed class IntegratedEnvironment : MonoBehaviour
    {
        public NpcAutonomy Brain;
        public NpcTerrainNavigation Surface;
        public Camera View;
        public Light Sun;
        public ParticleSystem Rain;
        public EnvironmentClock Clock = new EnvironmentClock(1904243);
        public ZoneExposure Exposure;
        public ZoneWeather LocalWeather;
        public CaveZonePolicy ShelterPolicy;
        public string Weather => Clock.Sample.target.ToString();
        public float DayDurationSeconds = 480f; // 8 minutes per full 24h cycle
        public float TimeOfDayNormalized { get; private set; } // 0.0 to 1.0
        public bool IsNight { get; private set; }
        private Color sunColor;
        private Starfall.Refuge.RefugeRuntime refuge;
        public OutdoorWeather OutdoorSample()
        {
            var s = Clock.Sample;
            return new OutdoorWeather { WindX=s.wind.x, WindY=s.wind.y, WindZ=s.wind.z,
                AirC=s.temperature, Rain01=s.precipitation };
        }
        private void Start() { sunColor = Sun.color; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1; Shader.SetGlobalFloat("_StarfallIntegratedWeather", 1); ShelterPolicy = new CaveZonePolicy(Surface.WorldId, Surface.Revision, "first-refuge"); }
        public ZoneWeather SampleAt(Vector3 position)
        {
            if (refuge == null && View != null) refuge = View.GetComponent<Starfall.Refuge.RefugeRuntime>();
            if (refuge != null && refuge.IntegratedMode)
                return refuge.Sample(position, OutdoorSample());
            // No authored adapter means no shelter credit at the retired fixture coordinates.
            var probe = new CaveProbe { WorldId = Surface.WorldId, WorldRevision = Surface.Revision, ZoneId = "first-refuge",
                GeometryVerified = false, FloorKnown = false, IngressKnown = false,
                WaterBoundKnown = false, ThermalVerified = false };
            return CaveZoneEvaluator.Evaluate(ShelterPolicy ?? new CaveZonePolicy(Surface.WorldId, Surface.Revision, "first-refuge"), OutdoorSample(), probe);
        }
        private void FixedUpdate()
        {
            Clock.Paused = Brain.MenuPaused;
            if (Clock.Paused) return;
            Clock.Step(); LocalWeather = SampleAt(Brain.transform.position + Vector3.up);
            if (refuge != null && refuge.IntegratedMode) refuge.StepIntegrated(OutdoorSample());
            Exposure.Step(LocalWeather, false);
        }
        private void Update()
        {
            var s = Clock.Sample;
            float envTime = Clock.Tick * EnvironmentClock.Dt;
            Shader.SetGlobalVector("_StarfallWind", s.wind);
            Shader.SetGlobalFloat("_StarfallEnvironmentTime", envTime);

            // Diurnal Day/Night Cycle progression
            TimeOfDayNormalized = (envTime % DayDurationSeconds) / DayDurationSeconds;
            // 0.0 = sunrise (06:00), 0.25 = noon (12:00), 0.5 = sunset (18:00), 0.75 = midnight (00:00)
            float sunPitch = Mathf.Sin(TimeOfDayNormalized * Mathf.PI * 2f);
            float sunAngle = TimeOfDayNormalized * 360f - 90f;
            IsNight = sunPitch < 0f;

            if (Sun != null)
            {
                Sun.transform.rotation = Quaternion.Euler(sunAngle, -35f, 0f);

                if (!IsNight)
                {
                    // Daytime & Twilight
                    float dayIntensity = Mathf.Clamp01(sunPitch * 1.4f);
                    float stormDim = Mathf.Lerp(2.0f, 0.75f, s.precipitation);
                    Sun.intensity = Mathf.Max(0.2f, dayIntensity * stormDim);

                    // Morning/Evening golden amber vs Midday white vs Storm cold
                    Color daylightColor = sunPitch < 0.25f 
                        ? Color.Lerp(new Color(1.0f, 0.62f, 0.35f), sunColor, sunPitch * 4f)
                        : sunColor;
                    Sun.color = Color.Lerp(daylightColor, new Color(0.65f, 0.76f, 1.0f), s.precipitation * 0.45f);
                }
                else
                {
                    // Nighttime: dim celestial moonlight from the blue gas giant & moons
                    Sun.intensity = 0.15f;
                    Sun.color = new Color(0.42f, 0.58f, 0.85f);
                }
            }

            float baseFog = IsNight ? 0.0020f : 0.0008f;
            RenderSettings.fogDensity = Mathf.Lerp(baseFog, 0.0045f, s.precipitation);
            if (Rain != null)
            {
                Rain.transform.position = View.transform.position + Vector3.up * 9;
                var emission = Rain.emission; emission.rateOverTime = SampleAt(View.transform.position - Vector3.up * 1.5f).Rain01 * 180;
                var velocity = Rain.velocityOverLifetime; velocity.x = s.wind.x * .3f; velocity.z = s.wind.z * .3f;
            }
        }
    }
}
