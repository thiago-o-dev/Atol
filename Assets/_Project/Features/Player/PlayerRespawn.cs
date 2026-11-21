using Assets._Project.Features.Boat;
using Assets._Project.Features.Duceree;
using Assets._Project.Features.StatusViewModels;
using System.Collections;
using UnityEngine;

// Morte do player: a estrela do HUD atravessa a tela, tudo escurece, ele volta pra onde nasceu (embaixo da
// Duceree, que ganha mais uma estrela), os barcos voltam pro lugar e o mundo decai um nível.
[DisallowMultipleComponent]
public sealed class PlayerRespawn : MonoBehaviour
{
    [Tooltip("HUD que faz a transição da morte. Sem ele o renascimento é seco.")]
    public PlayerHud Hud;
    public float DeathDelay = 2.2f;
    [Tooltip("Tempo pra ela se levantar antes de devolver o controle.")]
    public float RiseTime = 2.4f;

    private PlayerController _player;
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private BoatController[] _boats;
    private Pose[] _boatPoses;
    private bool _respawning;

    private void Awake()
    {
        _player = GetComponent<PlayerController>();
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;
    }

    private void Start()
    {
        _boats = FindObjectsByType<BoatController>(FindObjectsInactive.Exclude);
        _boatPoses = new Pose[_boats.Length];

        for (int i = 0; i < _boats.Length; i++)
            _boatPoses[i] = new Pose(_boats[i].transform.position, _boats[i].transform.rotation);

        _player.Health.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (_player && _player.Health)
            _player.Health.Died -= OnDied;
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

        if (_player.IsAboardBoat)
            _player.LeaveBoat(null);

        if (_player.SwimmingMovement)
            _player.SwimmingMovement.SetDrowning(true);

        yield return new WaitForSeconds(DeathDelay);

        if (Hud)
            yield return Hud.PlayDeath();

        ResetWorld();

        if (DucereeTree.Instance)
            DucereeTree.Instance.LightStar();

        if (Hud)
            yield return Hud.PlayRebirth();

        yield return new WaitForSeconds(RiseTime);

        _player.InputLocked = false;
        _respawning = false;
    }

    private void ResetWorld()
    {
        CharacterController controller = _player.CharacterController;

        if (controller)
            controller.enabled = false;

        transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
        Physics.SyncTransforms();

        if (controller)
            controller.enabled = true;

        if (_player.SwimmingMovement)
            _player.SwimmingMovement.SetDrowning(false);

        _player.ExitSwimming();

        for (int i = 0; i < _boats.Length; i++)
        {
            BoatController boat = _boats[i];

            if (boat == null || boat.Body == null)
                continue;

            boat.Body.linearVelocity = Vector3.zero;
            boat.Body.angularVelocity = Vector3.zero;
            boat.Body.position = _boatPoses[i].position;
            boat.Body.rotation = _boatPoses[i].rotation;
            boat.transform.SetPositionAndRotation(_boatPoses[i].position, _boatPoses[i].rotation);
            boat.Anchor();
        }

        WorldStateController world = WorldStateController.Instance;

        if (world)
            world.UpdateDecay(world.Decay + 1);

        _player.Health.Fill();
        _player.Stamina.Fill();
    }
}
