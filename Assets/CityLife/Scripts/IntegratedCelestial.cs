using UnityEngine;

namespace CityLife.World
{
    // Slow visual ephemeris driven by the regional simulation clock, not a gravity model.
    public sealed class IntegratedCelestial : MonoBehaviour
    {
        // Round218 matched dry-bank Editor comparison raises the giant above
        // the sea-stack tangent while retaining immense angular diameter and
        // complete sphere far beyond terrain. Player traversal/parallax and
        // user visual acceptance remain separate.
        // Keep the lower limb well above the sea horizon in a normal canyon
        // heading, so cliffs silhouette it as sky rather than a valley globe.
        // Greater distance to the raised centre is offset by diameter: about
        // the same immense apparent angular size as the prior study.
        public static readonly Vector3 GiantPosition = new Vector3(9000, 26000, 42000);
        public const float GiantScale = 36000;
        public const float SkyFarClip = 80000;
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
