using Assets._Project.Features.Boat;
using Assets._Project.Features.Duceree;
using Assets._Project.Features.StatusViewModels;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Death transition and checkpoint-based recovery for the player and boats.
[DisallowMultipleComponent]
public sealed class PlayerRespawn : MonoBehaviour
{
    [Tooltip("HUD that plays the death and rebirth transitions. Without it, respawn is immediate.")]
    public PlayerHud Hud;
    public float DeathDelay = 2.2f;
    [Tooltip("Time for the player to get up before control is returned.")]
    public float RiseTime = 2.4f;

    private PlayerController _player;
    private Pose _defaultPlayerPose;
    private readonly List<BoatSnapshot> _boatSnapshots = new List<BoatSnapshot>();
    private SpawnZone _activeSpawnZone;
    private BoatController _activeSpawnBoat;
    private bool _respawning;

    private struct BoatSnapshot
    {
        public BoatController Boat;
        public Pose Pose;

        public BoatSnapshot(BoatController boat, Pose pose)
        {
            Boat = boat;
            Pose = pose;
        }
    }

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _defaultPlayerPose = new Pose(transform.position, transform.rotation);
    }

    private void Start()
    {
        CacheSceneBoats();

        if (_player != null && _player.Health != null)
            _player.Health.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (_player != null && _player.Health != null)
            _player.Health.Died -= OnDied;
    }

    /// <summary>Called by a SpawnZone when the player enters its trigger volume.</summary>
    public void SetSpawnZone(SpawnZone zone)
    {
        if (zone == null || _respawning || zone == _activeSpawnZone)
            return;

        _activeSpawnZone = zone;
        _activeSpawnBoat = zone.Boat != null
            ? zone.Boat
            : (_player != null && _player.CurrentBoat != null ? _player.CurrentBoat : GetFirstSceneBoat());

        if (_activeSpawnBoat != null)
            CacheBoat(_activeSpawnBoat);
        else if (zone.BoatSpawnPoint != null)
            Debug.LogWarning("SpawnZone has a BoatSpawnPoint, but no BoatController could be resolved. Assign its Boat field.", zone);
    }

    private void OnDied()
    {
        if (!_respawning)
            StartCoroutine(Respawn());
    }

    private IEnumerator Respawn()
    {
        _respawning = true;
        _player.InputLocked = true;

        // Detach before moving either object so the player cannot remain parented
        // to a boat that is being teleported to a different checkpoint.
        if (_player.IsAboardBoat)
            _player.LeaveBoat(null);

        if (_player.SwimmingMovement != null)
            _player.SwimmingMovement.SetDrowning(true);

        yield return new WaitForSeconds(DeathDelay);

        if (Hud != null)
            yield return Hud.PlayDeath();

        ResetWorld();

        if (DucereeTree.Instance != null)
            DucereeTree.Instance.LightStar();

        if (Hud != null)
            yield return Hud.PlayRebirth();

        yield return new WaitForSeconds(RiseTime);

        _player.InputLocked = false;
        _respawning = false;
    }

    private void CacheSceneBoats()
    {
        BoatController[] boats = FindObjectsByType<BoatController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < boats.Length; i++)
            CacheBoat(boats[i]);
    }

    private void CacheBoat(BoatController boat)
    {
        if (boat == null)
            return;

        for (int i = 0; i < _boatSnapshots.Count; i++)
        {
            if (_boatSnapshots[i].Boat == boat)
                return;
        }

        _boatSnapshots.Add(new BoatSnapshot(
            boat,
            new Pose(boat.transform.position, boat.transform.rotation)));
    }

    private BoatController GetFirstSceneBoat()
    {
        for (int i = 0; i < _boatSnapshots.Count; i++)
        {
            if (_boatSnapshots[i].Boat != null)
                return _boatSnapshots[i].Boat;
        }

        return null;
    }

    private void ResetWorld()
    {
        // Re-select grounded movement and clear its motor state through the
        // movement API. CharacterController.velocity is read-only; do not try
        // to assign velocity/linearVelocity to the CharacterController.
        if (_player.IsAboardBoat)
            _player.LeaveBoat(null);

        if (_player.SwimmingMovement != null)
        {
            _player.SwimmingMovement.SetDrowning(false);
            _player.SwimmingMovement.SetDiveInput(false);
        }

        _player.ExitSwimming();

        if (_player.GroundedMovement != null)
        {
            _player.GroundedMovement.SetPlanarVelocity(Vector3.zero);
            _player.GroundedMovement.ResetVerticalVelocity();
        }

        if (_player.SwimmingMovement != null)
        {
            _player.SwimmingMovement.SetPlanarVelocity(Vector3.zero);
            _player.SwimmingMovement.ResetVerticalVelocity();
        }

        if (_player.BoatMovement != null)
        {
            _player.BoatMovement.StopDriving();
            _player.BoatMovement.ClearBoat();
        }

        Pose playerPose = _activeSpawnZone != null
            ? _activeSpawnZone.GetPlayerSpawnPose()
            : _defaultPlayerPose;

        CharacterController controller = _player.CharacterController;
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controller != null)
            controller.enabled = false;

        transform.SetPositionAndRotation(playerPose.position, playerPose.rotation);

        // Return every non-checkpoint boat to its original pose. The active
        // checkpoint boat receives the zone-specific boat pose instead.
        for (int i = 0; i < _boatSnapshots.Count; i++)
        {
            BoatSnapshot snapshot = _boatSnapshots[i];
            BoatController boat = snapshot.Boat;
            if (boat == null)
                continue;

            Pose boatPose = snapshot.Pose;
            if (_activeSpawnZone != null && boat == _activeSpawnBoat &&
                _activeSpawnZone.TryGetBoatSpawnPose(boat, out Pose checkpointBoatPose))
            {
                boatPose = checkpointBoatPose;
            }

            RestoreBoatPose(boat, boatPose);
        }

        Physics.SyncTransforms();

        if (controller != null)
            controller.enabled = controllerWasEnabled;

        _player.Health.Fill();
        _player.Stamina.Fill();
    }

    private static void RestoreBoatPose(BoatController boat, Pose pose)
    {
        Rigidbody body = boat.Body;

        // Clear momentum before moving the Rigidbody. A kinematic body cannot
        // accept velocity changes, so only write to velocity on dynamic bodies.
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        if (body != null)
        {
            body.position = pose.position;
            body.rotation = pose.rotation;
        }

        boat.transform.SetPositionAndRotation(pose.position, pose.rotation);
        Physics.SyncTransforms();

        // Re-anchor after moving the Rigidbody and its Transform together so the
        // boat's anchor state and physics pose agree at the checkpoint.
        boat.Anchor();

        // Anchor() may change the body's mode. If it remains dynamic, clear any
        // residual motion once more and put it to sleep at the restored pose.
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }
    }
}
