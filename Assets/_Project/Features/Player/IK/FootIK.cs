using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
[AddComponentMenu("Animation/Foot IK")]
public class FootIK : MonoBehaviour
{
    // ------------------------------------------------------------------ settings
    [Header("General")]
    [Tooltip("Humanoid Animator. Leave empty to auto-find it on this object or its children.")]
    public Animator animator;
    [Range(0f, 1f)]
    [Tooltip("Master weight. Set to 0 from gameplay code to disable (swimming, climbing...).")]
    public float externalWeight = 1f;
    [Tooltip("Seconds to fade the whole system in/out (jump, land, swim, tags).")]
    public float blendTime = 0.12f;

    [Header("Ground detection")]
    public LayerMask groundMask = ~0;
    [Tooltip("Highest step the feet may be raised onto (meters at scale 1).")]
    public float maxStepUp = 0.4f;
    [Tooltip("Deepest drop the feet may reach down to (meters at scale 1).")]
    public float maxStepDown = 0.5f;
    [Tooltip("Hit normals steeper than this (step edges, risers) are not used to tilt the foot. The hit height is still used, so big steps are never rejected.")]
    [Range(0f, 89f)] public float maxGroundSlope = 65f;
    [Tooltip("Height of the animation's ground plane relative to transform.position. 0 if the pivot is at the feet.")]
    public float baseHeightOffset = 0f;
    [Tooltip("Extra ankle-to-sole distance added to the value reported by the Animator. Tune if feet sink/float.")]
    public float footHeightOffset = 0f;

    [Header("Foot plant detection (from the animated pose)")]
    [Tooltip("Sole clearance (m) under which a foot counts as fully planted.")]
    public float plantedHeight = 0.02f;
    [Tooltip("Sole clearance (m) above which a foot counts as fully in the air.")]
    public float liftedHeight = 0.14f;
    [Tooltip("Height of the toe joint above the sole. Used so toe-off keeps the foot planted.")]
    public float toeHeightOffset = 0.03f;
    [Tooltip("How fast the plant weight follows (1/s).")]
    public float contactBlendSpeed = 12f;
    [Range(0f, 1f)]
    [Tooltip("How much a swinging foot is raised over higher ground (1 = full clearance, good for stairs).")]
    public float swingLift = 1f;
    [Tooltip("If the controller has float params named below, they multiply the plant weight (default value must be 1).")]
    public bool useAnimatorCurves = true;
    public string leftCurveParam = "LeftFootIK";
    public string rightCurveParam = "RightFootIK";

    [Header("Smoothing")]
    [Tooltip("Seconds to follow ground that got higher.")]
    public float footRaiseTime = 0.05f;
    [Tooltip("Seconds to follow ground that got lower.")]
    public float footLowerTime = 0.09f;
    [Tooltip("Seconds for the hip height.")]
    public float pelvisTime = 0.10f;
    [Tooltip("Surface normal smoothing speed (1/s).")]
    public float normalSpeed = 12f;

    [Header("Foot rotation")]
    public bool alignFootToGround = true;
    [Range(0f, 80f)] public float maxFootAngle = 35f;

