using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Collider))]
public sealed class ProximityInteractionComponent : MonoBehaviour
{
    public enum InteractionMode { Instantaneous, AwaitInteraction }

    [Header("Interaction")]
    [SerializeField] private InteractionMode mode = InteractionMode.AwaitInteraction;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool autoDeactivate;
    public UnityEvent OnInteracted = new UnityEvent();

    [Header("Shared Prompt")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Graphic[] graphics;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color pressedColor = Color.gray;
    [Min(0f)] [SerializeField] private float fadeDuration = 0.2f;

    private readonly HashSet<Collider> playerColliders = new HashSet<Collider>();
    private bool waiting;
    private bool pressed;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogWarning($"{name}: Proximity Collider must have Is Trigger enabled.", this);
    }

    private void OnEnable()
    {
        // A disabled MonoBehaviour can still receive Unity physics messages.
        // Start from a clean state and discard any stale manager registration.
        playerColliders.Clear();
        waiting = pressed = false;
        ProximityInteractionManager.RemoveOwner(this);
    }

    private void OnDisable()
    {
        playerColliders.Clear();
        waiting = pressed = false;
        ProximityInteractionManager.RemoveOwner(this);
    }

    private void OnTriggerEnter(Collider other) => TryEnter(other);

    // Also catches a player that was already overlapping when this component enabled.
    private void OnTriggerStay(Collider other)
    {
        if (!isActiveAndEnabled) return;
        if (!playerColliders.Contains(other)) TryEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!isActiveAndEnabled) return;
        playerColliders.Remove(other);
        if (playerColliders.Count == 0) Leave();
    }

    private void TryEnter(Collider other)
    {
        // Unity may send OnTriggerEnter/Stay to disabled MonoBehaviours.
        // Never let an inactive interaction enter the overlap/input system or
        // invoke an Instantaneous interaction from a physics callback.
        if (!isActiveAndEnabled || !other) return;

        bool isPlayer = HasPlayerTag(other.transform) ||
            (other.attachedRigidbody && HasPlayerTag(other.attachedRigidbody.transform));
        if (!isPlayer || !playerColliders.Add(other)) return;
        if (playerColliders.Count != 1) return;

        if (mode == InteractionMode.Instantaneous)
        {
            Interact();
        }
        else
        {
            waiting = true;
            ProximityInteractionManager.Instance.Show(this);
        }
    }

    private bool HasPlayerTag(Transform current)
    {
        while (current != null)
        {
            if (current.gameObject.tag == playerTag) return true;
            current = current.parent;
        }
        return false;
    }

    private void Leave()
    {
        waiting = pressed = false;
        ProximityInteractionManager.RemoveOwner(this);
    }

    internal bool CanInteract => isActiveAndEnabled && waiting && playerColliders.Count > 0;
    internal CanvasGroup PromptGroup => canvasGroup;
    internal float FadeDuration => fadeDuration;
    internal bool IsPressed => pressed;
    internal Graphic[] PromptGraphics => graphics;
    internal Color NormalColor => normalColor;
    internal Color PressedColor => pressedColor;

    internal bool UsesGraphic(Graphic graphic)
    {
        if (graphics == null) return false;
        foreach (Graphic item in graphics)
            if (item == graphic) return true;
        return false;
    }

    internal void Press()
    {
        if (!CanInteract) return;
        pressed = true;
        ProximityInteractionManager.Instance.Refresh(this);
    }

    internal void Release()
    {
        if (!pressed) return;
        pressed = false;
        ProximityInteractionManager.Instance.Refresh(this);
        if (CanInteract) Interact();
    }

    private void Interact()
    {
        bool deactivateAfterUse = autoDeactivate;

        // Release the prompt before invoking user events. This prevents this
        // component from being considered an active prompt owner during callbacks.
        if (deactivateAfterUse)
        {
            waiting = false;
            pressed = false;
            ProximityInteractionManager.RemoveOwner(this);
        }

        try
        {
            OnInteracted?.Invoke();
        }
        finally
        {
            if (deactivateAfterUse && this && isActiveAndEnabled)
                enabled = false;
        }
    }

    internal void CancelPress() => pressed = false;
}

