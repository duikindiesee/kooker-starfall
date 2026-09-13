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
        private Color sunColor;
        private void Start() { sunColor = Sun.color; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1; Shader.SetGlobalFloat("_StarfallIntegratedWeather", 1); ShelterPolicy = new CaveZonePolicy(Surface.WorldId, Surface.Revision, "first-refuge"); }
        public ZoneWeather SampleAt(Vector3 position)
        {
            var s = Clock.Sample;
            var outside = new OutdoorWeather { WindX = s.wind.x, WindY = s.wind.y, WindZ = s.wind.z, AirC = s.temperature, Rain01 = s.precipitation };
            bool inside = position.x < -6 && position.x > -13 && Mathf.Abs(position.z) < 2 && position.y > -.1f && position.y < 3;
            var origin = position + Vector3.up * 1.5f;
            bool roof = Physics.Raycast(origin, Vector3.up, 5, 1 << 8, QueryTriggerInteraction.Ignore);
            bool wind = Physics.Raycast(origin, -s.wind.normalized, 12, 1 << 8, QueryTriggerInteraction.Ignore);
            var rainDirection = (Vector3.up * 13 - s.wind * .3f).normalized;
            bool rain = Physics.Raycast(origin, rainDirection, 12, 1 << 8, QueryTriggerInteraction.Ignore);
            bool floor = Surface.TryGround(position, out float h, out _);
            bool ingress = Surface.TryGround(new Vector3(-6.5f, 0, 0), out float entry, out _);
            var probe = new CaveProbe { WorldId = Surface.WorldId, WorldRevision = Surface.Revision, ZoneId = "first-refuge",
                MetresInside = inside ? -6 - position.x : 0, GeometryVerified = inside && roof && floor,
                WindOcclusion01 = wind ? 1 : 0, RainOcclusion01 = rain ? 1 : 0,
                FloorKnown = false, IngressKnown = false, LowestRefugeFloorY = h, LowestConnectedIngressY = entry,
                WaterBoundKnown = true, MaximumDesignWaterY = CoastalWater.Level + .111f,
                ThermalVerified = false };
            // The maximum applies only to this fixed-datum regional shader, whose vertex strength is saturated.
            // A point probe is not a completed survey of every ingress; thermal refuge acceptance remains false.
            return CaveZoneEvaluator.Evaluate(ShelterPolicy ?? new CaveZonePolicy(Surface.WorldId, Surface.Revision, "first-refuge"), outside, probe);
        }
        private void FixedUpdate()
        {
            Clock.Paused = Brain.MenuPaused;
            if (Clock.Paused) return;
            Clock.Step(); LocalWeather = SampleAt(Brain.transform.position);
            Exposure.Step(LocalWeather, false);
        }
        private void Update()
        {
            var s = Clock.Sample;
            Shader.SetGlobalVector("_StarfallWind", s.wind);
            Shader.SetGlobalFloat("_StarfallEnvironmentTime", Clock.Tick * EnvironmentClock.Dt);
            Sun.intensity = Mathf.Lerp(2, .75f, s.precipitation);
            Sun.color = Color.Lerp(sunColor, new Color(.65f, .76f, 1), s.precipitation * .45f);
            RenderSettings.fogDensity = Mathf.Lerp(.0008f, .004f, s.precipitation);
            if (Rain != null)
            {
                Rain.transform.position = View.transform.position + Vector3.up * 9;
                var emission = Rain.emission; emission.rateOverTime = SampleAt(View.transform.position - Vector3.up * 1.5f).Rain01 * 180;
                var velocity = Rain.velocityOverLifetime; velocity.x = s.wind.x * .3f; velocity.z = s.wind.z * .3f;
            }
        }
        private void OnGUI()
        {
            if (Brain.MenuPaused) return;
            var controls = View.GetComponent<NpcPlayerControls>();
            var textStyle = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            float panelWidth = Mathf.Min(570, Screen.width * .48f);
            float panelLeft = Screen.width - panelWidth - 12;
            GUI.Box(new Rect(panelLeft, 12, panelWidth, 154), "");
            GUI.Label(new Rect(panelLeft + 12, 18, panelWidth - 24, 108),
                controls.Mode + "\n" + (!Application.isFocused ? "Click this window to focus controls" : controls.Looking ? "Mouse captured / Escape releases and pauses" : "Click or right-click in the world to look") +
                "\nTab: possess | F: spectator | P: options | F11: display\nSensitivity: P > Controls > Mouse look", textStyle);
            if (GUI.Button(new Rect(panelLeft + 12, 130, panelWidth - 24, 30), "Options", new GUIStyle(GUI.skin.button) { fontSize = 22 })) controls.OpenMenu();
            GUI.Box(new Rect(panelLeft, Screen.height - 140, panelWidth, 128), "");
            GUI.Label(new Rect(panelLeft + 12, Screen.height - 134, panelWidth - 24, 116),
                "STARFALL / Coastal preview" +
                "\n" + Weather + " | wind " + Clock.Sample.wind.magnitude.ToString("F1") + " m/s | " + Clock.Sample.temperature.ToString("F0") + " C" +
                "\nInhabitant wetness " + Exposure.Wetness01.ToString("P0") + " | " + (Exposure.Cold ? "cold exposure" : "comfortable") +
                "\nSwimming, boats and full saves: planned", textStyle);
        }
    }
}
