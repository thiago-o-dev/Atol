using UnityEngine;

namespace Assets._Project.Features.Movement
{
    public interface IMovement
    {
        void Move(Vector2 inputDirection);
        void Jump(bool released);
        void ApplyMovement();
    }

    /// <summary>
    /// Optional state-transfer contract for movement modes that move the player
    /// directly. Keeping planar velocity across modes avoids a dead stop when
    /// transitioning between grounded and swimming movement.
    /// </summary>
    public interface IPlanarVelocityMovement
    {
        Vector3 PlanarVelocity { get; }
        void SetPlanarVelocity(Vector3 velocity);
    }
    /// <summary>Optional buoyancy controls exposed by reusable swimming actors.</summary>
    public interface ISwimmingBuoyancyControl
    {
        void SetBuoyancyInput(float value);
        void SetDrowning(bool drowning);
        void SetTargetSubmersion(float fraction);
    }

}
