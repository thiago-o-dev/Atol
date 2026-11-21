using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.StatusViewModels
{
    public interface IStatusPool
    {
        float Normalized { get; }
    }

    // Barra de status. O visual fica no prefab, aqui só o preenchimento e os efeitos.
    public sealed class StatusBarUI : MonoBehaviour
    {
        public RectTransform Fill;
        public Graphic FillGraphic;
        public float Smoothing = 12f;

        [Header("Ghost")]
        [Tooltip("Opcional. Rastro que mostra quanto acabou de ser perdido.")]
        public RectTransform Ghost;
        public float GhostDelay = 0.45f;
        public float GhostSpeed = 0.5f;

        [Header("Low")]
        [Tooltip("Abaixo dessa fração a barra pulsa na LowColor.")]
        [Range(0f, 1f)] public float LowThreshold = 0.25f;
        public Color LowColor = new Color(0.9f, 0.25f, 0.2f, 1f);

        [Header("Auto Hide")]
        [Tooltip("Opcional. Com um CanvasGroup a barra some depois de um tempo cheia (ou vazia).")]
        public CanvasGroup Group;
        [Tooltip("Some quando vazia em vez de cheia, ex: água no barco.")]
        public bool HideWhenEmpty;
        public float HideDelay = 2f;

        private IStatusPool _pool;
        private Color _fillColor;
        private float _displayed;
        private float _ghost;
        private float _lastValue;
        private float _ghostHoldUntil;
        private float _lastShownTime;
        private float _flashUntil;

        private void Awake()
        {
            if (FillGraphic)
                _fillColor = FillGraphic.color;

            if (Group)
                Group.alpha = 0f;
        }

        public void Bind(IStatusPool pool)
        {
            _pool = pool;
            _displayed = _ghost = _lastValue = Value;

            Apply();
        }

        // Pisca na LowColor, ex: tentou gastar stamina que não tinha.
        public void Flash()
        {
            _flashUntil = Time.unscaledTime + 0.35f;
            _lastShownTime = Time.time;
        }

        private float Value => _pool as Object ? _pool.Normalized : 0f;

        private void Update()
        {
            float value = Value;

            if (value < _lastValue - 0.0001f)
                _ghostHoldUntil = Time.time + GhostDelay;

            _lastValue = value;
            _displayed = Mathf.Lerp(_displayed, value, Smoothing * Time.deltaTime);

            if (_ghost < value)
                _ghost = value;
            else if (Time.time >= _ghostHoldUntil)
                _ghost = Mathf.MoveTowards(_ghost, value, GhostSpeed * Time.deltaTime);

            Apply();

            if (FillGraphic)
            {
                Color color = _fillColor;

                if (value < LowThreshold)
                    color = Color.Lerp(color, LowColor, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));

                if (Time.unscaledTime < _flashUntil)
                    color = LowColor;

                FillGraphic.color = color;
            }

            if (Group)
            {
                bool resting = HideWhenEmpty ? value <= 0.001f : value >= 0.999f;

                if (!resting)
                    _lastShownTime = Time.time;

                float alpha = Time.time - _lastShownTime > HideDelay ? 0f : 1f;
                Group.alpha = Mathf.MoveTowards(Group.alpha, alpha, 3f * Time.deltaTime);
            }
        }

        private void Apply()
        {
            if (Fill)
                Fill.anchorMax = new Vector2(Mathf.Clamp01(_displayed), 1f);

            if (Ghost)
                Ghost.anchorMax = new Vector2(Mathf.Clamp01(_ghost), 1f);
        }
    }
}