// One input listener and one fade per shared CanvasGroup.
internal sealed class ProximityInteractionManager : MonoBehaviour
{
    private sealed class Prompt
    {
        public readonly HashSet<ProximityInteractionComponent> owners = new HashSet<ProximityInteractionComponent>();
        public readonly Dictionary<Graphic, Color> originalColors = new Dictionary<Graphic, Color>();
        public Coroutine fade;
    }

    private static ProximityInteractionManager instance;
    private readonly List<ProximityInteractionComponent> waiting = new List<ProximityInteractionComponent>();
    private readonly Dictionary<CanvasGroup, Prompt> prompts = new Dictionary<CanvasGroup, Prompt>();
    private readonly Dictionary<ProximityInteractionComponent, CanvasGroup> ownerGroups =
        new Dictionary<ProximityInteractionComponent, CanvasGroup>();
    private ProximityInteractionComponent pressedTarget;

    public static ProximityInteractionManager Instance
    {
        get
        {
            if (instance) return instance;
            var go = new GameObject("ProximityInteractionManager");
            instance = go.AddComponent<ProximityInteractionManager>();
            DontDestroyOnLoad(go);
            return instance;
        }
    }

    public static void RemoveOwner(ProximityInteractionComponent item)
    {
        if (instance) instance.Hide(item);
    }

    public void Show(ProximityInteractionComponent item)
    {
        if (!item || !item.CanInteract) return;

        CanvasGroup group = item.PromptGroup;
        if (!group) return;

        // If the reference was changed at runtime, release the old group first.
        if (ownerGroups.TryGetValue(item, out CanvasGroup previousGroup) && previousGroup != group)
            Hide(item);

        if (!waiting.Contains(item)) waiting.Add(item);
        Prompt prompt = GetPrompt(group);
        PruneOwners(group, prompt);
        prompt.owners.Add(item);
        ownerGroups[item] = group;
        AddGraphics(prompt, item);
        RefreshPrompt(group, prompt);

        // Only a currently-interactable owner may request the visible state.
        Fade(group, prompt, 1f, item.FadeDuration);
    }

    public void Refresh(ProximityInteractionComponent item)
    {
        if (item.PromptGroup && prompts.TryGetValue(item.PromptGroup, out Prompt prompt))
            RefreshPrompt(item.PromptGroup, prompt);
    }

    public void Hide(ProximityInteractionComponent item)
    {
        if (!item) return;
        waiting.Remove(item);
        if (pressedTarget == item)
        {
            item.CancelPress();
            pressedTarget = null;
        }

        // Use the group that was registered by Show, not the component's
        // current serialized reference (which may have changed since entry).
        if (!ownerGroups.TryGetValue(item, out CanvasGroup group)) return;
        ownerGroups.Remove(item);
        if (!group || !prompts.TryGetValue(group, out Prompt prompt)) return;

        prompt.owners.Remove(item);
        PruneOwners(group, prompt);
        RefreshPrompt(group, prompt);
        if (prompt.owners.Count > 0) return;

        Fade(group, prompt, 0f, item.FadeDuration, () =>
        {
            PruneOwners(group, prompt);
            // A new owner calls Show(), which cancels this fade and fades in.
            // Never force alpha back to 1 from a fade-out completion callback.
            if (prompt.owners.Count == 0)
            {
                RestoreGraphics(prompt);
                prompts.Remove(group);
            }
        });
    }

