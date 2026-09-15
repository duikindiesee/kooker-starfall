using UnityEngine;

namespace CityLife.World
{
    // Slow visual ephemeris driven by the regional simulation clock, not a gravity model.
    public sealed class IntegratedCelestial : MonoBehaviour
    {
        // Preserve the original study's large angular diameter, but move the
        // complete sphere far beyond the explorable terrain, not into the canyon.
        public static readonly Vector3 GiantPosition = new Vector3(4500, 4200, 21000);
        public const float GiantScale = 9800;
        public const float SkyFarClip = 40000;
        public static void PlaceDistantGiant(Transform giant, Camera view)
        {
            giant.position = GiantPosition;
            giant.localScale = Vector3.one * GiantScale;
            view.farClipPlane = SkyFarClip;
        }
        public IntegratedEnvironment Environment;
        public Transform Giant;
        public Transform[] Moons;
        private Vector3[] starts;
        private void Start() { starts = new Vector3[Moons.Length]; for (int i = 0; i < Moons.Length; i++) starts[i] = Moons[i].position; }
        private void Update()
        {
            float seconds = Environment.Clock.Tick * .02f;
            Giant.rotation = Quaternion.Euler(0, seconds * .003f, -18);
            for (int i = 0; i < Moons.Length; i++)
                Moons[i].position = Quaternion.Euler(0, Mathf.Sin(seconds / (1800 + i * 400)) * .4f, 0) * starts[i];
        }
    }
}
