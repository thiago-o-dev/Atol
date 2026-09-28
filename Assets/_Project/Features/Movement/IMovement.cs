using System;
using UnityEngine;

namespace Assets._Project.Features.Movement
{
    public interface IMovement
    {
        public void Move(Vector2 inputDirection);
        public void Jump(bool released);
        public void ApplyMovement();
    }
}
