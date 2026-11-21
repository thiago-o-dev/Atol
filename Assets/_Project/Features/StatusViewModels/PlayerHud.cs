using Assets._Project.Features.Boat;
using Assets._Project.Features.Combat;
using Assets._Project.Features.Pause;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Assets._Project.Features.StatusViewModels
{
    // HUD do player (PlayerHUD.prefab). O PlayerController instancia e liga as barras.
    public sealed class PlayerHud : MonoBehaviour
    {
        public StatusBarUI Health;
        public StatusBarUI Stamina;
        [Tooltip("Água no casco do barco em que o player está.")]
        public StatusBarUI Flood;
        [Tooltip("Some com o HUD inteiro durante as cutscenes.")]
        public CanvasGroup Group;
        [Tooltip("Estrela ao lado das barras. Dá um tranco ao levar dano e pulsa com pouca vida.")]
        public RectTransform Emblem;
        public float DamagePunch = 0.35f;
        [Tooltip("Vinheta vermelha que pisca ao levar dano.")]
        public CanvasGroup DamageFlash;
        public float DamageShake = 0.25f;
        [Tooltip("Graus que a câmera fecha num tranco ao levar dano.")]
        public float DamageZoom = 5f;
        [Tooltip("Batimento que sobe com pouca vida.")]
        public AudioSource LowHealthLoop;
        [Range(0f, 1f)] public float LowHealthVolume = 0.6f;

        [Header("Death")]
        [Tooltip("Tela preta usada na morte. Ignora o Group pra não sumir junto com o HUD.")]
        public CanvasGroup Fade;
        [Tooltip("Estrela que sai do Emblem e vai pro meio da tela. No renascimento sobe pros galhos da Duceree.")]
        public RectTransform DeathStar;
        public TMP_Text DeathPhrase;
        public float StarFlightTime = 1.4f;
        public float StarScale = 3.2f;
        public float PhraseTime = 2.2f;

        [Header("Crosshair")]
        [Tooltip("Mira que segue o ponteiro no lugar do cursor do sistema.")]
        public Graphic Crosshair;
        public Color CrosshairColor = new Color(1f, 0.97f, 0.88f, 0.8f);
        [Tooltip("Cor com um alvo ao alcance do golpe.")]
        public Color CrosshairTargetColor = new Color(0.95f, 0.66f, 0.22f, 1f);

        private PlayerController _player;
        private PlayerStrike _strike;
        private BoatFlooding _flooding;
        private float _punch;

        private void Awake()
        {
            Group.alpha = 0f;
        }

        public void Bind(PlayerController player)
        {
            _player = player;
            _strike = player.GetComponent<PlayerStrike>();

            Health.Bind(player.Health);
            Stamina.Bind(player.Stamina);

            player.Health.Damaged += OnDamaged;
            player.Stamina.ConsumeFailed += OnStaminaRefused;
        }

        private void OnDestroy()
        {
            Cursor.visible = true;

            if (_player == null)
                return;

            _player.Health.Damaged -= OnDamaged;
            _player.Stamina.ConsumeFailed -= OnStaminaRefused;
        }

        private void Update()
        {
            if (_player == null)
                return;

            float step = 2f * Time.unscaledDeltaTime;

            Group.alpha = Mathf.MoveTowards(Group.alpha, CutsceneComponent.AnyPlaying ? 0f : 1f, step);
            DamageFlash.alpha = Mathf.MoveTowards(DamageFlash.alpha, 0f, step);

            BoatFlooding flooding = _player.CurrentBoat ? _player.CurrentBoat.GetComponent<BoatFlooding>() : null;

            if (flooding != _flooding)
            {
                _flooding = flooding;
                Flood.Bind(flooding);
            }

            float scale = 1f + _punch;

            if (!_player.Health.IsDead && _player.Health.Normalized < Health.LowThreshold)
                scale += 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));

            _punch = Mathf.MoveTowards(_punch, 0f, step);
            Emblem.localScale = Vector3.one * scale;

            bool lowHealth = !_player.Health.IsDead && _player.Health.Normalized < Health.LowThreshold;
            LowHealthLoop.volume = Mathf.MoveTowards(LowHealthLoop.volume, lowHealth ? LowHealthVolume : 0f, step);

            UpdateCrosshair();
        }

        // O cursor do sistema só aparece onde tem botão pra clicar: pausa e cutscene.
        private void UpdateCrosshair()
        {
            bool aiming = !PauseMenu.IsPaused && !CutsceneComponent.AnyPlaying && Mouse.current != null;

            Cursor.visible = !aiming;
            Crosshair.enabled = aiming;

            if (!aiming)
                return;

            bool hasTarget = _strike && _strike.Target;

            Crosshair.rectTransform.position = Mouse.current.position.ReadValue();
            Crosshair.rectTransform.localScale = Vector3.one * (hasTarget ? 1.25f : 1f);
            Crosshair.color = hasTarget ? CrosshairTargetColor : CrosshairColor;
        }

        // A estrela deixa o HUD, cresce no meio da tela enquanto tudo escurece e a frase aparece.
        public IEnumerator PlayDeath()
        {
            DeathStar.gameObject.SetActive(true);
            Emblem.gameObject.SetActive(false);

            yield return FlyStar(Emblem.position, ScreenPoint(0.58f), 1f, StarScale, 1f);
            yield return FadePhrase(1f);
            yield return new WaitForSecondsRealtime(PhraseTime);
        }

        // O caminho de volta: a frase some e a estrela sobe pra copa da árvore enquanto a tela clareia.
        public IEnumerator PlayRebirth()
        {
            yield return FadePhrase(0f);
            yield return FlyStar(ScreenPoint(0.58f), ScreenPoint(1.2f), StarScale, 1f, 0f);

            DeathStar.gameObject.SetActive(false);
            Emblem.gameObject.SetActive(true);
        }

        private static Vector3 ScreenPoint(float height) => new Vector3(Screen.width * 0.5f, Screen.height * height);

        private IEnumerator FlyStar(Vector3 from, Vector3 to, float fromScale, float toScale, float darkness)
        {
            float startDarkness = Fade.alpha;

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / StarFlightTime)
            {
                float progress = Mathf.SmoothStep(0f, 1f, t);

                DeathStar.position = Vector3.Lerp(from, to, progress);
                DeathStar.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, progress);
                Fade.alpha = Mathf.Lerp(startDarkness, darkness, progress);

                yield return null;
            }

            Fade.alpha = darkness;
        }

        private IEnumerator FadePhrase(float alpha)
        {
            while (!Mathf.Approximately(DeathPhrase.alpha, alpha))
            {
                DeathPhrase.alpha = Mathf.MoveTowards(DeathPhrase.alpha, alpha, 1.5f * Time.unscaledDeltaTime);
                yield return null;
            }
        }

        private void OnDamaged(float amount)
        {
            _punch = DamagePunch;
            DamageFlash.alpha = 0.85f;

            CameraFeel.Shake(DamageShake);
            CameraFeel.Punch(-DamageZoom);
        }

        private void OnStaminaRefused(float requested, float available)
        {
            Stamina.Flash();
        }
    }
}
