using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.Boat
{
    // Rosa dos ventos do barco (WindIndicator.prefab). O casco no centro aponta pra proa e a seta mostra
    // pra onde o vento sopra em relação a ela. A seta apaga quando a proa está contra o vento.
    [DisallowMultipleComponent]
    public sealed class WindIndicatorUI : MonoBehaviour
    {
        public SpectralOceanFFT Ocean;
        public PlayerController Player;
        public CanvasGroup Group;
        public RectTransform Arrow;
        public Graphic ArrowGraphic;
        [Tooltip("Acendem conforme a força do vento.")]
        public Graphic[] StrengthPips;

        public Color GoodColor = new Color(0.95f, 0.66f, 0.22f, 1f);
        [Tooltip("Cor da seta com a proa contra o vento, quando a vela não puxa.")]
        public Color DeadColor = new Color(0.75f, 0.8f, 0.82f, 0.45f);
        [Tooltip("Meio ângulo da zona morta, igual ao No Go Angle do BoatController.")]
        public float NoGoAngle = 35f;
        public float FadeSpeed = 4f;

        private bool _visible;
        private float _angle;

        private void Awake()
        {
            if (Ocean == null)
                Ocean = FindAnyObjectByType<SpectralOceanFFT>();

            if (Group)
                Group.alpha = 0f;
        }

        public void SetVisible(bool value)
        {
            _visible = value;
        }

        private void LateUpdate()
        {
            if (Group)
                Group.alpha = Mathf.MoveTowards(Group.alpha, _visible ? 1f : 0f, FadeSpeed * Time.unscaledDeltaTime);

            if (!_visible || Ocean == null)
                return;

            Vector2 wind = Ocean.CurrentWindDirectionXZ;

            if (wind.sqrMagnitude < 0.0001f)
                return;

            // Em cima = proa. Fora do barco, usa a câmera.
            float heading = 0f;

            if (Player && Player.CurrentBoat)
                heading = Player.CurrentBoat.transform.eulerAngles.y;
            else if (Camera.main)
                heading = Camera.main.transform.eulerAngles.y;

            float relative = Mathf.DeltaAngle(heading, Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg);

            _angle = Mathf.LerpAngle(_angle, relative, 8f * Time.unscaledDeltaTime);
            Arrow.localRotation = Quaternion.Euler(0f, 0f, -_angle);

            // Vento soprando pra trás (perto de 180) = proa contra o vento.
            bool intoWind = Mathf.Abs(relative) > 180f - NoGoAngle;

            if (ArrowGraphic)
                ArrowGraphic.color = Color.Lerp(ArrowGraphic.color, intoWind ? DeadColor : GoodColor, 6f * Time.unscaledDeltaTime);

            float strength = Ocean.CurrentWindStrength;

            for (int i = 0; i < StrengthPips.Length; i++)
            {
                if (StrengthPips[i])
                    StrengthPips[i].canvasRenderer.SetAlpha(strength >= 0.3f + i * 0.25f ? 1f : 0.25f);
            }
        }
    }
}
