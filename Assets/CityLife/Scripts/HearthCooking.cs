using System;
using UnityEngine;
using Starfall.Refuge;
using CityLife.Items;
using CityLife.World;

namespace CityLife.Food
{
    /// <summary>
    /// Component facilitating culinary roasting of caught river trout and shore crab
    /// over the warm ember bed of the First Refuge hearth fire.
    /// Transforms raw biological catches into high-satiety, high-protein roasted meals.
    /// </summary>
    public sealed class HearthCooking : MonoBehaviour
    {
        public RefugeRuntime Refuge;
        public Vector3 AuthoredHearthPosition;
        public float MaxCookingDistance = 2.8f;
        private AudioSource cookingAudio;
        private static AudioClip sizzleClip;

        public static bool CanRoast(string itemTypeId)
        {
            if (string.IsNullOrEmpty(itemTypeId)) return false;
            return itemTypeId == "food-river-fish" || itemTypeId == "food-river-carp" || itemTypeId == "food-protein-crab" || itemTypeId == "food-wolf-meat";
        }

        public static string GetCookedTypeId(string rawTypeId)
        {
            if (rawTypeId == "food-river-fish" || rawTypeId == "food-river-carp") return "food-cooked-fish";
            if (rawTypeId == "food-protein-crab") return "food-cooked-crab";
            if (rawTypeId == "food-wolf-meat") return "food-cooked-meat";
            return null;
        }

        public static string GetItemDisplayName(string itemTypeId)
        {
            switch (itemTypeId)
            {
                case "food-river-fish": return "Freshwater River Barber";
                case "food-river-carp": return "Gauteng Common Carp";
                case "food-cooked-fish": return "Roasted River Fish";
                case "food-protein-crab": return "Protein Shore Crab";
                case "food-cooked-crab": return "Roasted Shore Crab";
                case "food-wolf-meat": return "Raw Wolf Venison";
                case "food-cooked-meat": return "Roasted Wolf Steak";
                case "material-wolf-leather": return "Cured Wolf Leather";
                default: return itemTypeId;
            }
        }

        private void Awake()
        {
            if (Refuge == null)
            {
                Refuge = FindFirstObjectByType<RefugeRuntime>();
            }
            EnsureAudioSource();
        }

        private void EnsureAudioSource()
        {
            if (cookingAudio == null)
            {
                cookingAudio = gameObject.GetComponent<AudioSource>();
                if (cookingAudio == null)
                {
                    cookingAudio = gameObject.AddComponent<AudioSource>();
                    cookingAudio.playOnAwake = false;
                    cookingAudio.spatialBlend = 0.5f;
                    cookingAudio.volume = 0.6f;
                }
            }
        }

        public Vector3 GetHearthPosition()
        {
            if (Refuge != null && Refuge.Hearth != Vector3.zero)
            {
                return Refuge.Hearth;
            }
            if (AuthoredHearthPosition != Vector3.zero)
            {
                return AuthoredHearthPosition;
            }
            var hearthObj = GameObject.Find("First refuge / discoverable place");
            if (hearthObj != null)
            {
                return hearthObj.transform.position;
            }
            return new Vector3(CoastalTerrain.RefugeCentre.x, 0, CoastalTerrain.RefugeCentre.y);
        }

        public bool IsNearHearth(Vector3 actorPos, out Vector3 hearthPos)
        {
            hearthPos = GetHearthPosition();
            float distXZ = Vector2.Distance(new Vector2(actorPos.x, actorPos.z), new Vector2(hearthPos.x, hearthPos.z));
            float distY = Mathf.Abs(actorPos.y - hearthPos.y);
            return distXZ <= MaxCookingDistance && distY <= 2.2f;
        }

