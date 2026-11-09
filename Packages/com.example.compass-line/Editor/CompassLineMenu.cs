#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CompassLine.Editor
{
    internal static class CompassLineMenu
    {
        [MenuItem("GameObject/UI/Compass Line", false, 2050)]
        private static void CreateCompassLine(MenuCommand command)
        {
            Canvas canvas = null;
            if (Selection.activeGameObject != null)
            {
                canvas = Selection.activeGameObject.GetComponentInParent<Canvas>();
            }

            if (canvas == null)
            {
                canvas = Object.FindAnyObjectByType<Canvas>();
            }

            if (canvas == null)
            {
                GameObject canvasObject = new GameObject(
                    "Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create Compass Canvas");
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            GameObject compassObject = new GameObject("Compass Line", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(compassObject, "Create Compass Line");
            compassObject.transform.SetParent(canvas.transform, false);

            RectTransform rect = compassObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -58f);
            rect.sizeDelta = new Vector2(256f, 80f);

            compassObject.AddComponent<global::CompassLine.CompassLineUI>();
            Selection.activeGameObject = compassObject;
            EditorGUIUtility.PingObject(compassObject);
            EditorSceneManager.MarkSceneDirty(compassObject.scene);
        }
    }
}
#endif