    [Header("Hips")]
    public bool adjustPelvis = true;
    [Tooltip("Max hip drop (m at scale 1).")]
    public float maxPelvisDrop = 0.45f;
    [Tooltip("Max hip raise (m at scale 1).")]
    public float maxPelvisRaise = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("How much the hips rise with the average height of planted feet on higher ground (stairs/slopes up).")]
    public float pelvisRaise = 0.5f;
    [Range(0f, 2f)]
    [Tooltip("Multiplier on the hip drop. >1 bends the legs more.")]
    public float pelvisDropScale = 1f;

    [Header("Legs")]
    [Tooltip("Ease the leg into full extension instead of snapping (prevents knee pop).")]
    public bool softLegLimit = true;
    [Range(0.7f, 0.99f)]
    [Tooltip("Fraction of the leg length where the soft limit starts.")]
    public float softLegStart = 0.9f;

    [Header("Toe / heel clipping")]
    [Tooltip("Raise the foot when the toe tip or heel would end up inside the ground.")]
    public bool preventPenetration = true;
    [Tooltip("Distance from the toe joint to the toe tip (m at scale 1).")]
    public float toeTipOffset = 0.05f;
    [Tooltip("Distance from the ankle back to the heel (m at scale 1).")]
    public float heelOffset = 0.07f;
    [Tooltip("Max extra lift this correction may add (m at scale 1).")]
    public float maxPenetrationFix = 0.15f;

    [Header("Disable for jump / fall / swim / climb ...")]
    [Tooltip("Root must have ground within this distance below it, otherwise the character counts as airborne.")]
    public float airborneHeight = 0.35f;
    [Tooltip("Optional bool parameter that is true while grounded. Leave empty to rely only on the ground ray.")]
    public string groundedParameter = "";
    [Tooltip("Animator state tags that switch IK off (checked on all layers, blended through transitions).")]
    public string[] disableTags = { "NoFootIK" };
    [Tooltip("Animator bool parameters that switch IK off while true.")]
    public string[] disableBoolParameters = { "IsSwimming" };
    [Tooltip("If set, IK fades out while the hips are inside a collider/trigger on these layers (water volumes).")]
    public LayerMask waterMask = 0;

    [Header("Debug")]
    public bool drawGizmos = true;

    // ------------------------------------------------------------------ public read-only state
    /// <summary>Current master weight after all fades (0..1).</summary>
    public float CurrentWeight => _global;
    /// <summary>Current hip offset along the up axis (meters).</summary>
    public float PelvisOffset => _pelvis;
    public bool IsSwimming => _swimming;
    public bool IsGrounded => _grounded;

    /// <summary>Call if you swap the Animator Controller at runtime (also detected automatically).</summary>
    public void RefreshParameters() { _paramsDirty = true; }

    // ------------------------------------------------------------------ internals
    class Foot
    {
        public bool isLeft;
        public Transform upper, lower, foot, toes;
        public float legLength;      // at scale 1
        public int curveHash;

        // filtered state
        public float contact;
        public float offset, offsetVel;
        public float lift, liftVel;                  // toe/heel clipping correction
        public Vector3 normal = Vector3.up;
        public Vector3 bend = Vector3.forward;       // smoothed knee bend direction
        public bool hasGround;

        // per-frame samples of the animated pose
        public Vector3 animPos;
        public Quaternion animRot = Quaternion.identity;
        public float sole;

        // outputs
        public Vector3 pos, hintPos;
        public Quaternion rot = Quaternion.identity;
        public float posW, rotW;

        // debug
        public Vector3 rayOrigin, hitPoint, toeProbe, heelProbe;
    }

    Animator _animator;
    Foot _left, _right;
    Transform _hips;
    bool _ready, _failed;
    bool _snap = true;
    bool _idle = true;
    bool _paramsDirty = true;
    RuntimeAnimatorController _lastController;

    int[] _tagHashes = new int[0];
    int[] _boolHashes = new int[0];
    int _groundedHash;

    float _global;
    float _pelvis, _pelvisVel;
    bool _swimming, _grounded;

    static readonly RaycastHit[] s_hits = new RaycastHit[16];
    CharacterController _ownCC;
    Rigidbody _ownRB;
    float _noGroundTime;
    bool _warnedNoGround;

    // Pose cache: lets us undo our own edits when the Animator did not re-evaluate this frame
    // (Animate Physics mode, culled animators, paused time) so offsets never accumulate.
    Transform[] _bones;                       // 0 hips, 1-3 left up/low/foot, 4-6 right up/low/foot
    Vector3 _animHipsPos, _writtenHipsPos;
    readonly Quaternion[] _animRot = new Quaternion[6];
    readonly Quaternion[] _writtenRot = new Quaternion[6];
    bool _hasWritten;

    float Scale => Mathf.Max(0.0001f, transform.lossyScale.y);

    void Awake()
    {
        ResolveAnimator();
    }

    void ResolveAnimator()
    {
        _animator = animator;
        if (_animator == null)
        {
            foreach (Animator a in GetComponentsInChildren<Animator>(true))
                if (a.isHuman) { _animator = a; break; }
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_animator == null) _animator = GetComponentInParent<Animator>();
        }
        _ownCC = GetComponentInParent<CharacterController>();
        _ownRB = GetComponentInParent<Rigidbody>();
    }

    void OnEnable()
    {
        ResetState();
        _warnedNoGround = false;
        _noGroundTime = 0f;
    }

    void OnDisable()
    {
        ResetState();
    }

    void ResetState()
    {
        _snap = true;
        _idle = true;
        _global = 0f;
        _pelvis = 0f;
        _pelvisVel = 0f;
        _paramsDirty = true;
        _hasWritten = false;
    }

    // ------------------------------------------------------------------ setup
    bool EnsureSetup()
    {
        if (_ready) return true;
        if (_failed) return false;

        if (_animator == null) ResolveAnimator();
        if (_animator == null || !_animator.isHuman)
        {
            Debug.LogWarning("FootIK needs an Animator with a Humanoid avatar (on this object or a child).", this);
            _failed = true;
            return false;
        }

        _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        _left = CreateFoot(true);
        _right = CreateFoot(false);
        if (_left == null || _right == null || _hips == null)
        {
            Debug.LogWarning("FootIK: humanoid avatar is missing leg bones.", this);
            _failed = true;
            return false;
        }

        _bones = new[]
        {
            _hips,
            _left.upper, _left.lower, _left.foot,
            _right.upper, _right.lower, _right.foot
        };

        _ready = true;
        return true;
    }

    Foot CreateFoot(bool left)
    {
        Transform upper = _animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
        Transform lower = _animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
        Transform foot = _animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        Transform toes = _animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
        if (upper == null || lower == null || foot == null) return null;

        float len = (lower.position - upper.position).magnitude + (foot.position - lower.position).magnitude;
        return new Foot
        {
            isLeft = left,
            upper = upper,
            lower = lower,
            foot = foot,
            toes = toes,
            legLength = len / Scale,
            bend = Vector3.ProjectOnPlane(transform.forward, transform.up).normalized
        };
    }

    void RefreshParametersNow()
    {
        _paramsDirty = false;
        _lastController = _animator.runtimeAnimatorController;

        var tags = new List<int>();
        if (disableTags != null)
            foreach (string t in disableTags)
                if (!string.IsNullOrEmpty(t)) tags.Add(Animator.StringToHash(t));
        _tagHashes = tags.ToArray();

        var bools = new List<int>();
        _groundedHash = 0;
        _left.curveHash = 0;
        _right.curveHash = 0;

        if (_lastController == null)
        {
            _boolHashes = bools.ToArray();
            return;
        }

        foreach (AnimatorControllerParameter p in _animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool)
            {
                if (disableBoolParameters != null && Array.IndexOf(disableBoolParameters, p.name) >= 0)
                    bools.Add(p.nameHash);
                if (!string.IsNullOrEmpty(groundedParameter) && p.name == groundedParameter)
                    _groundedHash = p.nameHash;
            }
            else if (p.type == AnimatorControllerParameterType.Float && useAnimatorCurves)
            {
                if (p.name == leftCurveParam) _left.curveHash = p.nameHash;
                if (p.name == rightCurveParam) _right.curveHash = p.nameHash;
            }
        }
        _boolHashes = bools.ToArray();
    }

    // ------------------------------------------------------------------ frame update
    void LateUpdate()
    {
        if (!EnsureSetup()) return;
        if (_animator == null || !_animator.isActiveAndEnabled) return;
        if (_paramsDirty || _animator.runtimeAnimatorController != _lastController)
            RefreshParametersNow();

        RestoreOrCapturePose();

        float dt = _animator.updateMode == AnimatorUpdateMode.UnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;

        if (dt > 1e-6f || _snap)
            Compute(dt);

        Apply();
        StorePose();
    }

    void RestoreOrCapturePose()
    {
        bool untouched = _hasWritten &&
                         (_bones[0].localPosition - _writtenHipsPos).sqrMagnitude < 1e-10f;
        for (int i = 0; untouched && i < 6; i++)
            untouched = Quaternion.Dot(_bones[i + 1].localRotation, _writtenRot[i]) > 0.999999f;

        if (untouched)
        {
            // Animator did not run since our last edit -> go back to the animated pose.
            _bones[0].localPosition = _animHipsPos;
            for (int i = 0; i < 6; i++) _bones[i + 1].localRotation = _animRot[i];
        }
        else
        {
            _animHipsPos = _bones[0].localPosition;
            for (int i = 0; i < 6; i++) _animRot[i] = _bones[i + 1].localRotation;
        }
    }

    void StorePose()
    {
        _writtenHipsPos = _bones[0].localPosition;
        for (int i = 0; i < 6; i++) _writtenRot[i] = _bones[i + 1].localRotation;
        _hasWritten = true;
    }

    // ------------------------------------------------------------------ main solve
    void Compute(float dt)
    {
        float s = Scale;
        Vector3 up = transform.up;
        Vector3 plane = transform.position + up * (baseHeightOffset * s);
        float sdt = _snap ? 1000f : dt;      // a huge step == "snap to target"

        // ---- 1. global weight: grounded / swimming / tags / external
        float suppression = ComputeSuppression();
        _swimming = waterMask.value != 0 && _hips != null &&
                    Physics.CheckSphere(_hips.position, 0.08f * s, waterMask, QueryTriggerInteraction.Collide);

        _grounded = RootGround(plane, up, s, out float rootRel);

        if (!_grounded && !_warnedNoGround && externalWeight > 0f && suppression <= 0f && !_swimming)
        {
            _noGroundTime += dt;
            if (_noGroundTime > 1.5f)
            {
                _warnedNoGround = true;
                //Debug.LogWarning("FootIK: no ground found below the character root, so IK stays off. " +
                //                 "Check 'Ground Mask', 'Airborne Height' and 'Base Height Offset' " +
                //                 "(is the pivot at the feet?), and that the ground has a collider.", this);
            }
        }
        else if (_grounded) _noGroundTime = 0f;

        float target = externalWeight * (1f - suppression);
        if (_swimming || !_grounded) target = 0f;
        target = Mathf.Clamp01(target);

        if (target <= 0f && _global <= 0f && Mathf.Abs(_pelvis) < 1e-4f)
        {
            // Fully off: skip all raycasts, keep outputs neutral.
            _idle = true;
            _pelvis = 0f;
            _pelvisVel = 0f;
            _snap = false;
            ClearOutputs(_left);
            ClearOutputs(_right);
            return;
        }

        bool feetSnap = _snap || _idle;
        _idle = false;
        float feetDt = feetSnap ? 1000f : dt;

        _global = Mathf.MoveTowards(_global, target, sdt / Mathf.Max(blendTime, 0.001f));
        float g = _global;

        // ---- 2. sample both feet (plant weight, ground offset, normal)
        SampleFoot(_left, plane, up, s, feetDt);
        SampleFoot(_right, plane, up, s, feetDt);

        // ---- 3. hips: drop to the lowest planted ground, rise with the average of planted feet
        float want = 0f;
        if (adjustPelvis)
        {
            float drop = Mathf.Min(0f, rootRel);
            drop = Mathf.Min(drop, Mathf.Min(0f, _left.offset) * _left.contact);
            drop = Mathf.Min(drop, Mathf.Min(0f, _right.offset) * _right.contact);
            drop = Mathf.Max(drop * pelvisDropScale, -maxPelvisDrop * s);

            float raise = 0f;
            float cSum = _left.contact + _right.contact;
            if (cSum > 0.05f)
            {
                float avg = (Mathf.Max(0f, _left.offset) * _left.contact +
                             Mathf.Max(0f, _right.offset) * _right.contact) / cSum;
                raise = Mathf.Min(avg * pelvisRaise, maxPelvisRaise * s) * Mathf.Clamp01(cSum);
            }
            want = (drop + raise) * g;
        }
        _pelvis = Mathf.SmoothDamp(_pelvis, want, ref _pelvisVel, Mathf.Max(pelvisTime, 0.001f), Mathf.Infinity, sdt);

        // ---- 4. final targets
        ResolveFoot(_left, up, s, g, feetDt);
        ResolveFoot(_right, up, s, g, feetDt);

        _snap = false;
    }

    float ComputeSuppression()
    {
        float result = 0f;

        if (_tagHashes.Length > 0)
        {
            int layers = _animator.layerCount;
            for (int i = 0; i < layers; i++)
            {
                float lw = i == 0 ? 1f : _animator.GetLayerWeight(i);
                if (lw <= 0f) continue;

                float v = HasTag(_animator.GetCurrentAnimatorStateInfo(i).tagHash) ? 1f : 0f;
                if (_animator.IsInTransition(i))
                {
                    float next = HasTag(_animator.GetNextAnimatorStateInfo(i).tagHash) ? 1f : 0f;
                    float t = Mathf.Clamp01(_animator.GetAnimatorTransitionInfo(i).normalizedTime);
                    v = Mathf.Lerp(v, next, t);
                }
                result = Mathf.Max(result, v * lw);
            }
        }

        for (int i = 0; i < _boolHashes.Length; i++)
            if (_animator.GetBool(_boolHashes[i])) { result = 1f; break; }

        return result;
    }

    bool HasTag(int hash)
    {
        if (hash == 0) return false;
        for (int i = 0; i < _tagHashes.Length; i++)
            if (_tagHashes[i] == hash) return true;
        return false;
    }

    // Nearest ground hit that is not part of this character (works even if character and
    // ground share a layer).
    bool CastGround(Vector3 origin, Vector3 dir, float len, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(origin, dir, s_hits, len, groundMask, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = s_hits[i];
            Collider c = h.collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (_ownCC != null && c == _ownCC) continue;
            if (_ownRB != null && c.attachedRigidbody == _ownRB) continue;
            if (_animator != null && c.transform.IsChildOf(_animator.transform)) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        return found;
    }

    // Ground under the character root. Also tells us how far the root hovers above the ground
    // (CharacterController skin width etc.), so the hips can sit the model on the floor.
    bool RootGround(Vector3 plane, Vector3 up, float s, out float rootRel)
    {
        rootRel = 0f;
        if (_groundedHash != 0 && !_animator.GetBool(_groundedHash)) return false;

        float upLen = maxStepUp * s;
        Vector3 origin = plane + up * upLen;
        float len = upLen + airborneHeight * s;
        if (!CastGround(origin, -up, len, out RaycastHit hit))
            return false;

        rootRel = Mathf.Clamp(Vector3.Dot(hit.point - plane, up), -maxStepDown * s, upLen);
        return true;
    }

    void SampleFoot(Foot f, Vector3 plane, Vector3 up, float s, float dt)
    {
        // Animated pose of the ankle (read before we touch any bone this frame).
        f.animPos = f.foot.position;
        f.animRot = f.foot.rotation;

        float bottom = f.isLeft ? _animator.leftFeetBottomHeight : _animator.rightFeetBottomHeight;
        f.sole = Mathf.Max(0f, bottom) + footHeightOffset * s;

        // Sole clearance above the animation plane; toe joint included so toe-off stays planted.
        float animBottom = Vector3.Dot(f.animPos - plane, up) - f.sole;
        float clearance = animBottom;
        if (f.toes != null)
        {
            float toeClear = Vector3.Dot(f.toes.position - plane, up) - toeHeightOffset * s;
            clearance = Mathf.Min(clearance, toeClear);
        }

        float t = Mathf.InverseLerp(plantedHeight * s, Mathf.Max(liftedHeight, plantedHeight + 0.001f) * s, clearance);
        t = t * t * (3f - 2f * t);
        float contactTarget = 1f - t;
        if (useAnimatorCurves && f.curveHash != 0)
            contactTarget *= Mathf.Clamp01(_animator.GetFloat(f.curveHash));
        f.contact = Mathf.MoveTowards(f.contact, contactTarget, contactBlendSpeed * dt);

        // Ground under the animated foot. The ray covers [plane - maxStepDown, plane + maxStepUp].
        float b = Mathf.Max(0f, animBottom);
        Vector3 onPlane = f.animPos - up * (animBottom + f.sole);     // foot bottom projected to the plane
        Vector3 origin = onPlane + up * (b + maxStepUp * s);
        float len = b + (maxStepUp + maxStepDown) * s;
        f.rayOrigin = origin;

        float rawOffset = 0f;
        Vector3 nTarget = up;
        f.hasGround = false;
        if (CastGround(origin, -up, len, out RaycastHit hit))
        {
            f.hasGround = true;
            f.hitPoint = hit.point;
            // A steep normal from a downward ray means we grazed a step edge / riser, not a wall:
            // the height is still valid (it is what places the foot), only the tilt is ignored.
            nTarget = Vector3.Angle(hit.normal, up) <= maxGroundSlope ? hit.normal : up;
            rawOffset = Mathf.Clamp(Vector3.Dot(hit.point - plane, up), -maxStepDown * s, maxStepUp * s);
        }

        float smoothTime = rawOffset > f.offset ? footRaiseTime : footLowerTime;
        f.offset = Mathf.SmoothDamp(f.offset, rawOffset, ref f.offsetVel, Mathf.Max(smoothTime, 0.001f), Mathf.Infinity, dt);

        float k = 1f - Mathf.Exp(-normalSpeed * dt);
        Vector3 n = Vector3.Slerp(f.normal, nTarget, k);
        f.normal = n.sqrMagnitude > 1e-6f ? n.normalized : up;
    }

    void ResolveFoot(Foot f, Vector3 up, float s, float g, float dt)
    {
        // Terrain shift: lowering only when planted, raising also during the swing (clearance).
        float groundShift = f.offset >= 0f
            ? f.offset * Mathf.Lerp(swingLift, 1f, f.contact)
            : f.offset * f.contact;
        // Swing feet ride with the hips so the animated swing keeps its shape.
        float shift = groundShift * g + _pelvis * (1f - f.contact);

        Vector3 target = f.animPos + up * shift;

        // Rotation: keep the animated foot, tilt it to the (smoothed, clamped) ground slope.
        Quaternion rot = f.animRot;
        float slopeAmount = 0f;
        if (alignFootToGround)
        {
            Quaternion slope = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(up, f.normal), maxFootAngle);
            float rw = f.contact * g;
            rot = Quaternion.Slerp(f.animRot, slope * f.animRot, rw);
            slopeAmount = Quaternion.Angle(Quaternion.identity, slope) * rw;   // ~1 degree == full weight
        }

        // ---- toe / heel penetration check (uses the final foot rotation)
        float pen = 0f;
        if (preventPenetration)
        {
            Vector3 fwd = f.toes != null ? Vector3.ProjectOnPlane(f.toes.position - f.foot.position, up) : Vector3.zero;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(transform.forward, up);
            fwd.Normalize();

            // sole points of the ANIMATED foot: toe tip and heel
            Vector3 toeAnim = f.toes != null
                ? f.toes.position + fwd * (toeTipOffset * s) - up * (toeHeightOffset * s)
                : f.animPos + fwd * (0.17f * s) - up * f.sole;
            Vector3 heelAnim = f.animPos - fwd * (heelOffset * s) - up * f.sole;

            Quaternion inv = Quaternion.Inverse(f.animRot);
            Vector3 toeNow = target + rot * (inv * (toeAnim - f.animPos));
            Vector3 heelNow = target + rot * (inv * (heelAnim - f.animPos));
            f.toeProbe = toeNow;
            f.heelProbe = heelNow;

            pen = Mathf.Max(Penetration(toeNow, up, s), Penetration(heelNow, up, s));
            pen = Mathf.Min(pen, maxPenetrationFix * s);
        }
        float liftTime = pen > f.lift ? 0.03f : 0.12f;      // rise fast (no visible clipping), settle slowly
        f.lift = Mathf.SmoothDamp(f.lift, pen, ref f.liftVel, liftTime, Mathf.Infinity, dt);
        float lift = f.lift * g;
        target += up * lift;

        // ---- soft leg-extension limit (after all offsets)
        Vector3 hipAnim = f.upper.position;
        Vector3 hip = hipAnim + up * _pelvis;
        float L = f.legLength * s;
        if (softLegLimit)
        {
            Vector3 d = target - hip;
            float dT = d.magnitude;
            float dA = (f.animPos - hipAnim).magnitude;     // distance the clip itself uses
            float start = L * softLegStart;
            if (dT > dA && dT > start && dT > 1e-5f)
            {
                float range = Mathf.Max(L - start, 1e-4f);
                float soft = start + range * (1f - Mathf.Exp(-(dT - start) / range));
                float dF = Mathf.Max(dA, Mathf.Min(soft, dT));
                dF = Mathf.Min(dF, Mathf.Max(dA, L * 0.9995f));
                target = hip + d * (dF / dT);
            }
        }

        // ---- knee bend direction from the animated pose, smoothed (stable bend plane)
        Vector3 axis = f.animPos - hipAnim;
        float axisLen = axis.magnitude;
        if (axisLen > 1e-4f)
        {
            Vector3 kneeV = f.lower.position - hipAnim;
            Vector3 bendV = kneeV - axis * (Vector3.Dot(kneeV, axis) / (axisLen * axisLen));
            if (bendV.magnitude > 0.01f * L)
            {
                float k = 1f - Mathf.Exp(-20f * dt);
                f.bend = Vector3.Slerp(f.bend, bendV.normalized, k).normalized;
            }
        }
        f.hintPos = (hip + target) * 0.5f + f.bend * (L * 0.5f);   // debug only

        // IK is only engaged when it actually changes something -> flat ground == pure animation.
        float presence = Mathf.Clamp01(Mathf.Max(Mathf.Max(Mathf.Abs(shift), Mathf.Abs(lift)), Mathf.Abs(_pelvis)) / (0.004f * s));

        f.pos = target;
        f.rot = rot;
        f.posW = presence;
        f.rotW = Mathf.Clamp01(Mathf.Max(presence, slopeAmount));
    }

    // How far (along up) the ground is ABOVE a sole point, i.e. how deep the point is buried.
    float Penetration(Vector3 p, Vector3 up, float s)
    {
        float upLen = maxStepUp * s;
        if (!CastGround(p + up * upLen, -up, upLen + 0.03f * s, out RaycastHit hit)) return 0f;
        return Mathf.Clamp(Vector3.Dot(hit.point - p, up), 0f, upLen);
    }

    void ClearOutputs(Foot f)
    {
        f.posW = 0f;
        f.rotW = 0f;
    }

    // ------------------------------------------------------------------ write to the skeleton
    void Apply()
    {
        // Hips first: the whole body (including both legs) moves rigidly with the hips bone.
        if (Mathf.Abs(_pelvis) > 1e-5f)
            _hips.position += transform.up * _pelvis;

        SolveLeg(_left);
        SolveLeg(_right);
    }

    // Analytic two-bone IK: ankle -> f.pos, knee towards f.bend, foot orientation -> f.rot.
    void SolveLeg(Foot f)
    {
        if (f.posW <= 0f && f.rotW <= 0f) return;

        if (f.posW > 0f)
        {
            Transform a = f.upper, b = f.lower, c = f.foot;
            Quaternion aRot0 = a.rotation, bRot0 = b.rotation;
            Vector3 aPos = a.position, bPos = b.position, cPos = c.position;

            float lab = (bPos - aPos).magnitude;
            float lbc = (cPos - bPos).magnitude;
            Vector3 d = f.pos - aPos;
            float dl = d.magnitude;

            if (dl > 1e-5f && lab > 1e-5f && lbc > 1e-5f)
            {
                Vector3 dir = d / dl;
                float dist = Mathf.Clamp(dl, Mathf.Abs(lab - lbc) + 1e-3f, lab + lbc - 1e-4f);

                float along = (lab * lab - lbc * lbc + dist * dist) / (2f * dist);
                float h = Mathf.Sqrt(Mathf.Max(lab * lab - along * along, 0f));

                Vector3 pole = Vector3.ProjectOnPlane(f.bend, dir);
                if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(bPos - aPos, dir);

                if (pole.sqrMagnitude > 1e-8f)
                {
                    pole.Normalize();
                    Vector3 knee = aPos + dir * along + pole * h;
                    Vector3 ankle = aPos + dir * dist;

                    a.rotation = Quaternion.FromToRotation(bPos - aPos, knee - aPos) * a.rotation;
                    b.rotation = Quaternion.FromToRotation(c.position - b.position, ankle - b.position) * b.rotation;

                    if (f.posW < 1f)
                    {
                        a.rotation = Quaternion.Slerp(aRot0, a.rotation, f.posW);
                        b.rotation = Quaternion.Slerp(bRot0, b.rotation, f.posW);
                    }
                }
            }
        }

        if (f.rotW > 0f)
            f.foot.rotation = Quaternion.Slerp(f.foot.rotation, f.rot, f.rotW);
    }

    // ------------------------------------------------------------------ gizmos
    // Work in Edit mode too (select the object): they show the animation plane, the sole points
    // of the current pose and where the ground is found. In Play mode they show the solver result.
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        if (Application.isPlaying && _ready && _left != null && _right != null)
        {
            DrawFootGizmo(_left);
            DrawFootGizmo(_right);
            return;
        }
        DrawEditPreview();
    }

    void DrawFootGizmo(Foot f)
    {
        Gizmos.color = f.hasGround ? Color.green : Color.gray;
        Gizmos.DrawLine(f.rayOrigin, f.hasGround ? f.hitPoint : f.rayOrigin - transform.up * 0.2f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(f.animPos, 0.03f);
        Gizmos.color = Color.Lerp(Color.yellow, Color.cyan, f.posW);
        Gizmos.DrawWireSphere(f.pos, 0.04f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(f.toeProbe, 0.015f);
        Gizmos.DrawWireSphere(f.heelProbe, 0.015f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(f.lower.position, f.hintPos);
    }

    void DrawEditPreview()
    {
        Animator a = animator;
        if (a == null)
            foreach (Animator c in GetComponentsInChildren<Animator>(true))
                if (c.isHuman) { a = c; break; }
        if (a == null || !a.isHuman) return;

        Physics.SyncTransforms();
        _ownCC = GetComponentInParent<CharacterController>();
        _ownRB = GetComponentInParent<Rigidbody>();
        if (_animator == null) _animator = a;

        float s = Scale;
        Vector3 up = transform.up;
        Vector3 plane = transform.position + up * (baseHeightOffset * s);

        Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
        Gizmos.DrawLine(plane - transform.right * 0.4f * s, plane + transform.right * 0.4f * s);
        Gizmos.DrawLine(plane - transform.forward * 0.4f * s, plane + transform.forward * 0.4f * s);

        DrawEditFoot(a, true, plane, up, s);
        DrawEditFoot(a, false, plane, up, s);
    }

    void DrawEditFoot(Animator a, bool left, Vector3 plane, Vector3 up, float s)
    {
        Transform foot = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        Transform toes = a.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
        if (foot == null) return;

        float bottom = Mathf.Max(0f, left ? a.leftFeetBottomHeight : a.rightFeetBottomHeight) + footHeightOffset * s;
        Vector3 sole = foot.position - up * bottom;
        float animBottom = Vector3.Dot(sole - plane, up);
        float b = Mathf.Max(0f, animBottom);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(sole, 0.025f);

        Vector3 origin = sole - up * animBottom + up * (b + maxStepUp * s);
        float len = b + (maxStepUp + maxStepDown) * s;
        if (CastGround(origin, -up, len, out RaycastHit hit))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, hit.point);
            Gizmos.DrawWireSphere(hit.point, 0.02f);
            Gizmos.DrawLine(hit.point, hit.point + hit.normal * 0.15f);
        }
        else
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(origin, origin - up * len);
        }

        Vector3 fwd = toes != null ? Vector3.ProjectOnPlane(toes.position - foot.position, up) : Vector3.zero;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(transform.forward, up);
        fwd.Normalize();
        Vector3 toeTip = toes != null
            ? toes.position + fwd * (toeTipOffset * s) - up * (toeHeightOffset * s)
            : foot.position + fwd * (0.17f * s) - up * bottom;
        Vector3 heel = foot.position - fwd * (heelOffset * s) - up * bottom;
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(toeTip, 0.015f);
        Gizmos.DrawWireSphere(heel, 0.015f);
    }
}
