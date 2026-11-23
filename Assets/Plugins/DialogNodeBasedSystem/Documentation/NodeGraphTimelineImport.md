# Importing a Linear Dialog Node Graph into Timeline

The editor importer converts a **single linear chain** of `SentenceNode` and `ExternalFunctionNode` assets into clips on a `DialogBehaviourTrack`.

## How to use it

1. Open a Timeline asset that contains a `DialogBehaviourTrack` bound to the scene object's `DialogBehaviour` component.
2. In the **Timeline window**, right-click the `DialogBehaviourTrack` row/header (the track name area, not an empty part of the timeline and not the track's Inspector).
3. Choose **Import Dialog Node Graph...** from the Timeline context menu. This is a real Timeline track action, not an Inspector-only context menu item.
4. In the importer window, assign the `DialogNodeGraph` asset, review the validated sequence and timing preview, then click **Import Graph as Timeline Clips**.

The importer can also be opened from the `DialogNodeGraph` asset's Inspector context menu or from **Tools > Dialog Node Based System > Import Node Graph To Timeline**. Those alternate entry points still require a target track to be assigned in the importer window.

## Supported graph structure

The importer intentionally supports only these node types:

- `SentenceNode` → `SentencePerformingClipAsset`
- `ExternalFunctionNode` → `CallExternalFunctionClipAsset`

The graph must be one fully connected, acyclic, linear chain: exactly one starting node, one incoming connection at most per node, no branches, and no disconnected nodes. The order comes from following each node's `ChildNode` connection from the unique starting node. Parent lists must agree with those connections because the runtime dialog system uses them to identify the starting node.

`AnswerNode`, `ModifyVariableNode`, `VariableConditionNode`, and all other node types are rejected. Graphs with multiple starts, multiple parents, cycles, missing references, or disconnected nodes are also rejected. The importer does not flatten branches or silently skip unsupported nodes.

## Timing defaults

- **Seconds Per Character:** `0.05` seconds.
- **Minimum Sentence Gap:** `1` second.
- **Maximum Sentence Gap:** `3` seconds.
- **Characters For Maximum Gap:** `100`.
- **External Function Clip Duration:** `0.1` seconds.

A sentence clip duration is `max(0.05, characterCount × secondsPerCharacter)`. The pause between sentence clips is linearly interpolated from the minimum to maximum gap as sentence length approaches the configured character threshold. No extra gap is added after the final sentence. For example, a 50-character sentence with the default settings receives a 2-second gap before the next sentence.

External Function nodes become short trigger clips in their exact graph order. When one appears between two sentences, the call clip is placed immediately after the previous sentence and the next sentence still waits until the calculated sentence gap has elapsed. Multiple calls can therefore occur in sequence during that gap.

Text length is read through `SentenceNode.GetText()` at import time. This uses the text returned by the current localization context and counts whitespace and rich-text markup characters as part of the string length. Runtime variable substitutions can change the final text length, so adjust durations manually when needed.

## Re-importing and placement

- **Replace clips previously imported from this graph** is enabled by default. It removes only sentence and external-function clips tagged with the same source graph.
- **Append after the last existing clip** places the new sequence after all remaining clips on the target track.
- Otherwise, the importer starts at **Start Time (seconds)**.
- Close Dialog clips and manually created clips are not removed by the replace option.

The track must remain bound to the `DialogBehaviour` component. Imported sentence clips use the normal `PerformSentenceNode` runtime path, and external-function clips use `DialogBehaviour.CallExternalFunction` through the track binding.
