using UnityEngine;

namespace CityLife.World
{
    /// <summary>Enables the authored coastal realtime reflection tier in the baked player.</summary>
    [RequireComponent(typeof(ReflectionProbe))]
    public sealed class CoastalReflectionRuntime : MonoBehaviour
    {
        private void Awake()
        {
            // The project-wide default remains off. Only a scene that explicitly carries
            // this component opts into the bounded 128px OnAwake coastal probe.
            QualitySettings.realtimeReflectionProbes = true;
        }
    }
}