        public bool EnsureHearthBurning()
        {
            if (Refuge == null) Refuge = FindFirstObjectByType<RefugeRuntime>();
            if (Refuge != null)
            {
                if (!Refuge.Fire.Burning)
                {
                    Refuge.Fire.AddLog();
                    Refuge.Fire.AddLog();
                    bool lit = Refuge.Ignite();
                    if (!lit)
                    {
                        Refuge.Fire.Ignite(0.0f, 2.0f, true);
                    }
                }
                if (Refuge.Flame != null) Refuge.Flame.SetActive(true);
                if (Refuge.FireLight != null)
                {
                    Refuge.FireLight.enabled = true;
                    Refuge.FireLight.intensity = 3.0f;
                }
                return Refuge.Fire.Burning;
            }
            return true;
        }

        public bool RoastItem(GameObject itemGo)
        {
            if (itemGo == null) return false;
            var phys = itemGo.GetComponent<PhysicalItem>();
            if (phys == null || !CanRoast(phys.itemTypeId)) return false;

            EnsureHearthBurning();

            string rawType = phys.itemTypeId;
            string cookedType = GetCookedTypeId(rawType);
            phys.itemTypeId = cookedType;

            if (cookedType == "food-cooked-fish")
            {
                phys.massKg = 0.55f;
            }
            else if (cookedType == "food-cooked-crab")
            {
                phys.massKg = 0.40f;
            }
            phys.ConfigureComponents();

            var ni = itemGo.GetComponent<NpcInteractable>();
            if (ni != null)
            {
                if (ni.StableId.Contains("fish") || ni.StableId.Contains("carp"))
                    ni.StableId = ni.StableId.Replace("food-river-fish", "food-cooked-fish").Replace("food-river-carp", "food-cooked-fish").Replace("carp", "cooked-fish");
                else if (ni.StableId.Contains("crab"))
                    ni.StableId = ni.StableId.Replace("crab", "cooked-crab");
            }

            // Procedurally update materials to look roasted / charred
            var renderers = itemGo.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r != null && r.material != null)
                {
                    if (cookedType == "food-cooked-fish")
                    {
                        r.material.color = new Color(0.68f, 0.45f, 0.22f); // Golden smoked amber fish
                    }
                    else if (cookedType == "food-cooked-crab")
                    {
                        r.material.color = new Color(0.88f, 0.28f, 0.16f); // Savory roasted coral red crab
                    }
                }
            }

            PlaySizzleCue();
            Debug.Log($"[HearthCooking] Successfully roasted {rawType} into {cookedType} at refuge hearth!");
            return true;
        }

        public void PlaySizzleCue()
        {
            EnsureAudioSource();
            if (cookingAudio != null)
            {
                if (sizzleClip == null)
                {
                    int sampleRate = 22050;
                    float[] samples = new float[sampleRate / 2]; // 0.5s sizzle
                    var rng = new System.Random(42);
                    for (int i = 0; i < samples.Length; i++)
                    {
                        float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                        float env = 1f - ((float)i / samples.Length);
                        samples[i] = noise * env * 0.22f;
                    }
                    sizzleClip = AudioClip.Create("HearthSizzle", samples.Length, 1, sampleRate, false);
                    sizzleClip.SetData(samples, 0);
                }
                cookingAudio.PlayOneShot(sizzleClip, 0.7f);
            }
        }

        public static bool TryRoastHeldItem(NpcAutonomy brain)
        {
            if (brain == null || brain.Actions == null || brain.Actions.Held == null) return false;
            var held = brain.Actions.Held;
            var phys = held.GetComponent<PhysicalItem>();
            if (phys == null || !CanRoast(phys.itemTypeId)) return false;

            var cooker = brain.GetComponentInChildren<HearthCooking>();
            if (cooker == null) cooker = FindFirstObjectByType<HearthCooking>();
            if (cooker == null)
            {
                cooker = brain.gameObject.AddComponent<HearthCooking>();
            }

            if (!cooker.IsNearHearth(brain.transform.position, out _)) return false;

            return cooker.RoastItem(held.gameObject);
        }
    }
}