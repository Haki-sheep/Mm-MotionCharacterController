using UnityEngine;

namespace KinematicCharacterController
{
    public partial class KinematicCharacterMotor
    {
        #region Kcc外部调用接口

        // ---------------------------角色状态获取与同步--------------------------------
        /// <summary>
        /// 获取当前 Motor 的完整运行状态快照。
        /// Returns all the state information of the motor that is pertinent for simulation.
        /// </summary>
        /// <returns>当前运动控制器状态</returns>
        public KinematicCharacterMotorState GetState()
        {
            KinematicCharacterMotorState state = new KinematicCharacterMotorState();

            state.Position = _transientPosition;
            state.Rotation = _transientRotation;

            state.BaseVelocity = BaseVelocity;
            state.AttachedRigidbodyVelocity = _attachedRigidbodyVelocity;

            state.MustUnground = _mustUnground;
            state.MustUngroundTime = _mustUngroundTimeCounter;
            state.LastMovementIterationFoundAnyGround = LastMovementIterationFoundAnyGround;
            state.GroundingStatus.CopyFrom(GroundingStatus);
            state.AttachedRigidbody = _attachedRigidbody;

            return state;
        }

        /// <summary>
        /// 立即把 Motor 同步到指定状态。
        /// Applies a motor state instantly.
        /// </summary>
        /// <param name="state">要应用的运动控制器状态</param>
        /// <param name="bypassInterpolation">是否跳过插值，直接同步到目标状态</param>
        public void ApplyState(KinematicCharacterMotorState state,
                               bool bypassInterpolation = true)
        {
            // 直接角色设置旋转位移
            SetPositionAndRotation(state.Position, state.Rotation, bypassInterpolation);

            // 同步数据
            BaseVelocity = state.BaseVelocity;
            _attachedRigidbodyVelocity = state.AttachedRigidbodyVelocity;

            _mustUnground = state.MustUnground;
            _mustUngroundTimeCounter = state.MustUngroundTime;
            LastMovementIterationFoundAnyGround = state.LastMovementIterationFoundAnyGround;

            GroundingStatus.CopyFrom(state.GroundingStatus);
            _attachedRigidbody = state.AttachedRigidbody;
        }

        // ---------------------------地面状态控制--------------------------------
        /// <summary>
        /// 强制角色在下一次地面更新时脱离地面。
        /// Forces the character to unground itself on its next grounding update.
        /// </summary>
        /// <param name="time">持续强制脱离地面的时间</param>
        public void ForceUnground(float time = 0.1f)
        {
            _mustUnground = true;
            _mustUngroundTimeCounter = time;
        }

        // ---------------------------Kcc的旋转位移控制--------------------------------
        /// <summary>
        /// 请求角色移动到指定位置，真正执行会发生在下次 Motor 更新时。
        /// Moves the character position, taking all movement collision solving into account.
        /// </summary>
        /// <param name="toPosition">目标位置</param>
        public void MoveCharacter(Vector3 toPosition)
        {
            _movePositionDirty = true;
            _movePositionTarget = toPosition;
        }

        /// <summary>
        /// 请求角色旋转到指定朝向，真正执行会发生在下次 Motor 更新时。
        /// Moves the character rotation. The actual move is done the next time the motor updates are called.
        /// </summary>
        /// <param name="toRotation">目标旋转</param>
        public void RotateCharacter(Quaternion toRotation)
        {
            _moveRotationDirty = true;
            _moveRotationTarget = toRotation;
        }

        /// <summary>
        /// 直接设置角色位置，不经过碰撞解算。
        /// Sets the character's position directly.
        /// </summary>
        /// <param name="position">目标位置</param>
        /// <param name="bypassInterpolation">是否跳过插值</param>
        public void SetPosition(Vector3 position, bool bypassInterpolation = true)
        {
            _transform.position = position;
            _initialSimulationPosition = position;
            _transientPosition = position;

            if (bypassInterpolation)
            {
                InitialTickPosition = position;
            }
        }

        /// <summary>
        /// 直接设置角色旋转，不经过碰撞解算。
        /// Sets the character's rotation directly.
        /// </summary>
        /// <param name="rotation">目标旋转</param>
        /// <param name="bypassInterpolation">是否跳过插值</param>
        public void SetRotation(Quaternion rotation, bool bypassInterpolation = true)
        {
            _transform.rotation = rotation;
            _initialSimulationRotation = rotation;
            TransientRotation = rotation;

            if (bypassInterpolation)
            {
                InitialTickRotation = rotation;
            }
        }

        /// <summary>
        /// 直接设置角色位置和旋转，不经过碰撞解算。
        /// Sets the character's position and rotation directly.
        /// </summary>
        /// <param name="position">目标位置</param>
        /// <param name="rotation">目标旋转</param>
        /// <param name="bypassInterpolation">是否跳过插值</param>
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation, bool bypassInterpolation = true)
        {
            _transform.SetPositionAndRotation(position, rotation);
            _initialSimulationPosition = position;
            _initialSimulationRotation = rotation;
            _transientPosition = position;
            TransientRotation = rotation;

            if (bypassInterpolation)
            {
                InitialTickPosition = position;
                InitialTickRotation = rotation;
            }
        }

        // ---------------------------Kcc的碰撞状态相关控制--------------------------------
        /// <summary>
        /// 开启或关闭胶囊体的碰撞检测。
        /// Sets whether or not the capsule collider will detect collisions.
        /// </summary>
        /// <param name="collisionsActive">是否启用碰撞</param>
        public void SetCapsuleCollisionsActivation(bool collisionsActive)
        {
            Capsule.isTrigger = !collisionsActive;
        }

        /// <summary>
        /// 开启或关闭移动过程中的碰撞解算。
        /// Sets whether or not the motor will solve collisions when moving.
        /// </summary>
        /// <param name="movementCollisionsSolvingActive">是否启用移动碰撞解算</param>
        public void SetMovementCollisionsSolvingActivation(bool movementCollisionsSolvingActive)
        {
            _solveMovementCollisions = movementCollisionsSolvingActive;
        }

        /// <summary>
        /// 开启或关闭地面检测与稳定性解算。
        /// Sets whether or not grounding will be evaluated for all hits.
        /// </summary>
        /// <param name="stabilitySolvingActive">是否启用地面解算</param>
        public void SetGroundSolvingActivation(bool stabilitySolvingActive)
        {
            _solveGrounding = stabilitySolvingActive;
        }

        #endregion
    }
}
