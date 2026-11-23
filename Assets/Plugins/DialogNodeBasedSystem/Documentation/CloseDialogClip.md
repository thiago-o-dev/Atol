# Closing dialogs from Timeline or scripts

## Timeline

1. Add a **Dialog Behaviour Track** to a Timeline.
2. Bind the track to the GameObject that has `DialogBehaviour`.
3. Place your `Sentence Performing` clips on the track.
4. Add a **Close Dialog Clip** at the point where the dialog should end. The clip closes the bound dialog once when playback reaches it.

Place the close clip after the final sentence clip (rather than overlapping a sentence clip) so later sentence evaluations do not show the panel again.

## From a script

Call `CloseDialog()` on the same `DialogBehaviour` component:

```csharp
[SerializeField] private DialogBehaviour dialogBehaviour;

private void EndBossIntro()
{
    dialogBehaviour.CloseDialog();
}
```

`CloseDialog()` immediately stops the dialog's typing/advance coroutine and invokes the normal dialog-finished callback and event.

## Text skipping vs. dialog advancement

These are independent settings:

- `IsCanSkippingText = false`: player input cannot instantly reveal the remainder of the current sentence. The player can still advance normally when `IsCanAdvanceDialog` is true.
- `IsCanAdvanceDialog = false`: player input cannot advance to the next dialog node. The dialog remains until `CloseDialog()` is called or a **Close Dialog Clip** is reached.

Both settings default to `true` to preserve the previous behavior.

For a scripted boss-intro dialog, set both properties to `false` before `StartDialog(...)`, then call `CloseDialog()` from your boss logic or let a Timeline clip close it at the desired time.
