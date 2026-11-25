using System;
using System.Collections.Generic;
using Assets._Project.Features.Audio;
using UnityEngine;

namespace Assets._Project.Features.WalkingDragon
{
    /// <summary>
    /// Bear-style animation/audio adapter for WalkingDragonController.
    /// The AI owns gameplay and movement; this component owns Animator parameters,
    /// animation triggers, animation-timed footsteps, and long-range 3D one-shot audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WalkingDragonAnimatableComponent : MonoBehaviour
    {
        [Serializable]
        public sealed class DistantSoundCue
        {
            public bool Enabled = true;
            public AudioClip Clip;
            [Range(0f, 1f)] public float Volume = 1f;
            [Min(0.01f)] public float MinDistance = 80f;
            [Min(0.02f)] public float MaxDistance = 1800f;
        }

        [Header("References")]
        [SerializeField] private WalkingDragonController dragon;
        [SerializeField] private Animator animator;
        [Tooltip("The boss movement motor. Auto-found on the dragon root when empty.")]
        [SerializeField] private SwimmingMovement movement;
        [Tooltip("The dragon Rigidbody. Auto-found on the dragon root when empty.")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private bool DisableAnimatorRootMotion = true;

        [Header("Animation Playback / Cutscenes")]
        [Tooltip("Global playback multiplier for the dragon's animations. Lower values make the colossal creature feel heavier. This is not applied while Stop Animations is enabled, so Timeline or a cutscene script can control Animator playback speed.")]
        [Range(0.1f, 1.5f)] [SerializeField] private float animationSpeedMultiplier = 0.55f;
        [Tooltip("Stops this component from driving Animator parameters, triggers, footsteps, or animation-timed fireball release. The Animator remains enabled and can be animated by Timeline or another cutscene system.")]
        public bool StopAnimations;

        [Header("Animation-Timed Fireball Release")]
        [Tooltip("Animator state used for a normal fireball throw. Match the generated controller state name.")]
        [SerializeField] private string fireballStateName = "Fireball";
        [Tooltip("Animator state used for a fireball throw during the ram jump.")]
        [SerializeField] private string flyFireballStateName = "Fly Fireball";
        [Tooltip("Release the projectile when this fraction of the fireball animation has elapsed. 0.5 means the exact midpoint of the clip, regardless of its slowed playback speed.")]
        [Range(0.05f, 0.95f)] [SerializeField] private float fireballReleaseNormalizedTime = 0.5f;
        [SerializeField] private int fireballAnimatorLayer;

        [Header("Animator Parameters")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
        [SerializeField] private string groundedParameter = "Grounded";
        [SerializeField] private string jumpingParameter = "Jumping";
        [SerializeField] private string fallingParameter = "Falling";
        [SerializeField] private string ramAirborneParameter = "RamAirborne";
        [SerializeField] private string chargingParameter = "Charging";

        [Header("Animator Triggers")]
        [SerializeField] private string roarTrigger = "Roar";
        [SerializeField] private string fireballTrigger = "Fireball";
        [SerializeField] private string flyFireballTrigger = "FlyFireball";
        [SerializeField] private string ramJumpTrigger = "RamJump";
        [SerializeField] private string ramChargeTrigger = "RamCharge";
        [SerializeField] private string ramImpactTrigger = "RamImpact";
        [SerializeField] private string landTrigger = "Land";
        [SerializeField] private string defendTrigger = "Defend";
        [SerializeField] private string tailAttackTrigger = "TailAttack";
        [SerializeField] private string hitTrigger = "Hit";
        [SerializeField] private string dieTrigger = "Die";
        [SerializeField] private string sleepTrigger = "Sleep";
        [SerializeField] private string wakeTrigger = "Wake";
        [SerializeField] private string flyFloatTrigger = "FlyFloat";
        [SerializeField] private string flyGlideTrigger = "FlyGlide";

        [Header("Parameter Smoothing")]
        [Min(0f)] [SerializeField] private float speedDampTime = 0.18f;
        [Min(0f)] [SerializeField] private float verticalSpeedDampTime = 0.12f;

        [Header("Footsteps")]
        [Tooltip("Footsteps are synchronized to the looping Ground Locomotion / Ram Charge animation time. No Animation Events need to be added to the imported FBX files.")]
        [SerializeField] private bool PlayFootsteps = true;
        [Min(0f)] [SerializeField] private float footstepMinMovementSpeed = 1.2f;
        [Range(0f, 1f)] [SerializeField] private float firstFootstepPhase = 0.12f;
        [Range(0f, 1f)] [SerializeField] private float secondFootstepPhase = 0.62f;
        [SerializeField] private int footstepAnimatorLayer;
        [SerializeField] private string locomotionStateName = "Ground Locomotion";
        [SerializeField] private string ramChargeStateName = "Ram Charge";
        [SerializeField] private DistantSoundCue FootstepSound = new DistantSoundCue
        {
            Volume = 0.9f,
            MinDistance = 80f,
            MaxDistance = 1800f
        };

        [Header("Distant Boss Sounds")]
        [Tooltip("Played when the Leviathan begins the roar telegraph before a boat ram.")]
        [SerializeField] private DistantSoundCue RoarSound = new DistantSoundCue
        {
            Volume = 1f,
            MinDistance = 120f,
            MaxDistance = 3500f
        };
        [Tooltip("Played only when the ram successfully contacts the boat hull.")]
        [SerializeField] private DistantSoundCue RamHitSound = new DistantSoundCue
        {
            Volume = 1f,
            MinDistance = 100f,
            MaxDistance = 3200f
        };
        [Tooltip("Played when a fireball projectile is actually released, not at the beginning of its wind-up.")]
        [SerializeField] private DistantSoundCue FireballThrowSound = new DistantSoundCue
        {
            Volume = 1f,
            MinDistance = 70f,
            MaxDistance = 2800f
        };

        private readonly Dictionary<int, AnimatorControllerParameterType> availableParameters =
            new Dictionary<int, AnimatorControllerParameterType>(24);

        private WalkingDragonController subscribedDragon;
        private Animator cachedAnimator;
        private float lastLocomotionNormalizedTime;
        private bool hasFootstepClock;
        private bool wasRamAirborne;
        private bool suppressNextLandAfterRamImpact;
        private int locomotionStateHash;
        private int ramChargeStateHash;
        private int fireballStateHash;
        private int flyFireballStateHash;
        private bool previousStopAnimations;

        private void Awake()
        {
            ResolveReferences();
            CacheAnimatorParameters();
            CacheStateHashes();
            if (animator != null && DisableAnimatorRootMotion)
                animator.applyRootMotion = false;
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheAnimatorParameters();
            CacheStateHashes();
            BindDragonEvents();
            wasRamAirborne = dragon != null && dragon.IsRamJumping;
            hasFootstepClock = false;
        }

        private void OnDisable()
        {
            UnbindDragonEvents();
            if (dragon != null)
                dragon.SetAnimationDrivenFireballReleaseAvailable(false);
            hasFootstepClock = false;
        }

        private void OnValidate()
        {
            speedDampTime = Mathf.Max(0f, speedDampTime);
            verticalSpeedDampTime = Mathf.Max(0f, verticalSpeedDampTime);
            footstepMinMovementSpeed = Mathf.Max(0f, footstepMinMovementSpeed);
            firstFootstepPhase = Mathf.Clamp01(firstFootstepPhase);
            secondFootstepPhase = Mathf.Clamp01(secondFootstepPhase);
            animationSpeedMultiplier = Mathf.Clamp(animationSpeedMultiplier, 0.1f, 1.5f);
            fireballReleaseNormalizedTime = Mathf.Clamp(fireballReleaseNormalizedTime, 0.05f, 0.95f);
            fireballAnimatorLayer = Mathf.Max(0, fireballAnimatorLayer);
            ClampCue(FootstepSound);
            ClampCue(RoarSound);
            ClampCue(RamHitSound);
            ClampCue(FireballThrowSound);
            CacheStateHashes();
        }

        private void Update()
        {
            ResolveReferences();
            BindDragonEvents();

            // Keep the controller's fallback/animation-timed release mode accurate
            // even if the Animator reference is removed or assigned at runtime.
            RefreshAnimationDrivenFireballRelease();

            if (animator == null || dragon == null)
                return;

            if (cachedAnimator != animator)
            {
                CacheAnimatorParameters();
                if (DisableAnimatorRootMotion && animator != null)
                    animator.applyRootMotion = false;
            }

            if (StopAnimations)
            {
                if (!previousStopAnimations)
                    ResetAutomaticAnimationTriggers();

                previousStopAnimations = true;
                hasFootstepClock = false;
                return;
            }

            previousStopAnimations = false;
            animator.speed = animationSpeedMultiplier;

            UpdateAnimatorParameters();
            UpdateLandingEdge();
            UpdateFootsteps();
        }

        private void LateUpdate()
        {
            // Sample the fireball state's normalized time after Animator evaluation
            // so the spawned projectile uses the muzzle's current animated pose.
            if (!StopAnimations && animator != null && dragon != null)
                UpdateFireballAnimationRelease();
        }

        private void ResolveReferences()
        {
            if (dragon == null)
                dragon = GetComponent<WalkingDragonController>() ?? GetComponentInParent<WalkingDragonController>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            if (movement == null && dragon != null)
                movement = dragon.GetComponent<SwimmingMovement>();

            if (body == null && dragon != null)
                body = dragon.GetComponent<Rigidbody>();
        }

        private void BindDragonEvents()
        {
            if (subscribedDragon == dragon)
                return;

            UnbindDragonEvents();
            subscribedDragon = dragon;
            if (subscribedDragon == null)
                return;

            subscribedDragon.RoarStarted += OnRoarStarted;
            subscribedDragon.FireballWindupStarted += OnFireballWindupStarted;
            subscribedDragon.FireballLaunched += OnFireballLaunched;
            subscribedDragon.RamJumpStarted += OnRamJumpStarted;
            subscribedDragon.RamChargeStarted += OnRamChargeStarted;
            subscribedDragon.RamImpact += OnRamImpact;
        }

        private void UnbindDragonEvents()
        {
            if (subscribedDragon != null)
            {
                subscribedDragon.RoarStarted -= OnRoarStarted;
                subscribedDragon.FireballWindupStarted -= OnFireballWindupStarted;
                subscribedDragon.FireballLaunched -= OnFireballLaunched;
                subscribedDragon.RamJumpStarted -= OnRamJumpStarted;
                subscribedDragon.RamChargeStarted -= OnRamChargeStarted;
                subscribedDragon.RamImpact -= OnRamImpact;
            }

            subscribedDragon = null;
        }

        private void OnRoarStarted()
        {
            if (StopAnimations)
                return;

            SetTrigger(roarTrigger);
            Vector3 roarPosition = dragon != null && dragon.VisualRoot != null
                ? dragon.VisualRoot.position
                : (dragon != null ? dragon.transform.position : transform.position);
            PlayCue(RoarSound, roarPosition);
        }

        private void OnFireballWindupStarted()
        {
            if (StopAnimations)
                return;

            // Select the airborne version while leaping; otherwise use the grounded throw.
            if (dragon != null && dragon.IsRamJumping)
                SetTrigger(flyFireballTrigger);
            else
                SetTrigger(fireballTrigger);
        }

        private void OnFireballLaunched(Vector3 spawnPosition)
        {
            if (StopAnimations)
                return;

            PlayCue(FireballThrowSound, spawnPosition);
        }

        private void OnRamJumpStarted()
        {
            if (StopAnimations)
                return;

            // Set the state booleans immediately as well as in Update. This keeps
            // AnimatePhysics controllers from evaluating Take Off with stale flags.
            SetBool(ramAirborneParameter, true);
            SetBool(groundedParameter, false);
            SetBool(jumpingParameter, true);
            SetBool(fallingParameter, false);
            SetBool(chargingParameter, false);
            SetTrigger(ramJumpTrigger);
        }

        private void OnRamChargeStarted()
        {
            if (StopAnimations)
                return;

            SetBool(ramAirborneParameter, false);
            SetBool(groundedParameter, true);
            SetBool(jumpingParameter, false);
            SetBool(fallingParameter, false);
            SetBool(chargingParameter, true);
            SetTrigger(ramChargeTrigger);
        }

        private void OnRamImpact(Vector3 impactPosition)
        {
            if (StopAnimations)
                return;

            suppressNextLandAfterRamImpact = dragon != null && dragon.IsRamJumping;
            ResetTrigger(landTrigger);
            SetBool(ramAirborneParameter, false);
            SetBool(groundedParameter, true);
            SetBool(chargingParameter, false);
            SetTrigger(ramImpactTrigger);
            PlayCue(RamHitSound, impactPosition);
        }

        private void UpdateAnimatorParameters()
        {
            Vector3 planarVelocity = movement != null ? movement.PlanarVelocity : Vector3.zero;
            // The controller stops the dragon at impact, then moves it backward
            // while it keeps facing the player. A negative Speed selects the
            // reverse-walk portion of the generated locomotion blend tree.
            float speed = dragon.IsRamRetreating ? -planarVelocity.magnitude : planarVelocity.magnitude;
            float verticalSpeed = body != null ? body.linearVelocity.y :
                (movement != null ? movement.VerticalVelocity : 0f);

            bool ramAirborne = dragon.IsRamJumping;
            bool charging = dragon.IsRamCharging;

            SetFloat(speedParameter, speed, speedDampTime);
            SetFloat(verticalSpeedParameter, verticalSpeed, verticalSpeedDampTime);
            SetBool(groundedParameter, !ramAirborne);
            SetBool(jumpingParameter, ramAirborne && verticalSpeed >= -0.05f);
            SetBool(fallingParameter, ramAirborne && verticalSpeed < -0.05f);
            SetBool(ramAirborneParameter, ramAirborne);
            SetBool(chargingParameter, charging);
        }

        private void UpdateLandingEdge()
        {
            bool isRamAirborne = dragon != null && dragon.IsRamJumping;
            if (wasRamAirborne && !isRamAirborne && (dragon == null || !dragon.IsRamCharging))
            {
                if (!suppressNextLandAfterRamImpact)
                    SetTrigger(landTrigger);
                suppressNextLandAfterRamImpact = false;
            }

            wasRamAirborne = isRamAirborne;
        }

        private void UpdateFootsteps()
        {
            if (!PlayFootsteps || animator == null || movement == null || FootstepSound == null || FootstepSound.Clip == null)
            {
                hasFootstepClock = false;
                return;
            }

            if (footstepAnimatorLayer < 0 || footstepAnimatorLayer >= animator.layerCount)
            {
                hasFootstepClock = false;
                return;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(footstepAnimatorLayer);
            bool isFootstepState = state.shortNameHash == locomotionStateHash || state.shortNameHash == ramChargeStateHash;
            if (!isFootstepState || animator.IsInTransition(footstepAnimatorLayer) ||
                movement.PlanarVelocity.magnitude < footstepMinMovementSpeed)
            {
                hasFootstepClock = false;
                return;
            }

            float normalizedTime = state.normalizedTime;
            if (!hasFootstepClock || normalizedTime < lastLocomotionNormalizedTime)
            {
                lastLocomotionNormalizedTime = normalizedTime;
                hasFootstepClock = true;
                return;
            }

            if (CrossedPhase(lastLocomotionNormalizedTime, normalizedTime, firstFootstepPhase))
                PlayCue(FootstepSound, GetFootstepPosition());
            if (CrossedPhase(lastLocomotionNormalizedTime, normalizedTime, secondFootstepPhase))
                PlayCue(FootstepSound, GetFootstepPosition());

            lastLocomotionNormalizedTime = normalizedTime;
        }

        private void UpdateFireballAnimationRelease()
        {
            if (StopAnimations || dragon == null || animator == null || !dragon.IsFireballWindingUp)
                return;

            if (fireballAnimatorLayer < 0 || fireballAnimatorLayer >= animator.layerCount)
                return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(fireballAnimatorLayer);
            bool isFireballState = IsFireballState(state.shortNameHash);

            // During the entry transition, inspect the destination state as well.
            // This avoids missing the first frames of a short transition.
            if (!isFireballState && animator.IsInTransition(fireballAnimatorLayer))
            {
                AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(fireballAnimatorLayer);
                if (IsFireballState(nextState.shortNameHash))
                {
                    state = nextState;
                    isFireballState = true;
                }
            }

            if (!isFireballState || state.normalizedTime < fireballReleaseNormalizedTime)
                return;

            // The controller clears its pending flag after creating the projectile,
            // so this remains safe to check every frame after the midpoint.
            dragon.TryReleaseFireballFromAnimation();
        }

        private bool IsFireballState(int stateHash)
        {
            return stateHash == fireballStateHash || stateHash == flyFireballStateHash;
        }

        private void RefreshAnimationDrivenFireballRelease()
        {
            if (dragon == null)
                return;

            bool hasUsableAnimator = !StopAnimations && animator != null && animator.enabled &&
                                     animator.gameObject.activeInHierarchy &&
                                     animator.runtimeAnimatorController != null &&
                                     fireballAnimatorLayer >= 0 && fireballAnimatorLayer < animator.layerCount &&
                                     fireballStateHash != 0 && flyFireballStateHash != 0;
            dragon.SetAnimationDrivenFireballReleaseAvailable(hasUsableAnimator);
        }

        private void ResetAutomaticAnimationTriggers()
        {
            ResetTrigger(roarTrigger);
            ResetTrigger(fireballTrigger);
            ResetTrigger(flyFireballTrigger);
            ResetTrigger(ramJumpTrigger);
            ResetTrigger(ramChargeTrigger);
            ResetTrigger(ramImpactTrigger);
            ResetTrigger(landTrigger);
            ResetTrigger(defendTrigger);
            ResetTrigger(tailAttackTrigger);
            ResetTrigger(hitTrigger);
            ResetTrigger(dieTrigger);
            ResetTrigger(sleepTrigger);
            ResetTrigger(wakeTrigger);
            ResetTrigger(flyFloatTrigger);
            ResetTrigger(flyGlideTrigger);
        }

        private static bool CrossedPhase(float previousTime, float currentTime, float phase)
        {
            if (currentTime <= previousTime)
                return false;

            int firstCycle = Mathf.FloorToInt(previousTime - phase) + 1;
            int lastCycle = Mathf.FloorToInt(currentTime - phase);
            return firstCycle <= lastCycle;
        }

        private void CacheAnimatorParameters()
        {
            availableParameters.Clear();
            cachedAnimator = animator;
            if (animator == null)
                return;

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
                availableParameters[parameters[i].nameHash] = parameters[i].type;
        }

        private void CacheStateHashes()
        {
            locomotionStateHash = string.IsNullOrWhiteSpace(locomotionStateName)
                ? 0 : Animator.StringToHash(locomotionStateName);
            ramChargeStateHash = string.IsNullOrWhiteSpace(ramChargeStateName)
                ? 0 : Animator.StringToHash(ramChargeStateName);
            fireballStateHash = string.IsNullOrWhiteSpace(fireballStateName)
                ? 0 : Animator.StringToHash(fireballStateName);
            flyFireballStateHash = string.IsNullOrWhiteSpace(flyFireballStateName)
                ? 0 : Animator.StringToHash(flyFireballStateName);
        }

        private bool HasParameter(string parameterName, AnimatorControllerParameterType expectedType)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                return false;

            int hash = Animator.StringToHash(parameterName);
            return availableParameters.TryGetValue(hash, out AnimatorControllerParameterType actualType) && actualType == expectedType;
        }

        private void SetFloat(string parameterName, float value, float dampTime)
        {
            if (!StopAnimations && animator != null && HasParameter(parameterName, AnimatorControllerParameterType.Float))
                animator.SetFloat(Animator.StringToHash(parameterName), value, dampTime, Time.deltaTime);
        }

        private void SetBool(string parameterName, bool value)
        {
            if (!StopAnimations && animator != null && HasParameter(parameterName, AnimatorControllerParameterType.Bool))
                animator.SetBool(Animator.StringToHash(parameterName), value);
        }

        private void ResetTrigger(string parameterName)
        {
            if (animator == null || !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
                return;

            animator.ResetTrigger(Animator.StringToHash(parameterName));
        }

        private void SetTrigger(string parameterName, bool allowDuringCutscene = false)
        {
            if ((StopAnimations && !allowDuringCutscene) || animator == null ||
                !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
                return;

            int hash = Animator.StringToHash(parameterName);
            animator.ResetTrigger(hash);
            animator.SetTrigger(hash);
        }

        private Vector3 GetFootstepPosition()
        {
            return dragon != null && dragon.GroundContactReference != null
                ? dragon.GroundContactReference.position
                : (dragon != null ? dragon.transform.position : transform.position);
        }

        private void PlayCue(DistantSoundCue cue, Vector3 worldPosition)
        {
            if (cue == null || !cue.Enabled || cue.Clip == null)
                return;

            float minDistance = Mathf.Max(0.01f, cue.MinDistance);
            float maxDistance = Mathf.Max(minDistance + 0.01f, cue.MaxDistance);
            Sound.Play(cue.Clip, worldPosition, Mathf.Clamp01(cue.Volume), minDistance, maxDistance);
        }

        private static void ClampCue(DistantSoundCue cue)
        {
            if (cue == null)
                return;

            cue.Volume = Mathf.Clamp01(cue.Volume);
            cue.MinDistance = Mathf.Max(0.01f, cue.MinDistance);
            cue.MaxDistance = Mathf.Max(cue.MinDistance + 0.01f, cue.MaxDistance);
        }

        // Manual hooks intentionally bypass StopAnimations. The option prevents
        // the AI/event adapter from taking over, while cutscene signals can still
        // request the exact animation they need.
        public void PlayRoar() => SetTrigger(roarTrigger, true);
        public void PlayFireball() => SetTrigger(fireballTrigger, true);
        public void PlayFlyFireball() => SetTrigger(flyFireballTrigger, true);
        public void PlayRamJump() => SetTrigger(ramJumpTrigger, true);
        public void PlayRamCharge() => SetTrigger(ramChargeTrigger, true);
        public void PlayRamImpact() => SetTrigger(ramImpactTrigger, true);
        public void PlayLand() => SetTrigger(landTrigger, true);
        public void PlayDefend() => SetTrigger(defendTrigger, true);
        public void PlayTailAttack() => SetTrigger(tailAttackTrigger, true);
        public void PlayHitReaction() => SetTrigger(hitTrigger, true);
        public void PlayDeath() => SetTrigger(dieTrigger, true);
        public void PlaySleep() => SetTrigger(sleepTrigger, true);
        public void PlayWake() => SetTrigger(wakeTrigger, true);
        public void PlayFlyFloat() => SetTrigger(flyFloatTrigger, true);
        public void PlayFlyGlide() => SetTrigger(flyGlideTrigger, true);

        /// <summary>
        /// Suspends AI-driven animation parameters/triggers and footstep/fireball
        /// animation callbacks, leaving the Animator active for Timeline or manual
        /// cutscene playback. Public Play* methods remain available to cutscene
        /// signals. Pair with WalkingDragonController.FreezeAttacks().
        /// </summary>
        public void SetStopAnimations(bool stopped)
        {
            StopAnimations = stopped;
            if (stopped)
            {
                ResetAutomaticAnimationTriggers();
                hasFootstepClock = false;
            }

            RefreshAnimationDrivenFireballRelease();
        }
    }
}
