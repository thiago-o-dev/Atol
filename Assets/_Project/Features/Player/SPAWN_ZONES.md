# Spawn Zones setup

`SpawnZone` is a checkpoint trigger. Add it to a scene GameObject with a `BoxCollider`; the component automatically sets the collider to **Is Trigger**.

For each checkpoint:

1. Create a GameObject for the zone volume and size its `BoxCollider` to cover the area the player must reach.
2. Add `SpawnZone` to that object.
3. Create a child or nearby marker Transform for the player's respawn position and assign it to **Player Spawn Point**. If left empty, the zone object's own Transform is used.
4. Assign the scene's `BoatController` to **Boat** when there is more than one boat, or when you want to explicitly choose which boat belongs to this checkpoint. If left empty, the player's current boat is preferred; otherwise the first active scene boat is used.
5. Create a marker Transform for the boat's respawn position and assign it to **Boat Spawn Point**. If left empty, the selected boat returns to its original scene position.

When the player enters the trigger, that zone becomes the active checkpoint. On death, the player is moved to its Player Spawn Point and the selected boat is moved to its Boat Spawn Point. Other boats return to the positions they had when `PlayerRespawn` initialized. The boat Rigidbody's momentum is cleared and `BoatController.Anchor()` is reapplied after teleporting.

If no SpawnZone has been reached, respawn uses the player's starting pose and the boats' starting poses, preserving the previous behavior.
