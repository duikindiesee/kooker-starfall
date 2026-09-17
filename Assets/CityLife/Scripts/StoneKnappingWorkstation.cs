using System;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Stone Knapping Workstation: Allows the autonomous inhabitant to perform percussion
    /// flaking with natural river cobbles and fieldstones to craft sharp flint blades and fire strikers.
    /// </summary>
    public sealed class StoneKnappingWorkstation : MonoBehaviour
    {
        [Header("References")]
        public NpcAutonomy Brain;
        public PhysicalItemBootstrap Bootstrap;
        public Transform AnvilPoint;
        public NpcInteractable Interactable;

        [Header("Crafting State")]
        public int TotalToolsCrafted = 0;
        public string LastCraftedToolId = "";
        public bool IsKnapping { get; private set; }

        private float knapCooldown = 0f;

        private void Awake()
        {
            if (Interactable == null)
                Interactable = GetComponent<NpcInteractable>();
            if (Interactable == null)
            {
                Interactable = gameObject.AddComponent<NpcInteractable>();
                Interactable.StableId = "knapping-workstation";
                Interactable.Kind = NpcObjectKind.Place;
                Interactable.Permission = true;
                Interactable.Approach = transform;
            }
        }

        private void Update()
        {
            if (knapCooldown > 0f)
                knapCooldown -= Time.deltaTime;
        }

        /// <summary>
        /// Checks if knapping can be performed with available natural stones.
        /// </summary>
        public bool CanKnap(out string reason)
        {
            if (knapCooldown > 0f)
            {
                reason = "cooldown-active";
                return false;
            }

            if (Bootstrap == null)
            {
                reason = "bootstrap-missing";
                return false;
            }

            reason = "ready";
            return true;
        }

        /// <summary>
        /// Executes a knapping strike sequence, producing a sharp stone blade or fire striker.
        /// </summary>
        public bool PerformKnap(out string producedToolId)
        {
            producedToolId = "";
            if (!CanKnap(out string reason))
                return false;

            knapCooldown = 3.0f;
            IsKnapping = true;

            // Alternate between sharp cutting blade and spark-producing fire striker
            producedToolId = (TotalToolsCrafted % 2 == 0) ? "tool-stone-blade" : "tool-fire-striker";
            LastCraftedToolId = producedToolId;
            TotalToolsCrafted++;

            Vector3 spawnPos = AnvilPoint != null ? AnvilPoint.position + Vector3.up * 0.15f : transform.position + Vector3.up * 0.2f;

            if (Bootstrap != null)
            {
                string uniqueId = $"knapped-{producedToolId}-{TotalToolsCrafted:D3}";
                var newItem = Bootstrap.CreatePhysicalItem(uniqueId, producedToolId, spawnPos, transform.rotation);
                if (newItem != null)
                {
                    var rb = newItem.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.linearVelocity = new Vector3(UnityEngine.Random.Range(-0.2f, 0.2f), 0.5f, UnityEngine.Random.Range(-0.2f, 0.2f));
                    }
                }
            }

            if (Brain != null && Brain.Log != null)
            {
                Brain.Log.Record(
                    Brain.Tick,
                    "crafting",
                    "stone-percussion-flaking",
                    producedToolId,
                    "knap-tool",
                    $"crafted {producedToolId} (total: {TotalToolsCrafted})",
                    "tool ready for foraging and firekeeping"
                );
            }

            IsKnapping = false;
            return true;
        }

        /// <summary>
        /// Verification entry point to validate knapping logic without scene dependencies.
        /// </summary>
        public static bool VerifyKnappingLogic(out string verificationReceipt)
        {
            var go = new GameObject("TestKnapping");
            var ws = go.AddComponent<StoneKnappingWorkstation>();
            bool canKnapInit = ws.CanKnap(out string reason);
            bool knapFailedNoBootstrap = !ws.PerformKnap(out _);
            UnityEngine.Object.DestroyImmediate(go);

            verificationReceipt = $"knapInit={canKnapInit}, noBootstrapRejected={knapFailedNoBootstrap}, reason={reason}";
            return canKnapInit && knapFailedNoBootstrap;
        }
    }
}
