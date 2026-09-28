using Assets._Project.Framework.Logging;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FloatingObject : MonoBehaviour
{
    [Header("Water")]
    [Tooltip("Object holding the water plane. Its Y position is used as the sea level.")]
    public Transform WaterSurface;

    [Header("Float Points")]
    [Tooltip("Points where buoyancy is applied. Spread them around the object's hull. If empty, the object's own position is used.")]
    public Transform[] FloatPoints;

    [Header("Buoyancy")]
    [Tooltip("How many times gravity pushes the object up when fully submerged. Above 1 floats, below 1 sinks.")]
    public float BuoyancyStrength = 2.5f;
    [Tooltip("Depth, in meters, at which a float point counts as fully submerged.")]
    public float SubmergedDepth = 1f;

    [Header("Drag")]
    public float WaterLinearDamping = 2f;
    public float WaterAngularDamping = 1.5f;

    public bool ShowLogs = true;

    public bool IsInWater { get; private set; }

    private Rigidbody _rigidbody;
    private FrameworkLogger _log;

    private float _airLinearDamping;
    private float _airAngularDamping;

    private void Awake()
    {
        _log = new(this, showLogs: ShowLogs, prefixColor: Color.cyan);

        _rigidbody = GetComponent<Rigidbody>();

        _airLinearDamping = _rigidbody.linearDamping;
        _airAngularDamping = _rigidbody.angularDamping;

        if (FloatPoints == null || FloatPoints.Length == 0)
            FloatPoints = new[] { transform };
    }

    private void Start()
    {
        if (!WaterController.Instance)
            _log.Error("No WaterController in the scene, object will not float");
    }

    private void FixedUpdate()
    {
        if (!WaterController.Instance)
            return;

        int submergedPoints = 0;

        foreach (Transform point in FloatPoints)
        {
            float depth = GetWaterHeight(point.position) - point.position.y;

            if (depth <= 0f)
                continue;

            submergedPoints++;

            float submersion = Mathf.Clamp01(depth / SubmergedDepth);

            Vector3 buoyancy = -Physics.gravity * (BuoyancyStrength * submersion / FloatPoints.Length);

            _rigidbody.AddForceAtPosition(buoyancy, point.position, ForceMode.Acceleration);
        }

        IsInWater = submergedPoints > 0;

        _rigidbody.linearDamping = IsInWater ? WaterLinearDamping : _airLinearDamping;
        _rigidbody.angularDamping = IsInWater ? WaterAngularDamping : _airAngularDamping;
    }

    private float GetWaterHeight(Vector3 position)
    {
        float seaLevel = WaterSurface ? WaterSurface.position.y : 0f;

        return seaLevel + WaterController.Instance.GetHeightAtPosition(position);
    }

    private void OnDrawGizmosSelected()
    {
        Transform[] points = FloatPoints != null && FloatPoints.Length > 0 ? FloatPoints : new[] { transform };

        foreach (Transform point in points)
        {
            if (!point)
                continue;

            bool isSubmerged = Application.isPlaying && WaterController.Instance && GetWaterHeight(point.position) > point.position.y;

            Gizmos.color = isSubmerged ? Color.blue : Color.white;
            Gizmos.DrawSphere(point.position, 0.15f);
        }
    }
}
