using System.Collections;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    // Additive fishing-supplies migration. Stable identity and tombstones prevent
    // duplication or resurrection; existing container poses remain authoritative.
    public sealed class FishingCampSupplies : MonoBehaviour
    {
        public const string BasketId = "fishing-camp-basket-v1";
        public NpcAutonomy Brain;
        public string Receipt { get; private set; } = "waiting-for-world";
        public bool Ready { get; private set; }
        private IEnumerator Start()
        {
            if (Brain == null) Brain = GetComponent<NpcAutonomy>();
            for (int i = 0; i < 2400; i++)
            {
                if (Brain != null && Brain.Ready && Brain.Survival != null && Brain.Survival.Enabled &&
                    Brain.PhysicalItems != null && Brain.PhysicalItems.Model != null) break;
                yield return null;
            }
            if (Brain?.PhysicalItems?.Model == null || Brain.Survival == null || !Brain.Survival.Enabled ||
                Brain.PhysicalItems.SaveRejected || FoodConsumptionBridge.IsIndeterminateFrozen)
            {
                Receipt = "supplies-refused-world-not-ready";
                yield break;
            }
            var model = Brain.PhysicalItems.Model;
            if (model.TryGetItem(BasketId, out _) || model.TryGetTombstone(BasketId, out _))
            {
                Receipt = "supplies-v1-already-applied";
                Ready = true;
                yield break;
            }
            foreach (var item in model.GetAllItemSnapshots())
                if (item.itemTypeId == "container-basket")
                {
                    Receipt = "existing-storage-preserved";
                    Ready = true;
                    yield break;
                }
            Vector3 position = new Vector3(42f, CoastalTerrain.Height(42f, -30f) + .17f, -30f);
            var basket = Brain.PhysicalItems.CreatePhysicalItem(BasketId, "container-basket", position, Quaternion.identity);
            if (basket == null) { Receipt = "supplies-create-refused"; yield break; }
            foreach (var part in basket.GetComponentsInChildren<Transform>()) part.gameObject.layer = 11;
            var interactable = basket.GetComponent<NpcInteractable>();
            interactable.ObservedType = "woven storage basket";
            if (!FoodConsumptionBridge.TryCommitCoordinatedCheckpoint(Brain, "world-content:fishing-supplies-v1", out string code))
            {
                basket.gameObject.SetActive(false);
                Brain.Pause();
                Receipt = "supplies-checkpoint-failed: " + code;
                Debug.LogError(Receipt);
                yield break;
            }
            Brain.Actions.RegisterInteractable(interactable);
            Receipt = "supplies-v1-committed";
            Ready = true;
            Debug.Log("FISHING_SUPPLIES: " + Receipt);
        }
    }
}
