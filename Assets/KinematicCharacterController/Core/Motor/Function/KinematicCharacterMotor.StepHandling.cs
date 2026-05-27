using UnityEngine;

namespace KinematicCharacterController
{
    public partial class KinematicCharacterMotor
    {
        /// <summary>
        /// 检测当前命中是否可以被当作可跨越台阶。
        /// Detects valid step opportunities for the current hit.
        /// </summary>
        /// <param name="characterPosition">角色当前位置</param>
        /// <param name="characterRotation">角色当前旋转</param>
        /// <param name="hitPoint">当前碰撞点</param>
        /// <param name="innerHitDirection">沿碰撞面朝角色内部的方向</param>
        /// <param name="stabilityReport">要写入的稳定性报告</param>
        private void DetectSteps(Vector3 characterPosition, Quaternion characterRotation, Vector3 hitPoint, Vector3 innerHitDirection, ref HitStabilityReport stabilityReport)
        {
            Collider tmpCollider;
            RaycastHit outerStepHit;
            Vector3 characterUp = characterRotation * _cachedWorldUp;
            Vector3 verticalCharToHit = Vector3.Project(hitPoint - characterPosition, characterUp);
            Vector3 horizontalCharToHitDirection = Vector3.ProjectOnPlane(hitPoint - characterPosition, characterUp).normalized;
            Vector3 stepCheckStartPos = (hitPoint - verticalCharToHit) + (characterUp * MaxStepHeight) + (horizontalCharToHitDirection * CollisionOffset * 3f);

            int nbStepHits = CharacterCollisionsSweep(
                stepCheckStartPos,
                characterRotation,
                -characterUp,
                MaxStepHeight + CollisionOffset,
                out outerStepHit,
                _internalCharacterHits,
                0f,
                true);

            if (CheckStepValidity(nbStepHits, characterPosition, characterRotation, innerHitDirection, stepCheckStartPos, out tmpCollider))
            {
                stabilityReport.ValidStepDetected = true;
                stabilityReport.SteppedCollider = tmpCollider;
            }

            if (StepHandling == StepHandlingMethod.Extra && !stabilityReport.ValidStepDetected)
            {
                stepCheckStartPos = characterPosition + (characterUp * MaxStepHeight) + (-innerHitDirection * MinRequiredStepDepth);
                nbStepHits = CharacterCollisionsSweep(
                    stepCheckStartPos,
                    characterRotation,
                    -characterUp,
                    MaxStepHeight - CollisionOffset,
                    out outerStepHit,
                    _internalCharacterHits,
                    0f,
                    true);

                if (CheckStepValidity(nbStepHits, characterPosition, characterRotation, innerHitDirection, stepCheckStartPos, out tmpCollider))
                {
                    stabilityReport.ValidStepDetected = true;
                    stabilityReport.SteppedCollider = tmpCollider;
                }
            }
        }

        /// <summary>
        /// 判断某次台阶候选命中是否真的满足跨越条件。
        /// Checks if a candidate step hit is actually valid.
        /// </summary>
        /// <param name="nbStepHits">台阶探测命中数量</param>
        /// <param name="characterPosition">角色当前位置</param>
        /// <param name="characterRotation">角色当前旋转</param>
        /// <param name="innerHitDirection">沿碰撞面朝角色内部的方向</param>
        /// <param name="stepCheckStartPos">台阶检测起点</param>
        /// <param name="hitCollider">输出：被当作台阶的碰撞体</param>
        /// <returns>是否找到有效台阶</returns>
        private bool CheckStepValidity(int nbStepHits, Vector3 characterPosition, Quaternion characterRotation, Vector3 innerHitDirection, Vector3 stepCheckStartPos, out Collider hitCollider)
        {
            hitCollider = null;
            Vector3 characterUp = characterRotation * Vector3.up;
            bool foundValidStepPosition = false;

            while (nbStepHits > 0 && !foundValidStepPosition)
            {
                RaycastHit farthestHit = new RaycastHit();
                float farthestDistance = 0f;
                int farthestIndex = 0;
                for (int i = 0; i < nbStepHits; i++)
                {
                    float hitDistance = _internalCharacterHits[i].distance;
                    if (hitDistance > farthestDistance)
                    {
                        farthestDistance = hitDistance;
                        farthestHit = _internalCharacterHits[i];
                        farthestIndex = i;
                    }
                }

                Vector3 characterPositionAtHit = stepCheckStartPos + (-characterUp * (farthestHit.distance - CollisionOffset));
                int atStepOverlaps = CharacterCollisionsOverlap(characterPositionAtHit, characterRotation, _internalProbedColliders);
                if (atStepOverlaps <= 0)
                {
                    if (CharacterCollisionsRaycast(
                            farthestHit.point + (characterUp * SecondaryProbesVertical) + (-innerHitDirection * SecondaryProbesHorizontal),
                            -characterUp,
                            MaxStepHeight + SecondaryProbesVertical,
                            out RaycastHit outerSlopeHit,
                            _internalCharacterHits,
                            true) > 0)
                    {
                        if (IsStableOnNormal(outerSlopeHit.normal))
                        {
                            // 先确认台阶上方没有阻挡，再继续做内侧地面检测。
                            if (CharacterCollisionsSweep(
                                    characterPosition,
                                    characterRotation,
                                    characterUp,
                                    MaxStepHeight - farthestHit.distance,
                                    out RaycastHit tmpUpObstructionHit,
                                    _internalCharacterHits) <= 0)
                            {
                                bool innerStepValid = false;
                                RaycastHit innerStepHit;

                                if (AllowSteppingWithoutStableGrounding)
                                {
                                    innerStepValid = true;
                                }
                                else if (CharacterCollisionsRaycast(
                                            characterPosition + Vector3.Project(characterPositionAtHit - characterPosition, characterUp),
                                            -characterUp,
                                            MaxStepHeight,
                                            out innerStepHit,
                                            _internalCharacterHits,
                                            true) > 0 && IsStableOnNormal(innerStepHit.normal))
                                {
                                    innerStepValid = true;
                                }

                                if (!innerStepValid)
                                {
                                    if (CharacterCollisionsRaycast(
                                            farthestHit.point + (innerHitDirection * SecondaryProbesHorizontal),
                                            -characterUp,
                                            MaxStepHeight,
                                            out innerStepHit,
                                            _internalCharacterHits,
                                            true) > 0 && IsStableOnNormal(innerStepHit.normal))
                                    {
                                        innerStepValid = true;
                                    }
                                }

                                if (innerStepValid)
                                {
                                    hitCollider = farthestHit.collider;
                                    foundValidStepPosition = true;
                                    return true;
                                }
                            }
                        }
                    }
                }

                if (!foundValidStepPosition)
                {
                    nbStepHits--;
                    if (farthestIndex < nbStepHits)
                    {
                        _internalCharacterHits[farthestIndex] = _internalCharacterHits[nbStepHits];
                    }
                }
            }

            return false;
        }
    }
}
