using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Editor // AddPublishFeaturePipeline
{
    public static class FrameworkTools
    {
        private const string commonDestinationRoot = "Assets/_Project/";
        private const string gamesDestinationRoot = commonDestinationRoot + "Games/";
        private const string SceneDestinationRoot = commonDestinationRoot + "Scenes";
        private const string MenuRoot = "Framework Tools/";
        private const string AppManagerPrefabPath = "Assets/_Project/Solver/Platform/AppManager.prefab";

        // ── Itens de menu ────────────────────────────────────────

        [MenuItem(MenuRoot + "Create/New Game", priority = 100)]
        public static void CreateGameFolder()
        {
            // ----- 1. Solicita o nome do jogo -----
            string gameName = GameNameDialog.Show();

            if (string.IsNullOrEmpty(gameName))
            {
                Debug.Log("[FrameworkTools] Criação de jogo cancelada pelo usuário.");
                return;
            }

            gameName = SanitizeFileName(gameName);

            if (string.IsNullOrEmpty(gameName))
            {
                EditorUtility.DisplayDialog("Nome inválido",
                    "O nome informado contém apenas caracteres inválidos.\nTente novamente.", "OK");
                return;
            }

            // ----- 2. Verifica se o jogo já existe -----
            string gameRoot = $"{gamesDestinationRoot}{gameName}";

            if (AssetDatabase.IsValidFolder(gameRoot))
            {
                EditorUtility.DisplayDialog(
                    "Jogo já existe",
                    $"Já existe um projeto com o nome \"{gameName}\" em:\n{gameRoot}\n\n" +
                    "Escolha um nome diferente para o novo jogo.",
                    "OK");
                return;
            }

            // ----- 3. Cria a pasta raiz do jogo -----
            EnsureDirectory(gameRoot);

            // ----- 4. Cria subpastas -----
            string[] subFolders = { "Prefabs", "Scripts", "Models", "UI" };
            foreach (string folder in subFolders)
            {
                EnsureDirectory($"{gameRoot}/{folder}");
            }

            // ----- 5. Cria as cenas -----
            string scenesPath = $"{gameRoot}";
            EnsureDirectory(scenesPath);

            string mainScenePath = $"{scenesPath}/{gameName}.unity";
            string testScenePath = $"{scenesPath}/{gameName}_Test.unity";

            // Carrega o prefab do AppManager (avisa se não encontrado, mas continua)
            GameObject appManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AppManagerPrefabPath);
            if (appManagerPrefab == null)
            {
                Debug.LogWarning($"[FrameworkTools] Prefab não encontrado em: {AppManagerPrefabPath}\n" +
                                 "As cenas serão criadas sem o AppManager.");
            }

            CreateSceneWithPrefab(mainScenePath, appManagerPrefab);
            CreateSceneWithPrefab(testScenePath, appManagerPrefab);

            AssetDatabase.Refresh();

            Debug.Log($"[FrameworkTools] Jogo \"{gameName}\" criado em: {gameRoot}");

            string prefabMsg = appManagerPrefab != null
                ? "• AppManager instanciado em (0, 0, 0) nas duas cenas"
                : "• Atenção: prefab AppManager não encontrado — cenas criadas sem ele";

            EditorUtility.DisplayDialog(
                "Jogo Criado — Concluído",
                $"Projeto \"{gameName}\" criado com sucesso!\n\n" +
                $"Localização:\n{gameRoot}\n\n" +
                $"Pastas criadas:\n" +
                $" Prefabs\n" +
                $" Scripts\n" +
                $" Models\n" +
                $" UI\n\n" +
                $"Cenas criadas:\n " +
                $"{gameName}.unity\n" +
                $" {gameName}_Test.unity\n\n" +
                prefabMsg,
                "OK");
        }

        [MenuItem(MenuRoot + "Publish/Publish Scene", priority = 110)]
        public static void PublishScene()
        {
            // ----- 1. Cena ativa -----
            Scene activeScene = SceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(activeScene.path))
            {
                bool create = EditorUtility.DisplayDialog(
                    "Cena sem arquivo",
                    "A cena atual nunca foi salva em disco.\n" +
                    "Deseja salvá-la agora antes de publicar?",
                    "Salvar e continuar", "Cancelar");

                if (!create) return;

                string tempPath = EditorUtility.SaveFilePanelInProject(
                    "Salvar cena atual", "NovaCena", "unity",
                    "Escolha onde salvar a cena atual");

                if (string.IsNullOrEmpty(tempPath)) return;

                EditorSceneManager.SaveScene(activeScene, tempPath);
                activeScene = SceneManager.GetActiveScene();
            }

            // ----- 2. Salva automaticamente se houver mudanças -----
            if (activeScene.isDirty)
            {
                Debug.Log("[FrameworkTools] Cena modificada — salvando automaticamente...");
                bool saved = EditorSceneManager.SaveScene(activeScene);

                if (!saved)
                {
                    EditorUtility.DisplayDialog("Erro ao salvar",
                        "Não foi possível salvar a cena atual.\n" +
                        "Salve manualmente e tente novamente.", "OK");
                    return;
                }

                Debug.Log("[FrameworkTools] Cena salva com sucesso.");
            }

            // ----- 3. Solicita o nome da cena publicada -----
            string sourcePath = activeScene.path;
            string defaultName = Path.GetFileNameWithoutExtension(sourcePath);
            string newName = SceneNameDialog.Show(defaultName);

            if (string.IsNullOrEmpty(newName))
            {
                Debug.Log("[FrameworkTools] Publicação cancelada pelo usuário.");
                return;
            }

            newName = SanitizeFileName(newName);

            // ----- 4. Garante pasta destino -----
            EnsureDirectory(SceneDestinationRoot);
            string destPath = $"{SceneDestinationRoot}/{newName}.unity";

            // ----- 5. Confirma sobrescrita se necessário -----
            if (File.Exists(destPath))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Cena já existe",
                    $"Já existe uma cena em:\n{destPath}\n\nDeseja substituí-la?",
                    "Substituir", "Cancelar");

                if (!overwrite) return;
            }

            // ----- 6. Clona o arquivo .unity -----
            bool copyOk = AssetDatabase.CopyAsset(sourcePath, destPath);

            if (!copyOk)
            {
                EditorUtility.DisplayDialog("Erro na cópia",
                    $"Não foi possível copiar a cena para:\n{destPath}\n\n" +
                    "Verifique permissões e tente novamente.", "OK");
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[FrameworkTools] Cena clonada para: {destPath}");

            // ----- 7. Adiciona ao Build Settings -----
            bool addedToBuild = AddSceneToBuildSettings(destPath);
            string buildMsg = addedToBuild
                ? "A cena foi adicionada ao Build Settings."
                : "A cena já estava no Build Settings.";

            EditorUtility.DisplayDialog(
                "Publish Scene — Concluído",
                $"Cena publicada com sucesso!\n\nDestino:\n{destPath}\n\n{buildMsg}",
                "OK");
        }

        // ── Separador visual antes da versão ─────────────────────

        [MenuItem(MenuRoot + "Help/Version", priority = 200)]
        public static void ShowVersion()
        {
            EditorUtility.DisplayDialog(
                "Framework Tools",
                "Framework Tools v0.0.1\nUnity 6 compatible\nby: DragãoHeremita",
                "OK");
        }

        [MenuItem(MenuRoot + "Help/Documents", priority = 200)]
        public static void ShowHelp()
        {
            bool open = EditorUtility.DisplayDialog(
              "Framework Help",
              "Abrir documentação?",
              "OK",
              "Cancelar");

            if (open)
                Application.OpenURL("https://git.solversys.com.br/d4i/trainment-serious-game");
        }

        // ── Helpers ──────────────────────────────────────────────

        private static void CreateSceneWithPrefab(string scenePath, GameObject prefab)
        {
            // Cria a cena em modo Additive para não fechar a cena atual do editor
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);

            if (prefab != null)
            {
                // Instancia o prefab mantendo o link com o asset original (PrefabUtility)
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance != null)
                {
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Debug.Log($"[FrameworkTools] Prefab \"{prefab.name}\" adicionado em {scenePath}");
                }
            }

            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[FrameworkTools] Cena criada: {scenePath}");
        }

        // Mantido para uso futuro (PublishScene não precisa de prefab)
        private static void CreateEmptyScene(string scenePath)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[FrameworkTools] Cena criada: {scenePath}");
        }

        private static bool AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            bool exists = scenes.Any(s =>
                s.path.Equals(scenePath, StringComparison.OrdinalIgnoreCase));

            if (exists) return false;

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[FrameworkTools] Adicionado ao Build Settings: {scenePath}");
            return true;
        }

        private static void EnsureDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                    Debug.Log($"[FrameworkTools] Pasta criada: {next}");
                }
                current = next;
            }
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        }
    }

    // ---------------------------------------------------------
    //  Janela modal para entrada do nome do novo jogo
    // ---------------------------------------------------------
    public class GameNameDialog : EditorWindow
    {
        private string _gameName = "";
        private bool _focusSet = false;

        private static string s_result = null;

        public static new string Show()
        {
            var window = CreateInstance<GameNameDialog>();
            window._gameName = "";
            window.titleContent = new GUIContent("Criar Novo Jogo — Nome");
            window.minSize = new Vector2(380, 110);
            window.maxSize = new Vector2(480, 110);

            s_result = null;
            window.ShowModal();
            return s_result ?? "";
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Nome do novo jogo:", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            GUI.SetNextControlName("GameNameField");
            _gameName = EditorGUILayout.TextField(_gameName);

            if (!_focusSet)
            {
                EditorGUI.FocusTextInControl("GameNameField");
                _focusSet = true;
            }

            // Enter confirma
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return)
            { Confirm(); return; }

            // Escape cancela
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            { Cancel(); return; }

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancelar", GUILayout.Width(90))) Cancel();
            if (GUILayout.Button("Criar", GUILayout.Width(90))) Confirm();
            EditorGUILayout.EndHorizontal();
        }

        private void Confirm() { s_result = _gameName; Close(); }
        private void Cancel() { s_result = ""; Close(); }
    }

    // ---------------------------------------------------------
    //  Janela modal para entrada do nome da cena publicada
    // ---------------------------------------------------------
    public class SceneNameDialog : EditorWindow
    {
        private string _sceneName = "";
        private bool _focusSet = false;

        private static string s_result = null;

        public static string Show(string defaultName = "")
        {
            var window = CreateInstance<SceneNameDialog>();
            window._sceneName = defaultName;
            window.titleContent = new GUIContent("Publish Scene — Nome");
            window.minSize = new Vector2(380, 110);
            window.maxSize = new Vector2(480, 110);

            s_result = null;
            window.ShowModal();
            return s_result ?? "";
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Nome da cena publicada:", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            GUI.SetNextControlName("SceneNameField");
            _sceneName = EditorGUILayout.TextField(_sceneName);

            if (!_focusSet)
            {
                EditorGUI.FocusTextInControl("SceneNameField");
                _focusSet = true;
            }

            // Enter confirma
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return)
            { Confirm(); return; }

            // Escape cancela
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            { Cancel(); return; }

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancelar", GUILayout.Width(90))) Cancel();
            if (GUILayout.Button("Publicar", GUILayout.Width(90))) Confirm();
            EditorGUILayout.EndHorizontal();
        }

        private void Confirm() { s_result = _sceneName; Close(); }
        private void Cancel() { s_result = ""; Close(); }
    }

}
