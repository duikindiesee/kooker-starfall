using UnityEngine;
using Starfall.EnvironmentFoundation;

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
        public ExposureState Exposure = new ExposureState();
        public string Weather => Clock.Sample.target.ToString();
        private Color sunColor;
        private void Start() { sunColor = Sun.color; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1; Shader.SetGlobalFloat("_StarfallIntegratedWeather", 1); }
        private void FixedUpdate()
        {
            Clock.Paused = Brain.MenuPaused;
            if (Clock.Paused) return;
            Clock.Step(); var s = Clock.Sample;
            bool shelter = Physics.Raycast(Brain.transform.position + Vector3.up * 1.9f, Vector3.up, 8, 1 << 8);
            Exposure.Step(s.temperature, s.wind.magnitude, s.precipitation, shelter);
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
                var emission = Rain.emission; emission.rateOverTime = s.precipitation * 180;
                var velocity = Rain.velocityOverLifetime; velocity.x = s.wind.x * .3f; velocity.z = s.wind.z * .3f;
            }
        }
        private void OnGUI()
        {
            if (Brain.MenuPaused) return;
            var controls = View.GetComponent<NpcPlayerControls>();
            GUI.Box(new Rect(Screen.width - 390, 12, 378, 84), "");
            GUI.Label(new Rect(Screen.width - 378, 18, 355, 75),
                controls.Mode + "\n" + (controls.Looking ? "Mouse captured / Escape releases and pauses" : "Click or right-click in the world to look") +
                "\nTab possess / F spectator / P options / F11 display\nSensitivity: P > Controls > Mouse look");
            GUI.Box(new Rect(Screen.width - 390, Screen.height - 115, 378, 103), "");
            GUI.Label(new Rect(Screen.width - 378, Screen.height - 109, 355, 97),
                "STARFALL / REGIONAL CANDIDATE " + Application.version +
                "\n" + Weather + " | wind " + Clock.Sample.wind.magnitude.ToString("F1") + " m/s | " + Clock.Sample.temperature.ToString("F0") + " C" +
                "\nInhabitant wetness " + Exposure.Wetness.ToString("P0") + " | " + (Exposure.Cold ? "cold exposure" : "comfortable") +
                "\nRegional slice; swimming, boats and full saves pending");
        }
    }
}
