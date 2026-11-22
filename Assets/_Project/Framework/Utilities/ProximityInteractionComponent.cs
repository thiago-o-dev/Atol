using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Detects a player entering a trigger and either invokes an event immediately
/// or waits for the player to press and release E.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ProximityInteractionComponent : MonoBehaviour
{
    public enum InteractionMode
    {
        Instantaneous,
        AwaitInteraction
    }

    private enum RuntimeState
    {
        Idle,
        AwaitingPress,
        AwaitingRelease,
        Deactivating
    }

    [Header("Interaction")]
    [SerializeField]
    private InteractionMode interactionMode = InteractionMode.AwaitInteraction;

    [Tooltip("The Player tag is checked on the collider, its attached Rigidbody, and its root object.")]
    [SerializeField]
    private string playerTag = "Player";

    [Tooltip("Disable this component after its first successful interaction. The prompt fades out first.")]
    [SerializeField]
    private bool autoDeactivate;

    [Header("Prompt")]
    [Tooltip("Optional CanvasGroup to fade in while the player is in range. Its alpha is controlled by this component.")]
    [SerializeField]
    private CanvasGroup promptCanvasGroup;

    [Tooltip("Optional UI Graphic (Image, Text, TMP text, etc.) to tint while E is held.")]
    [SerializeField]
    private Graphic promptGraphic;

    [SerializeField]
    private Color normalPromptColor = Color.white;

    [SerializeField]
    private Color pressedPromptColor = Color.gray;

    [Min(0f)]
    [Tooltip("Duration of the prompt fade, in seconds.")]
    [SerializeField]
    private float fadeDuration = 0.2f;

    [Tooltip("Use unscaled time so the prompt can still fade while the game is paused.")]
    [SerializeField]
    private bool useUnscaledTime = true;

    [Header("Events")]
    public UnityEvent OnInteracted = new UnityEvent();

    private readonly HashSet<Collider> playerColliders = new HashSet<Collider>();

    private RuntimeState state = RuntimeState.Idle;
    private float targetPromptAlpha;

    /// <summary>Gets or sets whether this component disables itself after one interaction.</summary>
    public bool AutoDeactivate
    {
        get => autoDeactivate;
        set => autoDeactivate = value;
    }

    /// <summary>The currently selected interaction mode.</summary>
    public InteractionMode Mode
    {
        get => interactionMode;
        set => interactionMode = value;
    }

    private void Reset()
    {
        Collider ownCollider = GetComponent<Collider>();
        if (ownCollider != null)
        {
            ownCollider.isTrigger = true;
        }
    }

    private void OnValidate()
    {
        fadeDuration = Mathf.Max(0f, fadeDuration);

        Collider ownCollider = GetComponent<Collider>();
        if (ownCollider != null && !ownCollider.isTrigger)
        {
            Debug.LogWarning(
                $"{nameof(ProximityInteractionComponent)} on '{name}' needs its Collider set to Is Trigger.",
                this);
        }
    }

    private void Awake()
    {
        Collider ownCollider = GetComponent<Collider>();
        if (ownCollider != null && !ownCollider.isTrigger)
        {
            Debug.LogWarning(
                $"{nameof(ProximityInteractionComponent)} on '{name}' requires its Collider to have Is Trigger enabled.",
                this);
        }

        ConfigurePrompt();
    }

    private void OnEnable()
    {
        playerColliders.Clear();
        state = RuntimeState.Idle;
        targetPromptAlpha = 0f;
        ConfigurePrompt();
    }

    private void OnDisable()
    {
        playerColliders.Clear();
        state = RuntimeState.Idle;
        targetPromptAlpha = 0f;
        SetPromptPressed(false);

        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.interactable = false;
            promptCanvasGroup.blocksRaycasts = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerCollider(other))
        {
            return;
        }

        bool playerWasOutside = playerColliders.Count == 0;
        if (!playerColliders.Add(other) || !playerWasOutside)
        {
            return;
        }

        if (state == RuntimeState.Deactivating)
        {
            return;
        }

        if (interactionMode == InteractionMode.Instantaneous)
        {
            InvokeInteraction();
        }
        else
        {
            state = RuntimeState.AwaitingPress;
            SetPromptPressed(false);
            FadePromptTo(1f);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!playerColliders.Remove(other) || playerColliders.Count > 0)
        {
            return;
        }

        HandlePlayerLeft();
    }

    private void Update()
    {
        RemoveDestroyedPlayerColliders();
        UpdatePromptFade();

        if (state != RuntimeState.AwaitingPress && state != RuntimeState.AwaitingRelease)
        {
            return;
        }

        if (playerColliders.Count == 0)
        {
            HandlePlayerLeft();
            return;
        }

        if (state == RuntimeState.AwaitingPress && WasEPressedThisFrame())
        {
            state = RuntimeState.AwaitingRelease;
            SetPromptPressed(true);
        }
        else if (state == RuntimeState.AwaitingRelease && WasEReleasedThisFrame())
        {
            SetPromptPressed(false);
            InvokeInteraction();
        }
    }

    private void InvokeInteraction()
    {
        OnInteracted?.Invoke();

        // An event listener may disable this component itself.
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (autoDeactivate)
        {
            BeginDeactivation();
        }
        else if (interactionMode == InteractionMode.AwaitInteraction)
        {
            // Keep the prompt visible and allow another press/release while the player remains in range.
            state = playerColliders.Count > 0 ? RuntimeState.AwaitingPress : RuntimeState.Idle;
        }
    }

    private void HandlePlayerLeft()
    {
        if (state == RuntimeState.Deactivating)
        {
            return;
        }

        state = RuntimeState.Idle;
        SetPromptPressed(false);
        FadePromptTo(0f);
    }

    private void BeginDeactivation()
    {
        state = RuntimeState.Deactivating;
        SetPromptPressed(false);
        FadePromptTo(0f);

        if (promptCanvasGroup == null || fadeDuration <= 0f || promptCanvasGroup.alpha <= 0.001f)
        {
            FinishDeactivation();
        }
    }

    private void FinishDeactivation()
    {
        enabled = false;
    }

    private void ConfigurePrompt()
    {
        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = 0f;
            promptCanvasGroup.interactable = false;
            promptCanvasGroup.blocksRaycasts = false;
        }

        SetPromptPressed(false);
    }

    private void FadePromptTo(float alpha)
    {
        targetPromptAlpha = Mathf.Clamp01(alpha);

        if (promptCanvasGroup != null && fadeDuration <= 0f)
        {
            promptCanvasGroup.alpha = targetPromptAlpha;
        }
    }

    private void UpdatePromptFade()
    {
        if (promptCanvasGroup == null)
        {
            if (state == RuntimeState.Deactivating)
            {
                FinishDeactivation();
            }

            return;
        }

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float step = fadeDuration <= 0f ? 1f : deltaTime / fadeDuration;
        promptCanvasGroup.alpha = Mathf.MoveTowards(
            promptCanvasGroup.alpha,
            targetPromptAlpha,
            step);

        if (state == RuntimeState.Deactivating && promptCanvasGroup.alpha <= 0.001f)
        {
            FinishDeactivation();
        }
    }

    private void SetPromptPressed(bool pressed)
    {
        if (promptGraphic != null)
        {
            promptGraphic.color = pressed ? pressedPromptColor : normalPromptColor;
        }
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other == null || string.IsNullOrEmpty(playerTag))
        {
            return false;
        }

        if (other.gameObject.tag == playerTag)
        {
            return true;
        }

        Rigidbody attachedBody = other.attachedRigidbody;
        if (attachedBody != null && attachedBody.gameObject.tag == playerTag)
        {
            return true;
        }

        Transform root = other.transform.root;
        return root != null && root.gameObject.tag == playerTag;
    }

    private void RemoveDestroyedPlayerColliders()
    {
        int removedCount = playerColliders.RemoveWhere(playerCollider => playerCollider == null);
        if (removedCount > 0 && playerColliders.Count == 0)
        {
            HandlePlayerLeft();
        }
    }

    private static bool WasEPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }

    private static bool WasEReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasReleasedThisFrame;
#else
        return Input.GetKeyUp(KeyCode.E);
#endif
    }
}
