using System;
using UnityEngine;
using CityLife.Items;

namespace CityLife.World
{
    /// <summary>
    /// Autonomous reactive crustacean actor.
    /// Provides idle claw twitching, shell bobbing, and defensive sideways scuttling
    /// when the survivor or player approaches within proximity range.
    /// </summary>
    public sealed class CoastalCrabActor : MonoBehaviour
    {
        public float ScuttleSpeed = 1.25f;
        public float ThreatDistance = 3.5f;
        public bool IsScuttling { get; private set; }
        public bool IsThreatDisplay { get; private set; }
        public bool IsStunned { get; private set; }
        public Vector3 CurrentVelocity { get; private set; }

        public enum CrabBehaviorState { Idle, Scuttling, ThreatDisplay, Stunned }
        public CrabBehaviorState State { get; private set; } = CrabBehaviorState.Idle;

        private Vector3 homePosition;
        private float nextTwitchTime;
        private float twitchAngleOffset;
        private float stateTimer;
        private float stunUntilTime;
        private PhysicalItem physItem;
        private Vector3 previousPosition;

        private void Start()
        {
            homePosition = transform.position;
            previousPosition = transform.position;
            nextTwitchTime = Time.time + UnityEngine.Random.Range(1f, 3f);
            physItem = GetComponent<PhysicalItem>();
        }

        public void Stun(float duration = 3.0f)
        {
            IsStunned = true;
            IsScuttling = false;
            IsThreatDisplay = false;
            State = CrabBehaviorState.Stunned;
            stunUntilTime = Time.time + duration;
        }

        private void Update()
        {
            // If held in hand or stowed in container, do not scuttle across terrain
            if (physItem != null && (physItem.IsCarried || physItem.IsStored))
            {
                IsScuttling = false;
                IsThreatDisplay = false;
                IsStunned = false;
                CurrentVelocity = Vector3.zero;
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Handle Stunned state (e.g. struck by hunter's club)
            if (IsStunned)
            {
                CurrentVelocity = Vector3.zero;
                if (Time.time >= stunUntilTime)
                {
                    IsStunned = false;
                    State = CrabBehaviorState.Idle;
                }
                else
                {
                    // Lying motionless on sand
                    float groundY = CoastalTerrain.Height(transform.position.x, transform.position.z) + 0.02f;
                    transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
                    return;
                }
            }

            Transform nearestThreat = null;
            float nearestDist = ThreatDistance;

            var player = FindFirstObjectByType<CharacterPreviewActor>();
            if (player != null)
            {
                float d = Vector3.Distance(transform.position, player.transform.position);
                if (d < nearestDist) { nearestDist = d; nearestThreat = player.transform; }
            }

            if (nearestThreat != null)
            {
                Vector3 toThreat = (nearestThreat.position - transform.position);
                toThreat.y = 0;
                Vector3 away = -toThreat;

                // State machine: alternate between rapid sideways scuttle burst (1.5s)
                // and rearing up into defensive threat display (1.8s) when cornered
                if (State == CrabBehaviorState.ThreatDisplay)
                {
                    IsScuttling = false;
                    IsThreatDisplay = true;
                    stateTimer -= dt;

                    // Turn directly to face the oncoming hunter with raised claws
                    if (toThreat.sqrMagnitude > 0.01f)
                    {
                        Quaternion lookAtThreat = Quaternion.LookRotation(toThreat.normalized, Vector3.up);
                        // Rear up by 18-24 degrees to brandish pincers in threat display
                        float rearAngle = 18f + Mathf.Sin(Time.time * 6f) * 5f;
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            lookAtThreat * Quaternion.Euler(-rearAngle, 0, 0), 360f * dt);
                    }

                    // Claws snap aggressively while holding ground
                    float threatBob = Mathf.Sin(Time.time * 8f) * 0.006f;
                    float baseY = CoastalTerrain.Height(transform.position.x, transform.position.z) + 0.03f;
                    transform.position = new Vector3(transform.position.x, baseY + threatBob, transform.position.z);

                    if (stateTimer <= 0f)
                    {
                        // Reset to scuttle burst if threat is still too close
                        State = CrabBehaviorState.Scuttling;
                        stateTimer = UnityEngine.Random.Range(1.2f, 1.8f);
                    }
                }
                else
                {
                    // Reactive evasive sideways scuttle away from threat
                    IsScuttling = true;
                    IsThreatDisplay = false;
                    State = CrabBehaviorState.Scuttling;
                    stateTimer -= dt;

                    if (away.sqrMagnitude > 0.001f)
                    {
                        // Crabs scuttle perpendicular/oblique to incoming threat (sideways)
                        Vector3 sideways = Vector3.Cross(away.normalized, Vector3.up).normalized;
                        if (Vector3.Dot(sideways, transform.right) < 0) sideways = -sideways;

                        Vector3 scuttleDir = (away.normalized * 0.65f + sideways * 0.75f).normalized;
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(sideways, Vector3.up), 360f * dt);

                        // Scampering leg scamper bounce
                        float legWiggle = Mathf.Sin(Time.time * 20f) * 0.005f;
                        Vector3 nextPos = transform.position + scuttleDir * ScuttleSpeed * dt;
                        nextPos.y = CoastalTerrain.Height(nextPos.x, nextPos.z) + 0.03f + legWiggle;
                        transform.position = nextPos;
                    }

                    // After a short scuttle burst, pause and turn to rear up in defensive posture
                    if (stateTimer <= 0f || nearestDist <= 1.9f)
                    {
                        State = CrabBehaviorState.ThreatDisplay;
                        stateTimer = UnityEngine.Random.Range(1.6f, 2.4f);
                    }
                }
            }
            else
            {
                IsScuttling = false;
                IsThreatDisplay = false;
                State = CrabBehaviorState.Idle;

                // Idle claw waving and shell twitching
                if (Time.time >= nextTwitchTime)
                {
                    nextTwitchTime = Time.time + UnityEngine.Random.Range(2f, 5f);
                    twitchAngleOffset = UnityEngine.Random.Range(-18f, 18f);
                    transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + twitchAngleOffset, 0f);
                }

                // Gentle procedural shell breathing bob
                float bob = Mathf.Sin(Time.time * 3f) * 0.004f;
                float baseY = CoastalTerrain.Height(transform.position.x, transform.position.z) + 0.03f;
                transform.position = new Vector3(transform.position.x, baseY + bob, transform.position.z);
            }

            CurrentVelocity = (transform.position - previousPosition) / dt;
            previousPosition = transform.position;
        }
    }
}
