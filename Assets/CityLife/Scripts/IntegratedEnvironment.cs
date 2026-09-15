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
