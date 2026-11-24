using Assets._Project.Features.Boat;
using UnityEngine;

/// <summary>
/// A checkpoint volume that updates the player's respawn pose when entered.
/// Each SpawnZone can provide separate player and boat destination transforms.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class SpawnZone : MonoBehaviour
{
    [Header("Respawn Positions")]
    [Tooltip("Where the player will respawn after activating this zone. Defaults to this object's transform.")]
    public Transform PlayerSpawnPoint;

    [Tooltip("Boat that should be moved to this zone's boat position. If empty, the player's current boat is used, or the first scene boat.")]
    public BoatController Boat;

    [Tooltip("Where the selected boat will be moved on respawn. If empty, that boat returns to its original scene position.")]
    public Transform BoatSpawnPoint;

    private void Reset()
    {
        EnsureTrigger();
    }

    private void OnValidate()
    {
        EnsureTrigger();
    }

    private void Awake()
    {
        EnsureTrigger();
    }

    private void EnsureTrigger()
    {
        BoxCollider trigger = GetComponent<BoxCollider>();
        if (trigger != null)
            trigger.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        PlayerRespawn respawn = player.GetComponent<PlayerRespawn>();
        if (respawn != null)
            respawn.SetSpawnZone(this);
    }

    /// <summary>Gets the world-space player pose for this checkpoint.</summary>
    public Pose GetPlayerSpawnPose()
    {
        Transform point = PlayerSpawnPoint != null ? PlayerSpawnPoint : transform;
        return new Pose(point.position, point.rotation);
    }

    /// <summary>Gets the boat pose when this zone has a boat position for the supplied boat.</summary>
    public bool TryGetBoatSpawnPose(BoatController boat, out Pose pose)
    {
        if (BoatSpawnPoint != null && (Boat == null || Boat == boat))
        {
            pose = new Pose(BoatSpawnPoint.position, BoatSpawnPoint.rotation);
            return true;
        }

        pose = default;
        return false;
    }
}
