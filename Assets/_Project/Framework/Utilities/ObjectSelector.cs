using Assets._Project.Framework.Architecture;
using Assets._Project.Framework.Logging;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class ObjectSelector : Singleton<ObjectSelector>
{
    public bool ShowLogs = true;
    [Header("Input")]
    [SerializeField] private Camera _camera;
    [SerializeField] private InputActionReference _pointAction;
    [SerializeField] private InputActionReference _clickAction;

    [Header("Events")]
    public UnityEvent<GameObject, bool> OnHovered;
    public UnityEvent<GameObject> OnPressed;
    public UnityEvent<GameObject> OnReleased;

    public GameObject HoveredObject { get; private set; }
    public GameObject PressedObject { get; private set; }

    private FrameworkLogger _log;
    protected override void SingletonAwake()
    {
        _log = new(this, showLogs: ShowLogs, prefixColor: Color.darkGreen);
    }

    private void OnEnable()
    {
        if (_pointAction == null)
        {
            _log.Error("Point Action is not assigned!");
            return;
        }

        if (_clickAction == null)
        {
            _log.Error("Click Action is not assigned!");
            return;
        }

        _log.Debug(
            $"Point Action: {_pointAction.action.name}, " +
            $"Enabled: {_pointAction.action.enabled}"
        );

        _log.Debug(
            $"Click Action: {_clickAction.action.name}, " +
            $"Enabled: {_clickAction.action.enabled}"
        );

        _pointAction.action.Enable();
        _clickAction.action.Enable();

        _clickAction.action.performed += OnClickPerformed;
        _clickAction.action.canceled += OnClickCanceled;
    }

    private void OnDisable()
    {
        if (_clickAction != null)
        {
            _clickAction.action.performed -= OnClickPerformed;
            _clickAction.action.canceled -= OnClickCanceled;

            _clickAction.action.Disable();
        }

        if (_pointAction != null)
        {
            _pointAction.action.Disable();
        }
    }

    private void Update()
    {
        UpdateHover();
    }

    private void UpdateHover()
    {
        if (_camera == null)
            return;

        Vector2 screenPosition = _pointAction.action.ReadValue<Vector2>();

        Ray ray = _camera.ScreenPointToRay(screenPosition);

        GameObject newHoveredObject = null;

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            newHoveredObject = hit.collider.gameObject;
        }

        // Nothing changed.
        if (newHoveredObject == HoveredObject)
            return;

        // Stop hovering previous object.
        if (HoveredObject != null)
        {
            _log.Debug($"Stopped hovering: {HoveredObject.name}");

            OnHovered?.Invoke(HoveredObject, false);
        }

        HoveredObject = newHoveredObject;

        // Start hovering new object.
        if (HoveredObject != null)
        {
            _log.Debug($"Started hovering: {HoveredObject.name}");

            OnHovered?.Invoke(HoveredObject, true);
        }
    }

    public void OnClickPerformed(InputAction.CallbackContext context)
    {
        if (_clickAction.action.IsPressed())
        {
            OnClickStarted(context);
        }
        else
        {
            OnClickCanceled(context);
        }
    }
    private void OnClickStarted(InputAction.CallbackContext context)
    {
        _log.Debug(
            $"Pressed! " +
            $"Action={context.action.name}, " +
            $"Control={context.control?.displayName}"
        );

        if (HoveredObject == null)
        {
            _log.Debug("Pressed, but no object is hovered.");
            return;
        }

        PressedObject = HoveredObject;

        _log.Debug($"Pressed object: {PressedObject.name}");

        OnPressed?.Invoke(PressedObject);
    }

    private void OnClickCanceled(InputAction.CallbackContext context)
    {
        _log.Debug(
            $"Released! " +
            $"Action={context.action.name}, " +
            $"Control={context.control?.displayName}"
        );

        if (PressedObject == null)
            return;

        GameObject releasedObject = PressedObject;

        _log.Debug($"Released object: {releasedObject.name}");

        OnReleased?.Invoke(releasedObject);

        PressedObject = null;
    }
}