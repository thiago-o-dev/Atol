using Assets._Project.Framework.Logging;
using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class SelectableComponent : MonoBehaviour
{
    private FrameworkLogger _log;
    private Outline _outline;

    [Header("Outline Settings")]
    public float OutlineWidth = 7f;
    public float InactiveOutlineWidth = 3f;
    public bool IsMaintainOutlineOnNoInteraction = false;
    public Color NoInteractionHintColor = Color.cyan;
    public Color SelectionHintColor = Color.orange;
    public Color PressedHintColor = Color.yellow;
    public float TransitionSpeed = 10f;
    public Outline.Mode Mode = Outline.Mode.OutlineVisible;

    private float _current = 0f;
    private bool _isHovered = false;
    private bool _isPressed = false;

    public UnityEvent OnClickPressed;
    public UnityEvent OnClickReleased;
    public UnityEvent OnMouseOver;
    public UnityEvent OnMouseLeft;

    private static bool _showLogs = false;

    private void Awake()
    {
        if (!TryGetComponent(out _outline))
            _outline = gameObject.AddComponent<Outline>();

        _outline.OutlineMode = Mode;

        _log = new(this, _showLogs);

        SetShaderProperty(0f);
    }

    private void Start()
    {
        if (ObjectSelector.Instance == null)
        {
            _log.Error(
                "No ObjectSelector found in the scene, add one to use a SelectableComponent."
            );

            return;
        }

        ObjectSelector.Instance.OnHovered.AddListener(OnHovered);
        ObjectSelector.Instance.OnPressed.AddListener(OnPressed);
        ObjectSelector.Instance.OnReleased.AddListener(OnReleased);
    }

    private void OnDestroy()
    {
        if (ObjectSelector.Instance == null)
            return;

        ObjectSelector.Instance.OnHovered.RemoveListener(OnHovered);
        ObjectSelector.Instance.OnPressed.RemoveListener(OnPressed);
        ObjectSelector.Instance.OnReleased.RemoveListener(OnReleased);
    }

    private void Update()
    {
        float target = _isHovered ? 1f : 0f;

        _current = Mathf.Lerp(
            _current,
            target,
            Time.deltaTime * TransitionSpeed
        );

        SetShaderProperty(_current);
    }

    private void OnHovered(GameObject target, bool hovered)
    {
        // Only react if this object is the object
        // currently being hovered.
        if (target != gameObject)
            return;

        _isHovered = hovered;

        if (_isHovered)
        {
            _log.Log("Started to hover clickable object!");
            OnMouseOver.Invoke();
        }
        else
        {
            _log.Log("Stopped hovering clickable object!");

            if (_isPressed)
            {
                _isPressed = false;
                _log.Log("Cancelled click after leaving hover");
            }

            OnMouseLeft.Invoke();
        }
    }

    private void OnPressed(GameObject target)
    {
        if (target != gameObject)
            return;

        _isPressed = true;

        _log.Log("Started to press clickable object!");

        OnClickPressed?.Invoke();
    }

    private void OnReleased(GameObject target)
    {
        if (target != gameObject || !_isPressed)
            return;

        _isPressed = false;

        _log.Log("Released press on clickable object!");

        OnClickReleased?.Invoke();
    }

    private void SetShaderProperty(float value)
    {
        Color color = Color.Lerp(_outline.OutlineColor, _isPressed
            ? PressedHintColor
            : _isHovered
            ? SelectionHintColor
            : NoInteractionHintColor,
            Time.deltaTime * TransitionSpeed).WithAlpha(
            _isPressed
            ? PressedHintColor.a
            : _isHovered
            ? SelectionHintColor.a
            : NoInteractionHintColor.a
            );

        color.a *= IsMaintainOutlineOnNoInteraction ? 1 : value;

        _outline.OutlineColor = color;
        _outline.OutlineWidth = IsMaintainOutlineOnNoInteraction ? Math.Max(OutlineWidth * value, InactiveOutlineWidth) : OutlineWidth * value;
    }
}