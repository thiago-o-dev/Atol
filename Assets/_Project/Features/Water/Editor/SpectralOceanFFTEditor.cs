#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpectralOceanFFT))]
[CanEditMultipleObjects]
public sealed class SpectralOceanFFTEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Coastal controls use world metres. Start with a shore preset, then fine-tune " +
            "the grouped settings below. The mesh remains visible when shoreline motion reaches zero.",
            MessageType.Info);

        EditorGUILayout.LabelField("Quick Shore Presets", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Sandy Beach")) ApplyPreset(ShorePreset.SandyBeach);
            if (GUILayout.Button("Rocky Coast")) ApplyPreset(ShorePreset.RockyCoast);
            if (GUILayout.Button("Calm Shore")) ApplyPreset(ShorePreset.CalmShore);
        }

        EditorGUILayout.Space(5f);
        DrawDefaultInspector();
    }

    private enum ShorePreset { SandyBeach, RockyCoast, CalmShore }

    private void ApplyPreset(ShorePreset preset)
    {
        foreach (Object item in targets)
        {
            SerializedObject so = new SerializedObject(item);
            so.Update();
            Undo.RecordObject(item, "Apply Ocean Shore Preset");

            switch (preset)
            {
                case ShorePreset.SandyBeach:
                    Set(so, "shoreFadeStartMeters", 0f);
                    Set(so, "shoreFadeEndMeters", 120f);
                    Set(so, "nearShoreWaveStrength", 0.04f);
                    Set(so, "shoreEdgeFadeMeters", 14f);
                    Set(so, "shoreBreakStartMeters", 3f);
                    Set(so, "shoreBreakPeakMeters", 24f);
                    Set(so, "shoreBreakEndMeters", 105f);
                    Set(so, "shoreDepthPerMeter", 0.11f);
                    Set(so, "shoreShoalingScale", 1.18f);
                    Set(so, "shoreChoppiness", 1.45f);
                    Set(so, "shoreHorizontalDrive", 0.48f);
                    Set(so, "shoreFoamWidthMeters", 60f);
                    Set(so, "shoreFoamEdgeFeatherMeters", 85f);
                    Set(so, "shoreFoamBlackThreshold", 0.45f);
                    Set(so, "shoreFoamBlackSoftness", 0.18f);
                    Set(so, "shoreFoamEdgeNoiseMeters", 14f);
                    Set(so, "shoreFoamGain", 3.5f);
                    break;

                case ShorePreset.RockyCoast:
                    Set(so, "shoreFadeStartMeters", 0f);
                    Set(so, "shoreFadeEndMeters", 65f);
                    Set(so, "nearShoreWaveStrength", 0.08f);
                    Set(so, "shoreEdgeFadeMeters", 7f);
                    Set(so, "shoreBreakStartMeters", 2f);
                    Set(so, "shoreBreakPeakMeters", 12f);
                    Set(so, "shoreBreakEndMeters", 52f);
                    Set(so, "shoreDepthPerMeter", 0.18f);
                    Set(so, "shoreShoalingScale", 1.24f);
                    Set(so, "shoreChoppiness", 1.85f);
                    Set(so, "shoreHorizontalDrive", 0.62f);
                    Set(so, "shoreFoamWidthMeters", 28f);
                    Set(so, "shoreFoamEdgeFeatherMeters", 52f);
                    Set(so, "shoreFoamBlackThreshold", 0.34f);
                    Set(so, "shoreFoamBlackSoftness", 0.14f);
                    Set(so, "shoreFoamEdgeNoiseMeters", 9f);
                    Set(so, "shoreFoamGain", 3.1f);
                    break;

                case ShorePreset.CalmShore:
                    Set(so, "shoreFadeStartMeters", 0f);
                    Set(so, "shoreFadeEndMeters", 145f);
                    Set(so, "nearShoreWaveStrength", 0.02f);
                    Set(so, "shoreEdgeFadeMeters", 18f);
                    Set(so, "shoreBreakStartMeters", 4f);
                    Set(so, "shoreBreakPeakMeters", 38f);
                    Set(so, "shoreBreakEndMeters", 140f);
                    Set(so, "shoreDepthPerMeter", 0.10f);
                    Set(so, "shoreShoalingScale", 1.10f);
                    Set(so, "shoreChoppiness", 1.20f);
                    Set(so, "shoreHorizontalDrive", 0.25f);
                    Set(so, "shoreFoamWidthMeters", 65f);
                    Set(so, "shoreFoamEdgeFeatherMeters", 110f);
                    Set(so, "shoreFoamBlackThreshold", 0.4f);
                    Set(so, "shoreFoamBlackSoftness", 0.22f);
                    Set(so, "shoreFoamEdgeNoiseMeters", 18f);
                    Set(so, "shoreFoamGain", 2.2f);
                    break;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(item);
        }
    }

    private static void Set(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }
}
#endif
