#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Assets._Project.Features.WalkingDragon.Editor
{
    /// <summary>
    /// Builds a Generic-rig Animator Controller using the DragonSoulEater FBX clips.
    /// Run from Tools > Walking Dragon > Create/Refresh Animator Controller.
    /// </summary>
    public static class WalkingDragonAnimatorBuilder
    {
        private const string AnimationFolder = "Assets/_Project/Features/Leviathan/Animations/DragonSoulEater";
        private const string ControllerPath = "Assets/_Project/Features/Leviathan/WalkingDragonAnimator.controller";

        [MenuItem("Tools/Walking Dragon/Create-Refresh Animator Controller")]
        public static void CreateOrRefreshController()
        {
            Dictionary<string, AnimationClip> clips = LoadRequiredClips();
            if (clips == null)
                return;

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Refresh Walking Dragon Animator",
                    "A WalkingDragonAnimator.controller already exists. Replace it with the generated state machine?",
                    "Replace",
                    "Cancel");
                if (!replace)
                    return;

                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"Could not create Animator Controller at '{ControllerPath}'. Make sure the Leviathan folder exists.");
                return;
            }

            AddParameters(controller);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = machine.AddState("Ground Locomotion", new Vector3(300f, 80f, 0f));
            machine.defaultState = locomotion;
            BlendTree groundBlend = CreateGroundBlendTree(controller, clips);
            locomotion.motion = groundBlend;

            AnimatorState roar = AddState(machine, "Roar", clips["Scream"], 60f, 280f);
            AnimatorState fireball = AddState(machine, "Fireball", clips["Fireball Shoot"], 300f, 420f);
            AnimatorState flyFireball = AddState(machine, "Fly Fireball", clips["Fly Fireball Shoot"], 500f, 600f);
            AnimatorState takeOff = AddState(machine, "Ram Take Off", clips["Take Off"], 760f, 80f);
            AnimatorState flyForward = AddState(machine, "Ram Flight", clips["Fly Forward"], 980f, 80f);
            AnimatorState ramCharge = AddState(machine, "Ram Charge", clips["Run"], 1190f, 80f);
            AnimatorState ramImpact = AddState(machine, "Ram Impact", clips["Basic Attack"], 1390f, 280f);
            AnimatorState land = AddState(machine, "Land", clips["Land"], 980f, 310f);
            AnimatorState defend = AddState(machine, "Defend", clips["Defend"], 300f, 780f);
            AnimatorState tailAttack = AddState(machine, "Tail Attack", clips["Tail Attack"], 560f, 780f);
            AnimatorState hit = AddState(machine, "Get Hit", clips["Get Hit"], 820f, 780f);
            AnimatorState die = AddState(machine, "Die", clips["Die"], 1080f, 780f);
            AnimatorState sleep = AddState(machine, "Sleeping", clips["Sleeping"], 1340f, 780f);
            AnimatorState flyFloat = AddState(machine, "Fly Float", clips["Fly Float"], 60f, 590f);
            AnimatorState flyGlide = AddState(machine, "Fly Glide", clips["Fly Glide"], 300f, 590f);

            // Any State transitions let gameplay events interrupt locomotion cleanly.
            AddAnyTrigger(machine, die, "Die", 0.08f);
            AddAnyTrigger(machine, roar, "Roar", 0.12f);
            AddAnyTrigger(machine, takeOff, "RamJump", 0.12f);
            AddAnyTrigger(machine, ramCharge, "RamCharge", 0.12f);
            AddAnyTrigger(machine, ramImpact, "RamImpact", 0.10f);
            AddAnyTrigger(machine, land, "Land", 0.12f);
            AddAnyTrigger(machine, flyFireball, "FlyFireball", 0.10f);
            AddAnyTrigger(machine, fireball, "Fireball", 0.10f);
            AddAnyTrigger(machine, tailAttack, "TailAttack", 0.12f);
            AddAnyTrigger(machine, defend, "Defend", 0.12f);
            AddAnyTrigger(machine, hit, "Hit", 0.10f);
            AddAnyTrigger(machine, sleep, "Sleep", 0.15f);
            AddAnyTrigger(machine, flyFloat, "FlyFloat", 0.15f);
            AddAnyTrigger(machine, flyGlide, "FlyGlide", 0.15f);
            AddAnyTrigger(machine, locomotion, "Wake", 0.15f);

            AddExit(roar, locomotion, 0.92f);
            AddExitWithCondition(fireball, ramCharge, 0.92f, AnimatorConditionMode.If, "Charging");
            AddExitWithConditions(fireball, locomotion, 0.92f,
                AnimatorConditionMode.IfNot, "RamAirborne", AnimatorConditionMode.IfNot, "Charging");
            AddExitWithCondition(fireball, flyForward, 0.92f, AnimatorConditionMode.If, "RamAirborne");

            AddExitWithCondition(flyFireball, flyForward, 0.92f, AnimatorConditionMode.If, "RamAirborne");
            AddExitWithCondition(flyFireball, ramCharge, 0.92f, AnimatorConditionMode.If, "Charging");
            AddExitWithConditions(flyFireball, locomotion, 0.92f,
                AnimatorConditionMode.IfNot, "RamAirborne", AnimatorConditionMode.IfNot, "Charging");

            // The ram leap plays Take Off, then loops the forward-flight clip until the
            // AI leaves the leap state. The final charge uses the Run cycle.
            AddExitWithCondition(takeOff, flyForward, 0.82f, AnimatorConditionMode.If, "RamAirborne");
            AddExitWithCondition(takeOff, land, 0.82f, AnimatorConditionMode.IfNot, "RamAirborne");
            AddConditionTransition(flyForward, land, AnimatorConditionMode.IfNot, "RamAirborne", 0.12f, false);
            AddConditionTransition(ramCharge, locomotion, AnimatorConditionMode.IfNot, "Charging", 0.12f, false);
            AddExit(ramImpact, locomotion, 0.92f);
            AddExit(land, locomotion, 0.92f);
            AddExit(defend, locomotion, 0.95f);
            AddExit(tailAttack, locomotion, 0.92f);
            AddExit(hit, locomotion, 0.92f);

            // Sleeping remains in its loop until a Wake trigger is received.

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = controller;
            Debug.Log($"Created Walking Dragon Animator Controller: {ControllerPath}\nAssign it to the dragon model's Animator, then add WalkingDragonAnimatableComponent to the boss root.");
        }

        private static Dictionary<string, AnimationClip> LoadRequiredClips()
        {
            string[] names =
            {
                "Idle", "Walk", "Run", "Scream", "Fireball Shoot", "Fly Fireball Shoot",
                "Take Off", "Fly Forward", "Land", "Basic Attack", "Defend", "Die",
                "Get Hit", "Tail Attack", "Sleeping", "Fly Float", "Fly Glide"
            };

            Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();
            for (int i = 0; i < names.Length; i++)
            {
                string path = $"{AnimationFolder}/{names[i]}.fbx";
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                AnimationClip found = null;
                for (int j = 0; j < assets.Length; j++)
                {
                    if (assets[j] is AnimationClip clip && clip.name == names[i])
                    {
                        found = clip;
                        break;
                    }
                }

                if (found == null)
                {
                    Debug.LogError($"Walking Dragon animation clip '{names[i]}' was not found. Expected FBX: {path}. Import the DragonSoulEater animation folder first.");
                    return null;
                }

                result.Add(names[i], found);
            }

            return result;
        }

        private static void AddParameters(AnimatorController controller)
        {
            AddParameter(controller, "Speed", AnimatorControllerParameterType.Float);
            AddParameter(controller, "VerticalSpeed", AnimatorControllerParameterType.Float);
            AddParameter(controller, "Grounded", AnimatorControllerParameterType.Bool);
            AddParameter(controller, "Jumping", AnimatorControllerParameterType.Bool);
            AddParameter(controller, "Falling", AnimatorControllerParameterType.Bool);
            AddParameter(controller, "RamAirborne", AnimatorControllerParameterType.Bool);
            AddParameter(controller, "Charging", AnimatorControllerParameterType.Bool);

            AddParameter(controller, "Roar", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Fireball", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "FlyFireball", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "RamJump", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "RamCharge", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "RamImpact", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Land", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Defend", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "TailAttack", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Hit", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Die", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Sleep", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "Wake", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "FlyFloat", AnimatorControllerParameterType.Trigger);
            AddParameter(controller, "FlyGlide", AnimatorControllerParameterType.Trigger);
        }

        private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            controller.AddParameter(name, type);
        }

        private static BlendTree CreateGroundBlendTree(AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            BlendTree tree = new BlendTree
            {
                name = "Dragon Ground Locomotion Blend Tree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
                minThreshold = -13f,
                maxThreshold = 13f
            };

            // Negative Speed values are used after a ram impact. Both negative
            // children reuse the Walk clip with negative time scales, so retreat
            // always looks like backward walking (not a forward run/moonwalk).
            // The farther negative threshold accelerates that same backward walk.
            tree.AddChild(clips["Walk"], -13f);
            tree.AddChild(clips["Walk"], -3.5f);
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walk"], 3.5f);
            tree.AddChild(clips["Run"], 13f);

            ChildMotion[] motions = tree.children;
            for (int i = 0; i < motions.Length; i++)
            {
                if (Mathf.Approximately(motions[i].threshold, -13f))
                    motions[i].timeScale = -1.7f;
                else if (Mathf.Approximately(motions[i].threshold, -3.5f))
                    motions[i].timeScale = -1f;
            }
            tree.children = motions;

            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string name, Motion motion, float x, float y)
        {
            AnimatorState state = machine.AddState(name, new Vector3(x, y, 0f));
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void AddAnyTrigger(AnimatorStateMachine machine, AnimatorState destination, string trigger, float duration)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddExit(AnimatorState source, AnimatorState destination, float exitTime)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = 0.15f;
        }

        private static void AddExitWithCondition(
            AnimatorState source,
            AnimatorState destination,
            float exitTime,
            AnimatorConditionMode mode,
            string parameter)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = 0.15f;
            transition.AddCondition(mode, 0f, parameter);
        }


        private static void AddExitWithConditions(
            AnimatorState source,
            AnimatorState destination,
            float exitTime,
            AnimatorConditionMode firstMode,
            string firstParameter,
            AnimatorConditionMode secondMode,
            string secondParameter)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = 0.15f;
            transition.AddCondition(firstMode, 0f, firstParameter);
            transition.AddCondition(secondMode, 0f, secondParameter);
        }

        private static void AddConditionTransition(
            AnimatorState source,
            AnimatorState destination,
            AnimatorConditionMode mode,
            string parameter,
            float duration,
            bool hasExitTime)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = hasExitTime;
            transition.exitTime = hasExitTime ? 0.9f : 0f;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.AddCondition(mode, 0f, parameter);
        }
    }
}
#endif
