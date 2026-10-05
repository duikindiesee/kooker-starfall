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
            var sceneBasket = GameObject.Find(BasketId);
            if (sceneBasket != null)
            {
                sceneBasket.SetActive(false);
                if (Application.isPlaying) Destroy(sceneBasket);
                else DestroyImmediate(sceneBasket);
            }
            if (Brain.Actions != null && Brain.Actions.Held != null)
            {
                var heldPhys = Brain.Actions.Held.GetComponent<PhysicalItem>();
                if (heldPhys != null && heldPhys.itemTypeId == "container-basket")
                {
                    Brain.ExecutePlayerAction(NpcActionKind.Drop, Brain.Actions.Held.StableId);
                }
            }
            Receipt = "basket-supplies-deprecated";
            Ready = true;
            Debug.Log("FISHING_SUPPLIES: " + Receipt);
            yield break;
        }
    }
}
