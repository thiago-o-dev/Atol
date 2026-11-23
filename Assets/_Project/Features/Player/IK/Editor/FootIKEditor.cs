using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FootIK))]
public class FootIKEditor : Editor
{
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var ik = (FootIK)target;

        EditorGUILayout.Space();

        Animator animator = ik.animator;
        if (animator == null)
            foreach (Animator a in ik.GetComponentsInChildren<Animator>(true))
                if (a.isHuman) { animator = a; break; }
        if (animator == null) animator = ik.GetComponentInChildren<Animator>(true);

        if (animator == null || animator.avatar == null || !animator.isHuman)
        {
            EditorGUILayout.HelpBox("FootIK needs an Animator with a Humanoid avatar on this object or a child.", MessageType.Error);
            return;
        }

        EditorGUILayout.HelpBox(
            "Animator: " + animator.name + "\nNo 'IK Pass' is needed: the solver runs in LateUpdate and moves the hips and legs directly.",
            MessageType.None);

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Weight", ik.CurrentWeight.ToString("0.00"));
            EditorGUILayout.LabelField("Hip offset (m)", ik.PelvisOffset.ToString("0.000"));
            EditorGUILayout.LabelField("Grounded", ik.IsGrounded.ToString());
            EditorGUILayout.LabelField("Swimming", ik.IsSwimming.ToString());
        }
        else
        {
            EditorGUILayout.HelpBox("Select the character to see ground rays and sole/toe/heel points in the Scene view (works in Edit mode).", MessageType.Info);
        }
    }
}
