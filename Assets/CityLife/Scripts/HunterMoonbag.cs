using System;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Side belt pouch (moonbag) worn on the hunter's waist.
    /// Securely holds up to 2 edible fruits for long journeys,
    /// freeing the inhabitant's hands for tool use, climbing, or swimming.
    /// Provides visual feedback with fruit peeking from the waist pouch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HunterMoonbag : MonoBehaviour
    {
        public const int MaxCapacity = 2;

        [SerializeField]
        private int storedFruitCount = 0;

        public int StoredCount => storedFruitCount;
        public bool CanStore => storedFruitCount < MaxCapacity;
        public bool CanRetrieve => storedFruitCount > 0;

        public Transform VisualSlot1;
        public Transform VisualSlot2;

        public bool StoreFruit()
        {
            if (storedFruitCount >= MaxCapacity) return false;
            storedFruitCount++;
            UpdateVisuals();
            return true;
        }

        public bool RetrieveFruit()
        {
            if (storedFruitCount <= 0) return false;
            storedFruitCount--;
            UpdateVisuals();
            return true;
        }

        public void SetStoredCount(int count)
        {
            storedFruitCount = Mathf.Clamp(count, 0, MaxCapacity);
            UpdateVisuals();
        }

        public void UpdateVisuals()
        {
            if (VisualSlot1 != null) VisualSlot1.gameObject.SetActive(storedFruitCount >= 1);
            if (VisualSlot2 != null) VisualSlot2.gameObject.SetActive(storedFruitCount >= 2);
        }

        public void AttachVisuals(Transform hipsBone, Material fruitMaterial)
        {
            if (hipsBone == null) return;

            // Hip waist pouch position: left hip
            Vector3 pouchAnchor = new Vector3(0.208f, -0.05f, -0.085f);

            Vector3 parentLossy = hipsBone.lossyScale;
            Vector3 targetScale1 = new Vector3(
                0.055f / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.x)),
                0.055f / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.y)),
                0.055f / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.z)));
            Vector3 targetScale2 = new Vector3(
                0.050f / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.x)),
                0.050f / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.y)),
                0.050f / Mathf.Max(0.0001f, Mathf.Abs(parentLossy.z)));

            if (VisualSlot1 == null)
            {
                var f1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f1.name = "Moonbag Fruit Indicator 1";
                f1.transform.SetParent(hipsBone, false);
                f1.transform.localPosition = pouchAnchor + new Vector3(0.012f, 0.025f, -0.012f);
                f1.transform.localScale = targetScale1;
                var col = f1.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                if (fruitMaterial != null) f1.GetComponent<MeshRenderer>().sharedMaterial = fruitMaterial;
                VisualSlot1 = f1.transform;
            }

            if (VisualSlot2 == null)
            {
                var f2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f2.name = "Moonbag Fruit Indicator 2";
                f2.transform.SetParent(hipsBone, false);
                f2.transform.localPosition = pouchAnchor + new Vector3(-0.012f, 0.020f, 0.015f);
                f2.transform.localScale = targetScale2;
                var col = f2.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
                if (fruitMaterial != null) f2.GetComponent<MeshRenderer>().sharedMaterial = fruitMaterial;
                VisualSlot2 = f2.transform;
            }

            UpdateVisuals();
        }
    }
}
