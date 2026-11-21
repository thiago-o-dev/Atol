using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Assets._Project.Features.Pause
{
    // Menu inicial (MainMenu.prefab). A cena do jogo precisa estar no Build Settings.
    public sealed class MainMenu : MonoBehaviour
    {
        public string GameScene = "Arquipelago";
        public GameObject FirstSelected;

        private void Start()
        {
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(FirstSelected);
        }

        public void Play() => SceneManager.LoadScene(GameScene);

        public void Quit() => QuitGame();

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
