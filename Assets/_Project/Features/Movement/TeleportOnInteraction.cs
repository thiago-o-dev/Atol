using Assets._Project.Framework.Logging;
using UnityEngine;

public class TeleportOnInteraction : MonoBehaviour
{
    public Transform Target;

    private FrameworkLogger _log;

    public void Awake()
    {
        _log = new(this, true, prefixColor: Color.cyan);
    }

    public void Interact(CharacterController body)
    {
        if (!Target)
        {
            _log.Error("No target set to teleport towards");
            return;
        }

        body.enabled = false;

        body.transform.position = Target.position;

        Physics.SyncTransforms();

        body.enabled = true;

        _log.Log($"Teleported {body.gameObject.name} to {Target.position}");
    }
}