    private void PruneOwners(CanvasGroup group, Prompt prompt)
    {
        List<ProximityInteractionComponent> stale = null;
        foreach (ProximityInteractionComponent owner in prompt.owners)
        {
            if (owner && owner.CanInteract && owner.PromptGroup == group) continue;
            stale ??= new List<ProximityInteractionComponent>();
            stale.Add(owner);
        }

        if (stale == null) return;
        foreach (ProximityInteractionComponent owner in stale)
        {
            prompt.owners.Remove(owner);
            if (owner)
            {
                waiting.Remove(owner);
                ownerGroups.Remove(owner);
            }
        }
    }

#if ENABLE_INPUT_SYSTEM
    private InputAction interactAction;

    private void Awake()
    {
        interactAction = new InputAction("Proximity Interact", InputActionType.Button, "<Keyboard>/e");
        interactAction.started += _ => PressTopWaiting();
        interactAction.canceled += _ => ReleasePressed();
        interactAction.Enable();
    }

    private void OnDestroy()
    {
        interactAction?.Disable();
        interactAction?.Dispose();
        if (instance == this) instance = null;
    }
#elif ENABLE_LEGACY_INPUT_MANAGER
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E)) PressTopWaiting();
        if (Input.GetKeyUp(KeyCode.E)) ReleasePressed();
    }
#endif

    private void PressTopWaiting()
    {
        if (pressedTarget) return;

        for (int i = waiting.Count - 1; i >= 0; i--)
        {
            ProximityInteractionComponent candidate = waiting[i];
            if (!candidate || !candidate.CanInteract)
            {
                // Do not leave disabled/destroyed components in the global
                // input queue. Hide also removes any remaining prompt ownership.
                waiting.RemoveAt(i);
                if (candidate) Hide(candidate);
                continue;
            }

            pressedTarget = candidate;
            pressedTarget.Press();
            return;
        }
    }

    private void ReleasePressed()
    {
        ProximityInteractionComponent target = pressedTarget;
        pressedTarget = null;
        if (target) target.Release();
    }

    private Prompt GetPrompt(CanvasGroup group)
    {
        if (!prompts.TryGetValue(group, out Prompt prompt))
            prompts.Add(group, prompt = new Prompt());
        return prompt;
    }

    private static void AddGraphics(Prompt prompt, ProximityInteractionComponent item)
    {
        if (item.PromptGraphics == null) return;
        foreach (Graphic graphic in item.PromptGraphics)
            if (graphic && !prompt.originalColors.ContainsKey(graphic))
                prompt.originalColors.Add(graphic, graphic.color);
    }

    private static void RefreshPrompt(CanvasGroup group, Prompt prompt)
    {
        foreach (var pair in prompt.originalColors)
        {
            if (!pair.Key) continue;
            ProximityInteractionComponent normalOwner = null;
            ProximityInteractionComponent pressedOwner = null;
            foreach (ProximityInteractionComponent owner in prompt.owners)
            {
                if (!owner || !owner.UsesGraphic(pair.Key)) continue;
                normalOwner ??= owner;
                if (owner.IsPressed) { pressedOwner = owner; break; }
            }

            pair.Key.color = pressedOwner ? pressedOwner.PressedColor :
                normalOwner ? normalOwner.NormalColor : pair.Value;
        }
    }

    private void Fade(CanvasGroup group, Prompt prompt, float target, float duration, System.Action done = null)
    {
        if (prompt.fade != null) StopCoroutine(prompt.fade);
        if (duration <= 0f)
        {
            group.alpha = target;
            prompt.fade = null;
            done?.Invoke();
            return;
        }
        prompt.fade = StartCoroutine(FadeRoutine(group, prompt, target, duration, done));
    }

    private IEnumerator FadeRoutine(CanvasGroup group, Prompt prompt, float target, float duration, System.Action done)
    {
        if (!group) { prompt.fade = null; done?.Invoke(); yield break; }
        float start = group.alpha;
        float elapsed = 0f;
        while (group && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        if (group) group.alpha = target;
        prompt.fade = null;
        done?.Invoke();
    }

    private static void RestoreGraphics(Prompt prompt)
    {
        foreach (var pair in prompt.originalColors)
            if (pair.Key) pair.Key.color = pair.Value;
    }
}
