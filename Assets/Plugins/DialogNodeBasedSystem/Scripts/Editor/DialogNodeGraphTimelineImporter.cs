#if UNITY_EDITOR && UNITY_TIMELINE
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace cherrydev
{
    /// <summary>
    /// Converts a linear DialogNodeGraph containing only SentenceNode and ExternalFunctionNode
    /// nodes into clips on a DialogBehaviourTrack.
    /// </summary>
    public class DialogNodeGraphTimelineImporter : EditorWindow
    {
        private DialogBehaviourTrack _targetTrack;
        private DialogNodeGraph _nodeGraph;

        private float _secondsPerCharacter = 0.05f;
        private float _minimumGap = 1f;
        private float _maximumGap = 3f;
        private int _charactersForMaximumGap = 100;
        private float _externalFunctionClipDuration = 0.1f;
        private double _startTime;
        private bool _appendAfterExistingClips;
        private bool _replaceClipsFromThisGraph = true;

        private Vector2 _previewScroll;
        private List<Node> _validatedSequence = new List<Node>();
        private string _validationError;

        [MenuItem("CONTEXT/DialogBehaviourTrack/Import Node Graph...")]
        private static void OpenFromTrackContext(MenuCommand command)
        {
            OpenWindow(command.context as DialogBehaviourTrack, null);
        }

        [MenuItem("CONTEXT/DialogNodeGraph/Import to Timeline...")]
        private static void OpenFromGraphContext(MenuCommand command)
        {
            OpenWindow(null, command.context as DialogNodeGraph);
        }

        [MenuItem("Tools/Dialog Node Based System/Import Node Graph To Timeline")]
        private static void OpenFromSelection()
        {
            DialogBehaviourTrack track = Selection.activeObject as DialogBehaviourTrack;
            DialogNodeGraph graph = Selection.activeObject as DialogNodeGraph;
            OpenWindow(track, graph);
        }

        [MenuItem("Tools/Dialog Node Based System/Import Node Graph To Timeline", true)]
        private static bool ValidateOpenFromSelection()
        {
            return Selection.activeObject is DialogBehaviourTrack || Selection.activeObject is DialogNodeGraph;
        }

        private static void OpenWindow(DialogBehaviourTrack track, DialogNodeGraph graph)
        {
            DialogNodeGraphTimelineImporter window = GetWindow<DialogNodeGraphTimelineImporter>(true,
                "Import Dialog Graph", true);
            // Assign both values, including null, so opening the importer from a different
            // context never leaves stale references from a previous import session.
            window._targetTrack = track;
            window._nodeGraph = graph;
            window.minSize = new Vector2(410f, 500f);
            window.RefreshValidation();
            window.Show();
        }

        /// <summary>
        /// Opens the graph importer for a track selected from the Timeline track context menu.
        /// </summary>
        public static void OpenForTimelineTrack(DialogBehaviourTrack track)
        {
            if (track == null)
                return;

            OpenWindow(track, null);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Linear Node Graph to Timeline", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            _targetTrack = (DialogBehaviourTrack)EditorGUILayout.ObjectField(
                "Target Track", _targetTrack, typeof(DialogBehaviourTrack), true);
            _nodeGraph = (DialogNodeGraph)EditorGUILayout.ObjectField(
                "Node Graph", _nodeGraph, typeof(DialogNodeGraph), false);
            if (EditorGUI.EndChangeCheck())
                RefreshValidation();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
            _secondsPerCharacter = Mathf.Max(0f,
                EditorGUILayout.FloatField("Seconds Per Character", _secondsPerCharacter));
            _minimumGap = Mathf.Max(0f, EditorGUILayout.FloatField("Minimum Sentence Gap", _minimumGap));
            _maximumGap = Mathf.Max(_minimumGap,
                EditorGUILayout.FloatField("Maximum Sentence Gap", _maximumGap));
            _charactersForMaximumGap = Mathf.Max(1,
                EditorGUILayout.IntField("Characters For Maximum Gap", _charactersForMaximumGap));
            _externalFunctionClipDuration = Mathf.Max(0.01f,
                EditorGUILayout.FloatField("External Function Clip Duration", _externalFunctionClipDuration));

            EditorGUILayout.HelpBox(
                "Sentence clip duration = text character count × seconds per character. " +
                "The pause between sentence clips scales from the minimum to maximum according to " +
                "sentence length. External Function nodes become short trigger clips and are placed " +
                "in graph order; the sentence pause is preserved before the next sentence.",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            _appendAfterExistingClips = EditorGUILayout.ToggleLeft(
                "Append after the last existing clip", _appendAfterExistingClips);
            using (new EditorGUI.DisabledScope(_appendAfterExistingClips))
            {
                _startTime = Math.Max(0d, EditorGUILayout.DoubleField("Start Time (seconds)", _startTime));
            }
            _replaceClipsFromThisGraph = EditorGUILayout.ToggleLeft(
                "Replace clips previously imported from this graph", _replaceClipsFromThisGraph);

            EditorGUILayout.Space(8f);
            DrawGraphPreview();

            EditorGUILayout.Space(8f);
            bool canImport = _targetTrack != null && _targetTrack.timelineAsset != null &&
                             _nodeGraph != null && string.IsNullOrEmpty(_validationError) &&
                             _validatedSequence.Count > 0;
            using (new EditorGUI.DisabledScope(!canImport))
            {
                if (GUILayout.Button("Import Graph as Timeline Clips", GUILayout.Height(32f)))
                    ImportGraph();
            }

            if (_targetTrack == null)
                EditorGUILayout.HelpBox(
                    "Assign a Dialog Behaviour Track, or open this tool from a track's context menu.",
                    MessageType.Warning);
            else if (_targetTrack.timelineAsset == null)
                EditorGUILayout.HelpBox("The selected track is not attached to a Timeline asset.", MessageType.Error);

            if (_nodeGraph == null)
                EditorGUILayout.HelpBox("Assign a Dialog Node Graph to import.", MessageType.Warning);
        }

        private void DrawGraphPreview()
        {
            if (_nodeGraph == null)
                return;

            if (!string.IsNullOrEmpty(_validationError))
            {
                EditorGUILayout.HelpBox(_validationError, MessageType.Error);
                return;
            }

            int sentenceCount = 0;
            int externalCount = 0;
            foreach (Node node in _validatedSequence)
            {
                if (node is SentenceNode)
                    sentenceCount++;
                else if (node is ExternalFunctionNode)
                    externalCount++;
            }

            EditorGUILayout.LabelField(
                $"Validated linear sequence ({sentenceCount} sentence clips, {externalCount} function clips)",
                EditorStyles.boldLabel);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll,
                GUILayout.MinHeight(70f), GUILayout.MaxHeight(190f));
            int sentenceIndex = 0;
            for (int i = 0; i < _validatedSequence.Count; i++)
            {
                Node node = _validatedSequence[i];
                if (node is SentenceNode sentence)
                {
                    sentenceIndex++;
                    string text = SafeGetText(sentence);
                    float duration = GetClipDuration(text);
                    float gap = IsThereAnotherSentenceAfter(i)
                        ? GetGapDuration(text)
                        : 0f;
                    string preview = text.Replace('\r', ' ').Replace('\n', ' ');
                    if (preview.Length > 64)
                        preview = preview.Substring(0, 61) + "...";

                    EditorGUILayout.LabelField(
                        $"{i + 1}. Sentence {sentenceIndex}: {sentence.GetCharacterName()} — {preview}",
                        EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(
                        $"   Clip: {duration:0.00}s   |   Gap before next sentence: {gap:0.00}s",
                        EditorStyles.miniLabel);
                }
                else if (node is ExternalFunctionNode externalNode)
                {
                    EditorGUILayout.LabelField(
                        $"{i + 1}. Call External: {externalNode.GetExternalFunctionName()}",
                        EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(
                        $"   Trigger clip: {_externalFunctionClipDuration:0.00}s",
                        EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.HelpBox(
                "Only Sentence and Call External nodes are supported. The graph must be one connected " +
                "linear chain with exactly one start, no branches, no cycles, and no multiple parents. " +
                "Answer, Modify Variable, Variable Condition, and other node types are rejected.",
                MessageType.Info);
        }

        private bool IsThereAnotherSentenceAfter(int index)
        {
            for (int i = index + 1; i < _validatedSequence.Count; i++)
            {
                if (_validatedSequence[i] is SentenceNode)
                    return true;
            }
            return false;
        }

        private void RefreshValidation()
        {
            _validatedSequence = new List<Node>();
            _validationError = null;
            if (_nodeGraph == null)
                return;

            if (!TryGetLinearSequence(_nodeGraph, out _validatedSequence, out _validationError))
                _validatedSequence.Clear();
        }

        private void ImportGraph()
        {
            RefreshValidation();
            if (_targetTrack == null || _nodeGraph == null || _targetTrack.timelineAsset == null ||
                !string.IsNullOrEmpty(_validationError) || _validatedSequence.Count == 0)
            {
                EditorUtility.DisplayDialog("Cannot Import Node Graph",
                    _validationError ?? "Assign a valid Node Graph and a Dialog Behaviour Track attached to a Timeline asset.",
                    "OK");
                return;
            }

            TimelineAsset timelineAsset = _targetTrack.timelineAsset;
            const string undoName = "Import Dialog Node Graph to Timeline";
            List<TimelineClip> existingClips = new List<TimelineClip>(_targetTrack.GetClips());
            List<TimelineClip> clipsToReplace = new List<TimelineClip>();

            if (_replaceClipsFromThisGraph)
            {
                foreach (TimelineClip existingClip in existingClips)
                {
                    SentencePerformingClipAsset sentenceAsset =
                        existingClip.asset as SentencePerformingClipAsset;
                    CallExternalFunctionClipAsset functionAsset =
                        existingClip.asset as CallExternalFunctionClipAsset;

                    if ((sentenceAsset != null && sentenceAsset.SourceGraph == _nodeGraph) ||
                        (functionAsset != null && functionAsset.SourceGraph == _nodeGraph))
                    {
                        clipsToReplace.Add(existingClip);
                    }
                }
            }

            double cursor = _startTime;
            if (_appendAfterExistingClips)
            {
                foreach (TimelineClip existingClip in existingClips)
                {
                    if (!clipsToReplace.Contains(existingClip))
                        cursor = Math.Max(cursor, existingClip.end);
                }
            }

            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { timelineAsset, _targetTrack }, undoName);

            foreach (TimelineClip oldClip in clipsToReplace)
                _targetTrack.DeleteClip(oldClip);

            int importedSentenceCount = 0;
            int importedFunctionCount = 0;
            bool hasPreviousSentence = false;
            double earliestNextSentenceTime = cursor;

            for (int i = 0; i < _validatedSequence.Count; i++)
            {
                Node node = _validatedSequence[i];

                if (node is SentenceNode sentenceNode)
                {
                    // Keep function calls in their graph position, but do not let them consume
                    // the sentence-to-sentence pause. The next sentence starts after both the
                    // pending pause and any function trigger clips that occur in between.
                    if (hasPreviousSentence)
                        cursor = Math.Max(cursor, earliestNextSentenceTime);

                    string text = SafeGetText(sentenceNode);
                    double duration = GetClipDuration(text);
                    TimelineClip timelineClip = _targetTrack.CreateClip<SentencePerformingClipAsset>();
                    SentencePerformingClipAsset clipAsset = timelineClip.asset as SentencePerformingClipAsset;
                    if (clipAsset == null)
                    {
                        Debug.LogError("Failed to create a Sentence Performing clip on the selected track.");
                        continue;
                    }

                    clipAsset.SentenceNode = new ExposedReference<SentenceNode>
                    {
                        defaultValue = sentenceNode
                    };
                    clipAsset.SourceGraph = _nodeGraph;

                    timelineClip.start = cursor;
                    timelineClip.duration = duration;
                    timelineClip.displayName = BuildSentenceClipName(sentenceNode, text);

                    EditorUtility.SetDirty(clipAsset);
                    cursor += duration;
                    earliestNextSentenceTime = cursor + GetGapDuration(text);
                    hasPreviousSentence = true;
                    importedSentenceCount++;
                }
                else if (node is ExternalFunctionNode externalNode)
                {
                    TimelineClip timelineClip = _targetTrack.CreateClip<CallExternalFunctionClipAsset>();
                    CallExternalFunctionClipAsset clipAsset = timelineClip.asset as CallExternalFunctionClipAsset;
                    if (clipAsset == null)
                    {
                        Debug.LogError("Failed to create a Call External Function clip on the selected track.");
                        continue;
                    }

                    clipAsset.FunctionName = externalNode.GetExternalFunctionName();
                    clipAsset.SourceGraph = _nodeGraph;

                    timelineClip.start = cursor;
                    timelineClip.duration = _externalFunctionClipDuration;
                    timelineClip.displayName = string.IsNullOrWhiteSpace(clipAsset.FunctionName)
                        ? "Call External Function"
                        : $"Call: {clipAsset.FunctionName}";

                    EditorUtility.SetDirty(clipAsset);
                    cursor += _externalFunctionClipDuration;
                    importedFunctionCount++;
                }
            }

            EditorUtility.SetDirty(_targetTrack);
            EditorUtility.SetDirty(timelineAsset);
            AssetDatabase.SaveAssets();

            string summary = $"Imported {importedSentenceCount} sentences and {importedFunctionCount} external calls.";
            ShowNotification(new GUIContent(summary));
            Debug.Log($"{summary} Source graph: '{_nodeGraph.name}'. Target track: '{_targetTrack.name}'.");
        }

        private float GetClipDuration(string text)
        {
            // A Timeline clip must have a positive duration, including for an empty sentence.
            return Mathf.Max(0.05f, (text ?? string.Empty).Length * _secondsPerCharacter);
        }

        private float GetGapDuration(string text)
        {
            int characterCount = (text ?? string.Empty).Length;
            float normalizedLength = Mathf.Clamp01(
                characterCount / (float)Mathf.Max(1, _charactersForMaximumGap));
            return Mathf.Lerp(_minimumGap, _maximumGap, normalizedLength);
        }

        private static string SafeGetText(SentenceNode sentenceNode)
        {
            if (sentenceNode == null)
                return string.Empty;

            try
            {
                return sentenceNode.GetText() ?? string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read text for '{sentenceNode.name}': {exception.Message}");
                return string.Empty;
            }
        }

        private static string BuildSentenceClipName(SentenceNode sentenceNode, string text)
        {
            string characterName = sentenceNode.GetCharacterName();
            if (string.IsNullOrWhiteSpace(characterName))
                characterName = "Sentence";

            string preview = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (preview.Length > 42)
                preview = preview.Substring(0, 39) + "...";

            return string.IsNullOrEmpty(preview) ? characterName : $"{characterName}: {preview}";
        }

        /// <summary>
        /// Validates the whole graph and returns its exact linked-list order. No graph traversal
        /// through branches or unsupported intermediary node types is performed.
        /// </summary>
        private static bool TryGetLinearSequence(
            DialogNodeGraph graph, out List<Node> sequence, out string error)
        {
            sequence = new List<Node>();
            error = null;

            if (graph == null || graph.NodesList == null || graph.NodesList.Count == 0)
            {
                error = "The selected Dialog Node Graph is empty.";
                return false;
            }

            HashSet<Node> graphNodes = new HashSet<Node>();
            for (int i = 0; i < graph.NodesList.Count; i++)
            {
                Node node = graph.NodesList[i];
                if (node == null)
                {
                    error = $"The graph contains a missing node reference at list index {i}. Remove the missing reference before importing.";
                    return false;
                }

                if (!(node is SentenceNode) && !(node is ExternalFunctionNode))
                {
                    error = $"Unsupported node type '{node.GetType().Name}' found ('{node.name}'). " +
                            "Timeline import supports only Sentence and Call External nodes. " +
                            "Remove unsupported nodes or create a separate linear graph.";
                    return false;
                }

                if (!graphNodes.Add(node))
                {
                    error = $"Node '{node.name}' appears more than once in the graph's NodesList.";
                    return false;
                }
            }

            Dictionary<Node, int> incomingCounts = new Dictionary<Node, int>();
            Dictionary<Node, Node> incomingParents = new Dictionary<Node, Node>();
            foreach (Node node in graphNodes)
                incomingCounts[node] = 0;

            foreach (Node node in graphNodes)
            {
                Node child = GetChild(node);
                if (child == null)
                    continue;

                if (!graphNodes.Contains(child))
                {
                    error = $"Node '{node.name}' connects to '{child.name}', which is not in this graph's NodesList.";
                    return false;
                }

                incomingCounts[child]++;
                incomingParents[child] = node;
                if (incomingCounts[child] > 1)
                {
                    error = $"Node '{child.name}' has multiple incoming connections. Only a single-parent linear dialog is supported.";
                    return false;
                }
            }

            Node startNode = null;
            int startCount = 0;
            foreach (Node node in graph.NodesList)
            {
                if (incomingCounts[node] == 0)
                {
                    startNode = node;
                    startCount++;
                }
            }

            if (startCount != 1)
            {
                error = startCount == 0
                    ? "No starting node was found. The graph may contain a cycle."
                    : $"The graph contains {startCount} starting nodes or disconnected chains. Exactly one connected linear chain is required.";
                return false;
            }

            // Match the ParentNodes data used by DialogBehaviour to find the runtime start node.
            foreach (Node node in graphNodes)
            {
                List<Node> parents = GetParents(node);
                int expectedCount = incomingCounts[node];
                if (parents == null || parents.Count != expectedCount)
                {
                    error = $"The parent connections for '{node.name}' do not match its child connections. " +
                            "Reconnect the node in the graph editor before importing.";
                    return false;
                }

                if (expectedCount == 1 && !parents.Contains(incomingParents[node]))
                {
                    error = $"The parent list for '{node.name}' is inconsistent with its incoming connection.";
                    return false;
                }
            }

            HashSet<Node> visited = new HashSet<Node>();
            Node current = startNode;
            while (current != null)
            {
                if (!visited.Add(current))
                {
                    error = $"A cycle was detected at node '{current.name}'. Only a linear acyclic graph can be imported.";
                    sequence.Clear();
                    return false;
                }

                sequence.Add(current);
                current = GetChild(current);
            }

            if (visited.Count != graphNodes.Count)
            {
                error = $"Only {visited.Count} of {graphNodes.Count} nodes are connected to the starting node. " +
                        "Disconnected nodes or cycles are not supported.";
                sequence.Clear();
                return false;
            }

            return true;
        }

        private static Node GetChild(Node node)
        {
            if (node is SentenceNode sentenceNode)
                return sentenceNode.ChildNode;
            if (node is ExternalFunctionNode externalFunctionNode)
                return externalFunctionNode.ChildNode;
            return null;
        }

        private static List<Node> GetParents(Node node)
        {
            if (node is SentenceNode sentenceNode)
                return sentenceNode.ParentNodes;
            if (node is ExternalFunctionNode externalFunctionNode)
                return externalFunctionNode.ParentNodes;
            return null;
        }
    }
}
#endif
