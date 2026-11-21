using Assets._Project.Features.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Assets._Project.Features.Pause
{
    // Esc (ou Start no controle) pausa o jogo e mostra o Panel. A UI fica no PauseMenu.prefab.
    [DisallowMultipleComponent]
    public sealed class PauseMenu : MonoBehaviour
    {
        public static bool IsPaused { get; private set; }

        public GameObject Panel;
        [Tooltip("Botão que começa selecionado, pra dar pra navegar no teclado/controle.")]
        public GameObject FirstSelected;
        public Slider VolumeSlider;
        public AudioClip ClickSound;

        private bool _selectorWasPaused;

        private void Awake()
        {
            Panel.SetActive(false);
            VolumeSlider.SetValueWithoutNotify(MasterVolume.Value);
        }

        // Ligado no onValueChanged do slider.
        public void SetVolume(float value) => MasterVolume.Value = value;

        private void Update()
        {
            bool pressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            pressed |= Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;

            if (pressed)
                SetPaused(!IsPaused);
        }

        private void OnDisable()
        {
            SetPaused(false);
        }

        public void Resume()
        {
            SetPaused(false);
            Click();
        }

        private void Click()
        {
            if (Camera.main)
                Sound.Play(ClickSound, Camera.main.transform.position);
        }

        public void Quit()
        {
            SetPaused(false);
            MainMenu.QuitGame();
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
                return;

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;

            // Sem isso o pulo e a câmera continuam respondendo com o jogo parado.
            InputActionMap playerActions = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player") : null;

            if (playerActions != null)
            {
                if (paused)
                    playerActions.Disable();
                else
                    playerActions.Enable();
            }

            // Uma cutscene pode ter pausado o seletor antes, então volta pro estado em que estava.
            ObjectSelector selector = ObjectSelector.Instance;

            if (selector != null)
            {
                if (paused)
                    _selectorWasPaused = selector.IsPaused;

                selector.IsPaused = paused || _selectorWasPaused;
            }

            if (Panel)
                Panel.SetActive(paused);

            if (paused && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(FirstSelected);
        }
    }
}
