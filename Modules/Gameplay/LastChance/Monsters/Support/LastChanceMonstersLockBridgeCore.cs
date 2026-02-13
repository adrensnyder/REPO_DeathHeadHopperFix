#nullable enable

using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support
{
    internal static class LastChanceMonstersLockBridgeCore
    {
        internal enum LockStabilizeKind
        {
            None,
            Force,
            Snap,
            Emergency
        }

        internal readonly struct LockStabilizeResult
        {
            internal LockStabilizeResult(LockStabilizeKind kind, float distance, float offLockTimer, float forceMagnitude)
            {
                Kind = kind;
                Distance = distance;
                OffLockTimer = offLockTimer;
                ForceMagnitude = forceMagnitude;
            }

            internal LockStabilizeKind Kind { get; }
            internal float Distance { get; }
            internal float OffLockTimer { get; }
            internal float ForceMagnitude { get; }
            internal bool Applied => Kind != LockStabilizeKind.None;
        }

        internal readonly struct HeadCoupleResult
        {
            internal HeadCoupleResult(bool hardSnap, float distance, float forceMagnitude)
            {
                HardSnap = hardSnap;
                Distance = distance;
                ForceMagnitude = forceMagnitude;
            }

            internal bool HardSnap { get; }
            internal float Distance { get; }
            internal float ForceMagnitude { get; }
        }

        internal static bool IsHeadProxyRuntimeApplicable(PlayerAvatar? player)
        {
            return player != null &&
                   LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() &&
                   LastChanceMonstersTargetProxyHelper.IsMasterContext() &&
                   LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
        }

        internal static LockStabilizeResult StabilizeTargetAtLockPoint(
            Rigidbody targetRb,
            Transform lockPoint,
            float offLockPointTimer,
            bool fixedUpdate,
            bool isPrimaryLockState)
        {
            if (!fixedUpdate || !isPrimaryLockState || targetRb == null || lockPoint == null)
            {
                return new LockStabilizeResult(LockStabilizeKind.None, 0f, offLockPointTimer, 0f);
            }

            var distToLock = Vector3.Distance(targetRb.position, lockPoint.position);
            if (distToLock <= 0.9f)
            {
                return new LockStabilizeResult(LockStabilizeKind.None, distToLock, offLockPointTimer, 0f);
            }

            if (offLockPointTimer > 1.2f)
            {
                var emergency = Vector3.Lerp(targetRb.position, lockPoint.position, 0.65f);
                targetRb.position = emergency;
                targetRb.velocity = Vector3.Lerp(targetRb.velocity, Vector3.zero, 0.7f);
                return new LockStabilizeResult(LockStabilizeKind.Emergency, distToLock, offLockPointTimer, 0f);
            }

            if (distToLock > 1.6f)
            {
                var snap = Vector3.Lerp(targetRb.position, lockPoint.position, 0.35f);
                targetRb.position = snap;
                targetRb.velocity = Vector3.Lerp(targetRb.velocity, Vector3.zero, 0.5f);
                return new LockStabilizeResult(LockStabilizeKind.Snap, distToLock, offLockPointTimer, 0f);
            }

            var follow = SemiFunc.PhysFollowPosition(targetRb.position, lockPoint.position, targetRb.velocity, 7f);
            targetRb.AddForce(follow, ForceMode.Acceleration);
            return new LockStabilizeResult(LockStabilizeKind.Force, distToLock, offLockPointTimer, follow.magnitude);
        }

        internal static HeadCoupleResult CoupleHeadToTarget(
            PhysGrabObject headPhys,
            Rigidbody headRb,
            Rigidbody targetRb,
            bool fixedUpdate)
        {
            var distance = Vector3.Distance(headRb.position, targetRb.position);
            if (distance > 2.5f)
            {
                headRb.position = targetRb.position;
                headRb.rotation = targetRb.rotation;
                headRb.velocity = targetRb.velocity;
                headRb.angularVelocity = targetRb.angularVelocity;
                return new HeadCoupleResult(true, distance, 0f);
            }

            headPhys.OverrideZeroGravity(0.1f);

            var follow = SemiFunc.PhysFollowPosition(headRb.position, targetRb.position, headRb.velocity, 5f);
            var dir = targetRb.position - headRb.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                var torque = SemiFunc.PhysFollowDirection(headRb.transform, dir.normalized, headRb, 0.5f);
                headRb.AddTorque(torque / Mathf.Max(headRb.mass, 0.0001f), ForceMode.Force);
            }

            headRb.AddForce(follow, fixedUpdate ? ForceMode.Acceleration : ForceMode.Force);
            headRb.velocity = Vector3.Lerp(headRb.velocity, targetRb.velocity, 0.35f);
            headRb.angularVelocity = Vector3.Lerp(headRb.angularVelocity, targetRb.angularVelocity, 0.35f);
            return new HeadCoupleResult(false, distance, follow.magnitude);
        }
    }
}
