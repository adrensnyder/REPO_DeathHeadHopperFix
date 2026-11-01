#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Abilities.Charge;
using DeathHeadHopper.DeathHead;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Stun
{
    internal static class ChargeHoldReleaseModule
    {
        private const string ChargeStrengthLogKey = "Fix:Charge.Strength";
        private const string ChargePermissiveFallbackLogKey = "Fix:Charge.PermissiveFallback";
        private const float RemoteReleaseCommandTag = -777f;
        private const float RemoteCancelCommandTag = -778f;
        private const float ChargeRestStopSeconds = 0.35f;
        private const float ChargeFailsafeSeconds = 10f;
        private const float ChargeRestVelocitySqrThreshold = 0.01f;
        private const float ChargeRestAngularVelocitySqrThreshold = 0.04f;
        private const float GroundContactProbeStartOffset = 0.02f;
        private const float GroundContactProbeDistance = 0.04f;
        private const float GroundContactMinimumNormalY = 0.5f;
        private const float ChargeMotionDiagnosticSampleInterval = 0.5f;
        private const int ChargeMotionDiagnosticMaxSamples = 24;
        private const float PositionCommandDiagnosticInterval = 0.15f;
        private const int PositionCommandDiagnosticMaxSamplesPerApi = 12;

        private static ManualLogSource? s_log;
        private static bool s_chargeGroundPatchReady;
        private static bool s_chargeMotionDiagnosticPatchReady;
        private static readonly Dictionary<int, ChargeHoldState> s_chargeHoldStates = new();
        private static readonly Dictionary<int, ChargeMotionDiagnosticTrace> s_chargeMotionDiagnosticTraces = new();
        private static readonly Dictionary<int, ChargeMotionDiagnosticTrace> s_chargeMotionDiagnosticPhysGrabTraces = new();
        private static readonly Dictionary<int, ChargeMotionDiagnosticTrace> s_chargeMotionDiagnosticRigidbodyTraces = new();
        private static readonly Dictionary<int, ChargeMotionDiagnosticTrace> s_chargeMotionDiagnosticTransformTraces = new();
        private static float s_lastLocalHoldInputStartTime;
        private static bool s_localHoldUiActive;
        private static bool s_localHoldInputPending;

        private sealed class ChargeHoldState
        {
            public float StartTime;
            public bool IsHolding;
            public float LaunchScale = 1f;
            public float ChargingStartTime;
            public float RestingTime;
        }

        private sealed class ChargeMotionDiagnosticTrace
        {
            public float LastSampleTime = float.NegativeInfinity;
            public int SampleCount;
            public bool SampleLimitLogged;
            public bool HasPreviousSamplePosition;
            public Vector3 PreviousHeadSamplePosition;
            public Vector3 PreviousTumbleSamplePosition;
            public ChargeHandler? ChargeHandler;
            public PhysGrabObject? PhysGrabObject;
            public int PhysGrabSampleCount;
            public float LastPhysGrabSampleTime = float.NegativeInfinity;
            public bool PhysGrabSampleLimitLogged;
            public int MovePositionLogCount;
            public float LastMovePositionLogTime = float.NegativeInfinity;
            public bool MovePositionLimitLogged;
            public int RigidbodyPositionLogCount;
            public float LastRigidbodyPositionLogTime = float.NegativeInfinity;
            public bool RigidbodyPositionLimitLogged;
            public int TransformPositionLogCount;
            public float LastTransformPositionLogTime = float.NegativeInfinity;
            public bool TransformPositionLimitLogged;
        }

        private struct ChargeMotionSnapshot
        {
            public bool Enabled;
            public int HandlerId;
            public int SampleNumber;
            public ChargeHandler.ChargeState State;
            public int HeadRigidbodyId;
            public Vector3 HeadPosition;
            public Vector3 HeadDeltaSincePreviousSample;
            public Vector3 HeadVelocity;
            public bool HeadIsKinematic;
            public Vector3 TargetStartOffset;
            public Vector3 CurrentStartOffset;
            public Vector3 StartVelocity;
            public Vector3 ChargeDirection;
            public float ChargeStrength;
            public float WindupTimer;
            public float ChargeTimer;
            public int BounceCount;
            public int EnemiesHit;
            public bool IsColliding;
            public bool IsGrabbed;
            public bool IsFrozen;
            public int TumbleRigidbodyId;
            public Vector3 TumblePosition;
            public Vector3 TumbleDeltaSincePreviousSample;
            public Vector3 TumbleVelocity;
            public bool TumbleIsKinematic;
        }

        private struct PhysGrabMotionSnapshot
        {
            public bool Enabled;
            public ChargeHandler? ChargeHandler;
            public int HandlerId;
            public int SampleNumber;
            public ChargeHandler.ChargeState ChargeState;
            public int PhysGrabObjectId;
            public int RigidbodyId;
            public Vector3 TransformPosition;
            public Vector3 RigidbodyPosition;
            public Vector3 LinearVelocity;
            public bool IsKinematic;
            public bool Frozen;
            public float FrozenTimer;
            public Vector3 FrozenPosition;
            public Vector3 FrozenVelocity;
            public bool Grabbed;
            public int PlayerGrabberCount;
        }

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            s_log = log;
            s_chargeGroundPatchReady = true;
            harmony.CreateClassProcessor(typeof(ChargeHandlerStartWindupPatch)).Patch();
            if (FeatureFlags.DebugLogging)
            {
                s_chargeMotionDiagnosticPatchReady = true;
                harmony.CreateClassProcessor(typeof(ChargeHandlerMotionDiagnosticPatch)).Patch();
                harmony.CreateClassProcessor(typeof(PhysGrabObjectMotionDiagnosticPatch)).Patch();
                harmony.CreateClassProcessor(typeof(ChargeHandlerStartChargeDiagnosticPatch)).Patch();
                harmony.CreateClassProcessor(typeof(ChargeHandlerEndChargeDiagnosticPatch)).Patch();
                TryInstallChargePositionCommandDiagnosticPatch(
                    harmony,
                    typeof(RigidbodyMovePositionDiagnosticPatch),
                    "Rigidbody.MovePosition");
                TryInstallChargePositionCommandDiagnosticPatch(
                    harmony,
                    typeof(RigidbodyPositionSetterDiagnosticPatch),
                    "Rigidbody.position setter");
                TryInstallChargePositionCommandDiagnosticPatch(
                    harmony,
                    typeof(TransformPositionSetterDiagnosticPatch),
                    "Transform.position setter");
            }
            PatchChargeHandlerDamageModeIfPossible(harmony, asm);
            PatchChargeAbilityHoldReleaseIfPossible(harmony, asm);
            PatchStunHandlerHoldScalingIfPossible(harmony, asm);
        }

        private static void TryInstallChargePositionCommandDiagnosticPatch(
            Harmony harmony,
            Type patchType,
            string targetDescription)
        {
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
            }
            catch (Exception ex)
            {
                s_log?.LogWarning(
                    $"[Fix:ChargeMotion] Could not install the {targetDescription} observer; " +
                    $"other charge diagnostics remain active. error={ex.GetType().Name}: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(ChargeHandler), nameof(ChargeHandler.StartWindup))]
        private static class ChargeHandlerStartWindupPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeGroundPatchReady;

            [HarmonyPrefix]
            private static void Prefix(ChargeHandler __instance, out bool __state)
            {
                __state = __instance != null && __instance.State == ChargeHandler.ChargeState.None;
            }

            [HarmonyPostfix]
            private static void Postfix(ChargeHandler __instance, bool __state)
            {
                if (!__state || __instance == null || __instance.State != ChargeHandler.ChargeState.Windup)
                    return;

                ApplyGroundOffsetGuard(__instance);
            }
        }

        [HarmonyPatch(typeof(ChargeHandler), nameof(ChargeHandler.FixedUpdate))]
        private static class ChargeHandlerMotionDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(ChargeHandler __instance, out ChargeMotionSnapshot __state)
            {
                __state = default;
                try
                {
                    __state = BeginChargeMotionDiagnosticSample(__instance);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("FixedUpdate prefix", ex);
                }
            }

            [HarmonyPostfix]
            private static void Postfix(ChargeHandler __instance, ChargeMotionSnapshot __state)
            {
                if (!__state.Enabled || __instance == null)
                    return;

                try
                {
                    var after = CaptureChargeMotionSnapshot(__instance, __state.HandlerId);
                    LogChargeMotionSample(__state, after);
                    CompleteChargeMotionDiagnosticSample(after);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("FixedUpdate postfix", ex);
                }
            }
        }

        [HarmonyPatch(typeof(PhysGrabObject), "FixedUpdate")]
        private static class PhysGrabObjectMotionDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(PhysGrabObject __instance, out PhysGrabMotionSnapshot __state)
            {
                __state = default;
                try
                {
                    __state = BeginPhysGrabMotionDiagnosticSample(__instance);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("PhysGrabObject.FixedUpdate prefix", ex);
                }
            }

            [HarmonyPostfix]
            private static void Postfix(PhysGrabObject __instance, PhysGrabMotionSnapshot __state)
            {
                if (!__state.Enabled || __instance == null)
                    return;

                try
                {
                    var after = CapturePhysGrabMotionSnapshot(
                        __instance,
                        __state.ChargeHandler,
                        __state.HandlerId,
                        __state.SampleNumber);
                    LogPhysGrabMotionSample(__state, after);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("PhysGrabObject.FixedUpdate postfix", ex);
                }
            }
        }

        [HarmonyPatch(typeof(Rigidbody), nameof(Rigidbody.MovePosition))]
        private static class RigidbodyMovePositionDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(Rigidbody __instance, [HarmonyArgument(0)] Vector3 requestedPosition)
            {
                try
                {
                    LogChargeRigidbodyPositionCommand(__instance, requestedPosition, "Rigidbody.MovePosition");
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("Rigidbody.MovePosition observer", ex);
                }
            }
        }

        [HarmonyPatch(typeof(Rigidbody), "set_position")]
        private static class RigidbodyPositionSetterDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(Rigidbody __instance, [HarmonyArgument(0)] Vector3 requestedPosition)
            {
                try
                {
                    LogChargeRigidbodyPositionCommand(__instance, requestedPosition, "Rigidbody.position");
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("Rigidbody.position observer", ex);
                }
            }
        }

        [HarmonyPatch(typeof(Transform), "set_position")]
        private static class TransformPositionSetterDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(Transform __instance, [HarmonyArgument(0)] Vector3 requestedPosition)
            {
                try
                {
                    LogChargeTransformPositionCommand(__instance, requestedPosition);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("Transform.position observer", ex);
                }
            }
        }

        [HarmonyPatch(typeof(ChargeHandler), nameof(ChargeHandler.StartCharge))]
        private static class ChargeHandlerStartChargeDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(ChargeHandler __instance, out ChargeMotionSnapshot __state)
            {
                __state = default;
                try
                {
                    __state = FeatureFlags.DebugLogging
                        ? CaptureChargeMotionSnapshot(__instance, GetUnityObjectInstanceId(__instance))
                        : default;
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("StartCharge prefix", ex);
                }
            }

            [HarmonyPostfix]
            private static void Postfix(ChargeHandler __instance, ChargeMotionSnapshot __state)
            {
                if (!__state.Enabled || __instance == null)
                    return;

                try
                {
                    var after = CaptureChargeMotionSnapshot(__instance, __state.HandlerId);
                    LogChargeMotionEvent("start-charge", __state, after);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("StartCharge postfix", ex);
                }
            }
        }

        [HarmonyPatch(typeof(ChargeHandler), nameof(ChargeHandler.EndCharge))]
        private static class ChargeHandlerEndChargeDiagnosticPatch
        {
            [HarmonyPrepare]
            private static bool Prepare() => s_chargeMotionDiagnosticPatchReady;

            [HarmonyPrefix]
            private static void Prefix(ChargeHandler __instance, out ChargeMotionSnapshot __state)
            {
                __state = default;
                try
                {
                    __state = FeatureFlags.DebugLogging
                        ? CaptureChargeMotionSnapshot(__instance, GetUnityObjectInstanceId(__instance))
                        : default;
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("EndCharge prefix", ex);
                }
            }

            [HarmonyPostfix]
            private static void Postfix(ChargeHandler __instance, ChargeMotionSnapshot __state)
            {
                if (!__state.Enabled || __instance == null)
                    return;

                try
                {
                    var after = CaptureChargeMotionSnapshot(__instance, __state.HandlerId);
                    LogChargeMotionEvent("end-charge", __state, after);
                    RemoveChargeMotionDiagnosticTrace(__instance);
                }
                catch (Exception ex)
                {
                    LogChargeMotionDiagnosticFailure("EndCharge postfix", ex);
                }
            }
        }

        private static void ApplyGroundOffsetGuard(ChargeHandler chargeHandler)
        {
            var vanillaOffset = chargeHandler.targetStartOffset;
            var groundPointAvailable = false;
            var groundDistance = 0f;
            bool? groundContact = null;

            try
            {
                var groundPoint = chargeHandler.controller?.collisionHandler?.GetGroundPointDown();
                groundPointAvailable = groundPoint.HasValue;
                if (groundPoint.HasValue)
                    groundDistance = Vector3.Distance(groundPoint.Value, chargeHandler.transform.position);

                groundContact = HasTightGroundContact(chargeHandler);
                if ((!groundPointAvailable || Mathf.Approximately(groundDistance, 0f)) && groundContact == false)
                    chargeHandler.targetStartOffset = Vector3.zero;
            }
            catch (Exception ex)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:ChargeGround.ProbeFailure", 600))
                {
                    s_log?.LogWarning(
                        $"[Fix:ChargeGround] Ground probe failed; preserving vanilla offset. " +
                        $"error={ex.GetType().Name}: {ex.Message}");
                }
            }

            if (FeatureFlags.DebugLogging)
            {
                s_log?.LogInfo(
                    $"[Fix:ChargeGround] start-windup groundPointAvailable={groundPointAvailable} " +
                    $"groundDistance={groundDistance:F3} groundContact={groundContact?.ToString() ?? "unknown"} " +
                    $"vanillaOffset={FormatVector(vanillaOffset)} " +
                    $"finalOffset={FormatVector(chargeHandler.targetStartOffset)}");
            }
        }

        private static bool? HasTightGroundContact(ChargeHandler chargeHandler)
        {
            var head = chargeHandler.controller?.deathHead;
            if (head == null)
                return null;

            var colliders = head.GetComponentsInChildren<Collider>(true);
            Collider? lowestCollider = null;
            var lowestY = float.PositiveInfinity;
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger)
                    continue;

                var bottomY = collider.bounds.min.y;
                if (bottomY >= lowestY)
                    continue;

                lowestCollider = collider;
                lowestY = bottomY;
            }

            if (lowestCollider == null)
                return null;

            var bounds = lowestCollider.bounds;
            var origin = new Vector3(
                bounds.center.x,
                bounds.min.y + GroundContactProbeStartOffset,
                bounds.center.z);
            var groundMask = LayerMask.GetMask("Default");
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                GroundContactProbeDistance,
                groundMask,
                QueryTriggerInteraction.Ignore);
            var headPhysGrabObject = head.GetComponent<PhysGrabObject>();

            foreach (var hit in hits)
            {
                var hitCollider = hit.collider;
                if (hitCollider == null || hit.normal.y < GroundContactMinimumNormalY)
                    continue;

                if (hitCollider.transform == head.transform || hitCollider.transform.IsChildOf(head.transform))
                    continue;

                if (headPhysGrabObject != null && hitCollider.GetComponentInParent<PhysGrabObject>() == headPhysGrabObject)
                    continue;

                return true;
            }

            return false;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F3},{value.y:F3},{value.z:F3})";
        }

        private static void BeginChargeMotionDiagnosticTrace(ChargeHandler chargeHandler)
        {
            var handlerId = GetUnityObjectInstanceId(chargeHandler);
            if (!FeatureFlags.DebugLogging || handlerId == 0)
                return;

            if (!s_chargeMotionDiagnosticTraces.TryGetValue(handlerId, out var trace))
            {
                trace = new ChargeMotionDiagnosticTrace();
                s_chargeMotionDiagnosticTraces[handlerId] = trace;
            }

            trace.ChargeHandler = chargeHandler;
            RegisterPhysGrabMotionDiagnosticTarget(trace, handlerId);
        }

        private static void RemoveChargeMotionDiagnosticTrace(ChargeHandler? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            var handlerId = GetUnityObjectInstanceId(chargeHandler);
            if (handlerId != 0)
            {
                if (s_chargeMotionDiagnosticTraces.TryGetValue(handlerId, out var trace) && trace.PhysGrabObject != null)
                {
                    var physGrabObjectId = GetUnityObjectInstanceId(trace.PhysGrabObject);
                    RemoveDiagnosticTraceMapping(s_chargeMotionDiagnosticPhysGrabTraces, physGrabObjectId, trace);
                    RemoveDiagnosticTraceMapping(
                        s_chargeMotionDiagnosticRigidbodyTraces,
                        GetUnityObjectInstanceId(trace.PhysGrabObject.rb),
                        trace);
                    RemoveDiagnosticTraceMapping(
                        s_chargeMotionDiagnosticTransformTraces,
                        GetUnityObjectInstanceId(trace.PhysGrabObject.transform),
                        trace);
                }

                s_chargeMotionDiagnosticTraces.Remove(handlerId);
            }
        }

        private static void RemoveDiagnosticTraceMapping(
            Dictionary<int, ChargeMotionDiagnosticTrace> mappings,
            int instanceId,
            ChargeMotionDiagnosticTrace trace)
        {
            if (instanceId != 0 && mappings.TryGetValue(instanceId, out var registeredTrace) &&
                ReferenceEquals(trace, registeredTrace))
            {
                mappings.Remove(instanceId);
            }
        }

        private static void RegisterPhysGrabMotionDiagnosticTarget(ChargeMotionDiagnosticTrace trace, int handlerId)
        {
            var chargeHandler = trace.ChargeHandler;
            if (chargeHandler == null || handlerId == 0)
                return;

            var physGrabObject = chargeHandler.impactDetector != null
                ? chargeHandler.impactDetector.physGrabObject
                : null;
            if (physGrabObject == null)
                return;

            trace.PhysGrabObject = physGrabObject;
            var physGrabObjectId = GetUnityObjectInstanceId(physGrabObject);
            if (physGrabObjectId != 0)
                s_chargeMotionDiagnosticPhysGrabTraces[physGrabObjectId] = trace;

            var rigidbodyId = GetUnityObjectInstanceId(physGrabObject.rb);
            if (rigidbodyId != 0)
                s_chargeMotionDiagnosticRigidbodyTraces[rigidbodyId] = trace;

            var transformId = GetUnityObjectInstanceId(physGrabObject.transform);
            if (transformId != 0)
                s_chargeMotionDiagnosticTransformTraces[transformId] = trace;
        }

        private static ChargeMotionSnapshot BeginChargeMotionDiagnosticSample(ChargeHandler? chargeHandler)
        {
            if (!FeatureFlags.DebugLogging || chargeHandler == null)
                return default;

            var handlerId = GetUnityObjectInstanceId(chargeHandler);
            if (handlerId == 0 || chargeHandler.State == ChargeHandler.ChargeState.None)
            {
                return default;
            }

            if (!s_chargeMotionDiagnosticTraces.TryGetValue(handlerId, out var trace))
            {
                trace = new ChargeMotionDiagnosticTrace();
                trace.ChargeHandler = chargeHandler;
                s_chargeMotionDiagnosticTraces[handlerId] = trace;
                RegisterPhysGrabMotionDiagnosticTarget(trace, handlerId);
            }

            var now = Time.time;
            if (trace.SampleCount >= ChargeMotionDiagnosticMaxSamples)
            {
                if (!trace.SampleLimitLogged)
                {
                    trace.SampleLimitLogged = true;
                    s_log?.LogDebug(
                        $"[Fix:ChargeMotion] sampling-limit reached handler={handlerId} " +
                        $"samples={trace.SampleCount} interval={ChargeMotionDiagnosticSampleInterval:F1}s");
                }
                return default;
            }

            if (now - trace.LastSampleTime < ChargeMotionDiagnosticSampleInterval)
                return default;

            trace.LastSampleTime = now;
            trace.SampleCount++;
            var snapshot = CaptureChargeMotionSnapshot(chargeHandler, handlerId);
            snapshot.SampleNumber = trace.SampleCount;
            if (trace.HasPreviousSamplePosition)
            {
                snapshot.HeadDeltaSincePreviousSample = snapshot.HeadPosition - trace.PreviousHeadSamplePosition;
                snapshot.TumbleDeltaSincePreviousSample = snapshot.TumblePosition - trace.PreviousTumbleSamplePosition;
            }
            return snapshot;
        }

        private static void CompleteChargeMotionDiagnosticSample(ChargeMotionSnapshot snapshot)
        {
            if (!snapshot.Enabled || !s_chargeMotionDiagnosticTraces.TryGetValue(snapshot.HandlerId, out var trace))
                return;

            trace.PreviousHeadSamplePosition = snapshot.HeadPosition;
            trace.PreviousTumbleSamplePosition = snapshot.TumblePosition;
            trace.HasPreviousSamplePosition = true;
        }

        private static PhysGrabMotionSnapshot BeginPhysGrabMotionDiagnosticSample(PhysGrabObject? physGrabObject)
        {
            if (!FeatureFlags.DebugLogging || physGrabObject == null)
                return default;

            var physGrabObjectId = GetUnityObjectInstanceId(physGrabObject);
            if (physGrabObjectId == 0 ||
                !s_chargeMotionDiagnosticPhysGrabTraces.TryGetValue(physGrabObjectId, out var trace) ||
                trace.ChargeHandler == null || trace.ChargeHandler.State == ChargeHandler.ChargeState.None)
            {
                return default;
            }

            var now = Time.time;
            if (trace.PhysGrabSampleCount >= ChargeMotionDiagnosticMaxSamples)
            {
                if (!trace.PhysGrabSampleLimitLogged)
                {
                    trace.PhysGrabSampleLimitLogged = true;
                    s_log?.LogDebug(
                        $"[Fix:ChargeMotion] PhysGrabObject sampling-limit reached handler={GetUnityObjectInstanceId(trace.ChargeHandler)} " +
                        $"samples={trace.PhysGrabSampleCount} interval={ChargeMotionDiagnosticSampleInterval:F1}s");
                }
                return default;
            }

            if (now - trace.LastPhysGrabSampleTime < ChargeMotionDiagnosticSampleInterval)
                return default;

            trace.LastPhysGrabSampleTime = now;
            trace.PhysGrabSampleCount++;
            return CapturePhysGrabMotionSnapshot(
                physGrabObject,
                trace.ChargeHandler,
                GetUnityObjectInstanceId(trace.ChargeHandler),
                trace.PhysGrabSampleCount);
        }

        private static PhysGrabMotionSnapshot CapturePhysGrabMotionSnapshot(
            PhysGrabObject? physGrabObject,
            ChargeHandler? chargeHandler,
            int handlerId,
            int sampleNumber)
        {
            if (!FeatureFlags.DebugLogging || physGrabObject == null || chargeHandler == null ||
                handlerId == 0 || physGrabObject.rb == null)
                return default;

            return new PhysGrabMotionSnapshot
            {
                Enabled = true,
                ChargeHandler = chargeHandler,
                HandlerId = handlerId,
                SampleNumber = sampleNumber,
                ChargeState = chargeHandler.State,
                PhysGrabObjectId = GetUnityObjectInstanceId(physGrabObject),
                RigidbodyId = GetUnityObjectInstanceId(physGrabObject.rb),
                TransformPosition = physGrabObject.transform.position,
                RigidbodyPosition = physGrabObject.rb.position,
                LinearVelocity = physGrabObject.rb.linearVelocity,
                IsKinematic = physGrabObject.rb.isKinematic,
                Frozen = physGrabObject.frozen,
                FrozenTimer = physGrabObject.frozenTimer,
                FrozenPosition = physGrabObject.frozenPosition,
                FrozenVelocity = physGrabObject.frozenVelocity,
                Grabbed = physGrabObject.grabbed,
                PlayerGrabberCount = physGrabObject.playerGrabbing != null ? physGrabObject.playerGrabbing.Count : 0
            };
        }

        private static void LogPhysGrabMotionSample(PhysGrabMotionSnapshot before, PhysGrabMotionSnapshot after)
        {
            if (!before.Enabled || !after.Enabled)
                return;

            s_log?.LogDebug(
                $"[Fix:ChargeMotion] physgrab-sample={before.SampleNumber} handler={before.HandlerId} " +
                $"state={before.ChargeState}->{after.ChargeState} physGrab={before.PhysGrabObjectId} rb={before.RigidbodyId} " +
                $"transformPos={FormatVector(before.TransformPosition)}->{FormatVector(after.TransformPosition)} " +
                $"rbPos={FormatVector(before.RigidbodyPosition)}->{FormatVector(after.RigidbodyPosition)} " +
                $"fixedTransformDelta={FormatVector(after.TransformPosition - before.TransformPosition)} " +
                $"fixedRbDelta={FormatVector(after.RigidbodyPosition - before.RigidbodyPosition)} " +
                $"velocity={FormatVector(before.LinearVelocity)}->{FormatVector(after.LinearVelocity)} " +
                $"kinematic={before.IsKinematic}->{after.IsKinematic} frozen={before.Frozen}->{after.Frozen} " +
                $"frozenTimer={before.FrozenTimer:F2}->{after.FrozenTimer:F2} " +
                $"frozenPosition={FormatVector(before.FrozenPosition)}->{FormatVector(after.FrozenPosition)} " +
                $"frozenVelocity={FormatVector(before.FrozenVelocity)}->{FormatVector(after.FrozenVelocity)} " +
                $"grabbed={before.Grabbed}->{after.Grabbed} playerGrabbers={before.PlayerGrabberCount}->{after.PlayerGrabberCount}");
        }

        private static void LogChargeRigidbodyPositionCommand(
            Rigidbody? rigidbody,
            Vector3 requestedPosition,
            string api)
        {
            if (!FeatureFlags.DebugLogging || rigidbody == null)
                return;

            var rigidbodyId = GetUnityObjectInstanceId(rigidbody);
            if (rigidbodyId == 0 ||
                !s_chargeMotionDiagnosticRigidbodyTraces.TryGetValue(rigidbodyId, out var trace) ||
                trace.ChargeHandler == null || trace.ChargeHandler.State == ChargeHandler.ChargeState.None)
            {
                return;
            }

            var currentPosition = rigidbody.position;
            if ((requestedPosition - currentPosition).sqrMagnitude < 0.0001f ||
                !TryAcquirePositionCommandLog(trace, api))
            {
                return;
            }

            LogChargePositionCommand(trace, api, rigidbodyId, currentPosition, requestedPosition, rigidbody);
        }

        private static void LogChargeTransformPositionCommand(Transform? transform, Vector3 requestedPosition)
        {
            if (!FeatureFlags.DebugLogging || transform == null)
                return;

            var transformId = GetUnityObjectInstanceId(transform);
            if (transformId == 0 ||
                !s_chargeMotionDiagnosticTransformTraces.TryGetValue(transformId, out var trace) ||
                trace.ChargeHandler == null || trace.ChargeHandler.State == ChargeHandler.ChargeState.None)
            {
                return;
            }

            var currentPosition = transform.position;
            if ((requestedPosition - currentPosition).sqrMagnitude < 0.0001f ||
                !TryAcquirePositionCommandLog(trace, "Transform.position"))
            {
                return;
            }

            var physGrabObject = trace.PhysGrabObject;
            var rigidbody = physGrabObject != null ? physGrabObject.rb : null;
            LogChargePositionCommand(
                trace,
                "Transform.position",
                rigidbody != null ? GetUnityObjectInstanceId(rigidbody) : 0,
                currentPosition,
                requestedPosition,
                rigidbody);
        }

        private static bool TryAcquirePositionCommandLog(ChargeMotionDiagnosticTrace trace, string api)
        {
            var now = Time.time;
            if (api == "Rigidbody.MovePosition")
            {
                if (trace.MovePositionLogCount >= PositionCommandDiagnosticMaxSamplesPerApi)
                {
                    if (!trace.MovePositionLimitLogged)
                    {
                        trace.MovePositionLimitLogged = true;
                        s_log?.LogDebug(
                            $"[Fix:ChargeMotion] position-command sampling-limit reached api={api} " +
                            $"handler={GetUnityObjectInstanceId(trace.ChargeHandler)}");
                    }
                    return false;
                }

                if (now - trace.LastMovePositionLogTime < PositionCommandDiagnosticInterval)
                    return false;

                trace.LastMovePositionLogTime = now;
                trace.MovePositionLogCount++;
                return true;
            }

            if (api == "Rigidbody.position")
            {
                if (trace.RigidbodyPositionLogCount >= PositionCommandDiagnosticMaxSamplesPerApi)
                {
                    if (!trace.RigidbodyPositionLimitLogged)
                    {
                        trace.RigidbodyPositionLimitLogged = true;
                        s_log?.LogDebug(
                            $"[Fix:ChargeMotion] position-command sampling-limit reached api={api} " +
                            $"handler={GetUnityObjectInstanceId(trace.ChargeHandler)}");
                    }
                    return false;
                }

                if (now - trace.LastRigidbodyPositionLogTime < PositionCommandDiagnosticInterval)
                    return false;

                trace.LastRigidbodyPositionLogTime = now;
                trace.RigidbodyPositionLogCount++;
                return true;
            }

            if (trace.TransformPositionLogCount >= PositionCommandDiagnosticMaxSamplesPerApi)
            {
                if (!trace.TransformPositionLimitLogged)
                {
                    trace.TransformPositionLimitLogged = true;
                    s_log?.LogDebug(
                        $"[Fix:ChargeMotion] position-command sampling-limit reached api={api} " +
                        $"handler={GetUnityObjectInstanceId(trace.ChargeHandler)}");
                }
                return false;
            }

            if (now - trace.LastTransformPositionLogTime < PositionCommandDiagnosticInterval)
                return false;

            trace.LastTransformPositionLogTime = now;
            trace.TransformPositionLogCount++;
            return true;
        }

        private static void LogChargePositionCommand(
            ChargeMotionDiagnosticTrace trace,
            string api,
            int rigidbodyId,
            Vector3 currentPosition,
            Vector3 requestedPosition,
            Rigidbody? rigidbody)
        {
            var chargeHandler = trace.ChargeHandler;
            if (chargeHandler == null)
                return;

            var physGrabObject = trace.PhysGrabObject;
            s_log?.LogDebug(
                $"[Fix:ChargeMotion] position-command api={api} handler={GetUnityObjectInstanceId(chargeHandler)} " +
                $"state={chargeHandler.State} physGrab={(physGrabObject != null ? GetUnityObjectInstanceId(physGrabObject) : 0)} " +
                $"rb={rigidbodyId} requested={FormatVector(requestedPosition)} " +
                $"current={FormatVector(currentPosition)} delta={FormatVector(requestedPosition - currentPosition)} " +
                $"velocity={FormatVector(rigidbody != null ? rigidbody.linearVelocity : Vector3.zero)} " +
                $"kinematic={rigidbody != null && rigidbody.isKinematic} " +
                $"frozen={physGrabObject != null && physGrabObject.frozen} " +
                $"grabbed={physGrabObject != null && physGrabObject.grabbed}");
        }

        private static ChargeMotionSnapshot CaptureChargeMotionSnapshot(ChargeHandler? chargeHandler, int handlerId)
        {
            if (!FeatureFlags.DebugLogging || chargeHandler == null || handlerId == 0)
                return default;

            var headRigidbody = chargeHandler.rb;
            var controller = chargeHandler.controller;
            var tumbleRigidbody = controller?.deathHead?.playerAvatar?.tumble?.rb;
            var physGrabObject = chargeHandler.impactDetector != null
                ? chargeHandler.impactDetector.physGrabObject
                : null;

            return new ChargeMotionSnapshot
            {
                Enabled = true,
                HandlerId = handlerId,
                State = chargeHandler.State,
                HeadRigidbodyId = GetUnityObjectInstanceId(headRigidbody),
                HeadPosition = headRigidbody != null ? headRigidbody.position : chargeHandler.transform.position,
                HeadVelocity = headRigidbody != null ? headRigidbody.linearVelocity : Vector3.zero,
                HeadIsKinematic = headRigidbody != null && headRigidbody.isKinematic,
                TargetStartOffset = chargeHandler.targetStartOffset,
                CurrentStartOffset = chargeHandler.currentStartOffset,
                StartVelocity = chargeHandler.startVelocity,
                ChargeDirection = chargeHandler.chargeDirection,
                ChargeStrength = chargeHandler.chargeStrength,
                WindupTimer = chargeHandler.windupTimer,
                ChargeTimer = chargeHandler.chargeTimer,
                BounceCount = chargeHandler.bounceColliders != null ? chargeHandler.bounceColliders.Count : 0,
                EnemiesHit = chargeHandler.enemiesHit,
                IsColliding = controller?.collisionHandler?.isColliding ?? false,
                IsGrabbed = physGrabObject != null && physGrabObject.grabbed,
                IsFrozen = physGrabObject != null && physGrabObject.frozen,
                TumbleRigidbodyId = tumbleRigidbody != null ? GetUnityObjectInstanceId(tumbleRigidbody) : 0,
                TumblePosition = tumbleRigidbody != null ? tumbleRigidbody.position : Vector3.zero,
                TumbleVelocity = tumbleRigidbody != null ? tumbleRigidbody.linearVelocity : Vector3.zero,
                TumbleIsKinematic = tumbleRigidbody != null && tumbleRigidbody.isKinematic
            };
        }

        private static void LogChargeMotionSample(ChargeMotionSnapshot before, ChargeMotionSnapshot after)
        {
            if (!before.Enabled || !after.Enabled)
                return;

            var isHolding = s_chargeHoldStates.TryGetValue(before.HandlerId, out var holdState) && holdState.IsHolding;
            var message =
                $"[Fix:ChargeMotion] sample={before.SampleNumber} handler={before.HandlerId} " +
                $"held={isHolding} state={before.State}->{after.State} " +
                $"headRB={before.HeadRigidbodyId} pos={FormatVector(before.HeadPosition)}->{FormatVector(after.HeadPosition)} " +
                $"fixedDelta={FormatVector(after.HeadPosition - before.HeadPosition)} " +
                $"sinceSampleDelta={FormatVector(before.HeadDeltaSincePreviousSample)} " +
                $"vel={FormatVector(before.HeadVelocity)}->{FormatVector(after.HeadVelocity)} " +
                $"kinematic={before.HeadIsKinematic}->{after.HeadIsKinematic} " +
                $"targetOffset={FormatVector(before.TargetStartOffset)}->{FormatVector(after.TargetStartOffset)} " +
                $"currentOffset={FormatVector(before.CurrentStartOffset)}->{FormatVector(after.CurrentStartOffset)} " +
                $"startVelocity={FormatVector(before.StartVelocity)}->{FormatVector(after.StartVelocity)} " +
                $"chargeDirection={FormatVector(before.ChargeDirection)}->{FormatVector(after.ChargeDirection)} " +
                $"strength={before.ChargeStrength:F2}->{after.ChargeStrength:F2} " +
                $"windupTimer={before.WindupTimer:F2}->{after.WindupTimer:F2} " +
                $"chargeTimer={before.ChargeTimer:F2}->{after.ChargeTimer:F2} " +
                $"bounces={before.BounceCount}->{after.BounceCount} enemiesHit={before.EnemiesHit}->{after.EnemiesHit} " +
                $"colliding={before.IsColliding}->{after.IsColliding} grabbed={before.IsGrabbed}->{after.IsGrabbed} " +
                $"frozen={before.IsFrozen}->{after.IsFrozen} " +
                $"tumbleRB={before.TumbleRigidbodyId}->{after.TumbleRigidbodyId} " +
                $"tumblePos={FormatVector(before.TumblePosition)}->{FormatVector(after.TumblePosition)} " +
                $"tumbleSinceSampleDelta={FormatVector(before.TumbleDeltaSincePreviousSample)} " +
                $"tumbleVel={FormatVector(before.TumbleVelocity)}->{FormatVector(after.TumbleVelocity)} " +
                $"tumbleKinematic={before.TumbleIsKinematic}->{after.TumbleIsKinematic}";
            s_log?.LogDebug(message);
        }

        private static void LogChargeMotionEvent(string eventName, ChargeHandler? chargeHandler)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            try
            {
                var handlerId = chargeHandler != null ? GetUnityObjectInstanceId(chargeHandler) : 0;
                var snapshot = CaptureChargeMotionSnapshot(chargeHandler, handlerId);
                if (!snapshot.Enabled)
                {
                    s_log?.LogDebug($"[Fix:ChargeMotion] event={eventName} handler=unavailable");
                    return;
                }

                var isHolding = s_chargeHoldStates.TryGetValue(handlerId, out var holdState) && holdState.IsHolding;
                LogChargeMotionEvent(eventName, snapshot, snapshot, isHolding);
            }
            catch (Exception ex)
            {
                LogChargeMotionDiagnosticFailure($"event {eventName}", ex);
            }
        }

        private static void LogChargeMotionEvent(
            string eventName,
            ChargeMotionSnapshot before,
            ChargeMotionSnapshot after)
        {
            var isHolding = s_chargeHoldStates.TryGetValue(before.HandlerId, out var holdState) && holdState.IsHolding;
            LogChargeMotionEvent(eventName, before, after, isHolding);
        }

        private static void LogChargeMotionEvent(
            string eventName,
            ChargeMotionSnapshot before,
            ChargeMotionSnapshot after,
            bool isHolding)
        {
            if (!FeatureFlags.DebugLogging || !before.Enabled || !after.Enabled)
                return;

            s_log?.LogDebug(
                $"[Fix:ChargeMotion] event={eventName} handler={before.HandlerId} held={isHolding} " +
                $"state={before.State}->{after.State} headRB={before.HeadRigidbodyId} " +
                $"pos={FormatVector(before.HeadPosition)}->{FormatVector(after.HeadPosition)} " +
                $"vel={FormatVector(before.HeadVelocity)}->{FormatVector(after.HeadVelocity)} " +
                $"kinematic={before.HeadIsKinematic}->{after.HeadIsKinematic} " +
                $"targetOffset={FormatVector(before.TargetStartOffset)}->{FormatVector(after.TargetStartOffset)} " +
                $"currentOffset={FormatVector(before.CurrentStartOffset)}->{FormatVector(after.CurrentStartOffset)} " +
                $"startVelocity={FormatVector(before.StartVelocity)}->{FormatVector(after.StartVelocity)} " +
                $"chargeDirection={FormatVector(before.ChargeDirection)}->{FormatVector(after.ChargeDirection)} " +
                $"strength={before.ChargeStrength:F2}->{after.ChargeStrength:F2} " +
                $"windupTimer={before.WindupTimer:F2}->{after.WindupTimer:F2} " +
                $"chargeTimer={before.ChargeTimer:F2}->{after.ChargeTimer:F2} " +
                $"bounces={before.BounceCount}->{after.BounceCount} enemiesHit={before.EnemiesHit}->{after.EnemiesHit} " +
                $"colliding={before.IsColliding}->{after.IsColliding} grabbed={before.IsGrabbed}->{after.IsGrabbed} " +
                $"frozen={before.IsFrozen}->{after.IsFrozen} tumbleRB={before.TumbleRigidbodyId}->{after.TumbleRigidbodyId} " +
                $"tumblePos={FormatVector(before.TumblePosition)}->{FormatVector(after.TumblePosition)} " +
                $"tumbleVel={FormatVector(before.TumbleVelocity)}->{FormatVector(after.TumbleVelocity)} " +
                $"tumbleKinematic={before.TumbleIsKinematic}->{after.TumbleIsKinematic}");
        }

        private static void LogChargeMotionDiagnosticFailure(string phase, Exception exception)
        {
            if (!FeatureFlags.DebugLogging || !LogLimiter.ShouldLog("Fix:ChargeMotion.DiagnosticFailure", 120))
                return;

            s_log?.LogWarning(
                $"[Fix:ChargeMotion] Diagnostic failed during {phase}; gameplay continues. " +
                $"error={exception.GetType().Name}: {exception.Message}");
        }

        private static void PatchChargeHandlerDamageModeIfPossible(Harmony harmony, Assembly asm)
        {
            var mWindup = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.ChargeWindup), new[] { typeof(Vector3) });
            var windupPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ChargeWindup_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPrefix != null)
                harmony.Patch(mWindup, prefix: new HarmonyMethod(windupPrefix));
            var windupPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ChargeWindup_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPostfix != null)
                harmony.Patch(mWindup, postfix: new HarmonyMethod(windupPostfix));

            var mReset = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.ResetState), Type.EmptyTypes);
            var mFixedUpdate = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.FixedUpdate), Type.EmptyTypes);
            var mCancelCharge = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.CancelCharge), Type.EmptyTypes);
            var mEnemyHit = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.EnemyHit));
            var mUpdateWindupDirection = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.UpdateWindupDirection), new[] { typeof(Vector3) });
            var mSyncChargeState = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.SyncChargeStateRPC));
            var syncChargeStatePrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_SyncChargeStateRPC_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var resetPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ResetState_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mReset != null && resetPostfix != null)
                harmony.Patch(mReset, postfix: new HarmonyMethod(resetPostfix));
            var mEndCharge = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.EndCharge), Type.EmptyTypes);
            var endChargePostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_EndCharge_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mEndCharge != null && endChargePostfix != null)
                harmony.Patch(mEndCharge, postfix: new HarmonyMethod(endChargePostfix));
            var fixedUpdatePostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_FixedUpdate_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mFixedUpdate != null && fixedUpdatePostfix != null)
                harmony.Patch(mFixedUpdate, postfix: new HarmonyMethod(fixedUpdatePostfix));
            var cancelPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_CancelCharge_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mCancelCharge != null && cancelPostfix != null)
                harmony.Patch(mCancelCharge, postfix: new HarmonyMethod(cancelPostfix));
            var enemyHitPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_EnemyHit_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mEnemyHit != null && enemyHitPrefix != null)
                harmony.Patch(mEnemyHit, prefix: new HarmonyMethod(enemyHitPrefix));
            var updateWindupDirectionPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_UpdateWindupDirection_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mUpdateWindupDirection != null && updateWindupDirectionPrefix != null)
                harmony.Patch(mUpdateWindupDirection, prefix: new HarmonyMethod(updateWindupDirectionPrefix));
            var syncChargeStatePostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_SyncChargeStateRPC_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mSyncChargeState != null)
            {
                if (syncChargeStatePrefix != null)
                    harmony.Patch(mSyncChargeState, prefix: new HarmonyMethod(syncChargeStatePrefix));
                if (syncChargeStatePostfix != null)
                    harmony.Patch(mSyncChargeState, postfix: new HarmonyMethod(syncChargeStatePostfix));
            }
        }

        private static bool ChargeHandler_ChargeWindup_Prefix(ChargeHandler __instance)
        {
            if (__instance == null)
                return true;

            if (IsChargeHandlerHeadGrabbed(__instance))
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:Charge.Grabbed", 30))
                {
                    Debug.Log("[Fix:Charge] Charge windup blocked because the head is grabbed.");
                }
                return false;
            }

            return true;
        }

        private static void ChargeHandler_ChargeWindup_Postfix(ChargeHandler __instance)
        {
            if (__instance == null)
                return;

            if (!IsChargeState(__instance, "Windup"))
                return;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0)
                return;

            var state = GetOrCreateChargeHoldState(id);
            state.StartTime = Time.time;
            state.IsHolding = true;
            state.LaunchScale = 1f;
            BeginChargeMotionDiagnosticTrace(__instance);
            LogChargeMotionEvent("windup-start", __instance);
            s_localHoldUiActive = true;
            AbilityModule.SetChargeSlotActivationProgress(0f);
        }

        private static void ChargeHandler_FixedUpdate_Postfix(ChargeHandler __instance)
        {
            if (__instance == null)
                return;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0)
                return;

            if (SemiFunc.IsMasterClientOrSingleplayer() && IsChargeState(__instance, "Charging"))
            {
                if (!TryStopStalledCharge(__instance, id))
                    TryStopAtBounceLimit(__instance);

                return;
            }

            if (!IsLocalChargeHandler(__instance))
            {
                if (!s_chargeHoldStates.TryGetValue(id, out var remoteState))
                    return;

                if (!IsChargeState(__instance, "Windup"))
                {
                    s_chargeHoldStates.Remove(id);
                    return;
                }

                // Keep authoritative windup open until explicit release/cancel command arrives.
                if (remoteState.IsHolding)
                {
                    __instance.windupTimer = Mathf.Max(0.01f, __instance.windupTime);
                }
                return;
            }

            if (!s_chargeHoldStates.TryGetValue(id, out var state))
            {
                // Non-host clients do not execute ChargeWindup locally (RPC goes to master).
                // Bootstrap local hold preview when authoritative state sync reaches Windup.
                if (IsChargeState(__instance, "Windup") && s_localHoldInputPending)
                {
                    state = GetOrCreateChargeHoldState(id);
                    state.StartTime = s_lastLocalHoldInputStartTime > 0f ? s_lastLocalHoldInputStartTime : Time.time;
                    state.IsHolding = true;
                    state.LaunchScale = 1f;
                    s_localHoldUiActive = true;
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                }
                else
                {
                    if (!s_localHoldUiActive)
                        AbilityModule.SetChargeSlotActivationProgress(0f);
                    return;
                }
            }

            if (!IsChargeState(__instance, "Windup"))
            {
                s_chargeHoldStates.Remove(id);
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                if (!s_localHoldUiActive)
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                return;
            }

            if (s_localHoldUiActive && state.IsHolding)
            {
                var holdSecondsRemote = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
                var progressRemote = Mathf.Clamp01((Time.time - state.StartTime) / holdSecondsRemote);
                var requiredScaleRemote = GetMinimumChargeReleaseScale(__instance);
                AbilityModule.SetChargeSlotActivationProgress(progressRemote, requiredScaleRemote);
            }
            else if (!state.IsHolding)
            {
                if (!s_localHoldUiActive)
                {
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                }

                if (!IsChargeState(__instance, "Windup"))
                {
                    s_chargeHoldStates.Remove(id);
                }
                return;
            }
            else
            {
                if (!IsChargeState(__instance, "Windup"))
                {
                    s_chargeHoldStates.Remove(id);
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                    return;
                }

                var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
                var progress = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
                var requiredScale = GetMinimumChargeReleaseScale(__instance);
                AbilityModule.SetChargeSlotActivationProgress(progress, requiredScale);
            }

            if (!state.IsHolding)
                return;

            __instance.windupTimer = Mathf.Max(0.01f, __instance.windupTime);
        }

        private static bool TryStopStalledCharge(ChargeHandler chargeHandler, int id)
        {
            var state = GetOrCreateChargeHoldState(id);
            if (state.ChargingStartTime <= 0f)
                state.ChargingStartTime = Time.time;

            var controller = chargeHandler.controller;
            var rigidbody = chargeHandler.rb;
            var isResting = controller?.collisionHandler?.isColliding == true &&
                            rigidbody != null &&
                            rigidbody.linearVelocity.sqrMagnitude <= ChargeRestVelocitySqrThreshold &&
                            rigidbody.angularVelocity.sqrMagnitude <= ChargeRestAngularVelocitySqrThreshold;

            state.RestingTime = isResting
                ? state.RestingTime + Time.fixedDeltaTime
                : 0f;

            var reachedRestStop = state.RestingTime >= ChargeRestStopSeconds;
            var reachedFailsafe = Time.time - state.ChargingStartTime >= ChargeFailsafeSeconds;
            if (!reachedRestStop && !reachedFailsafe)
                return false;

            chargeHandler.EndCharge();

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHCharge.StalledStop", 30))
            {
                var reason = reachedRestStop ? "stable-rest" : "failsafe-timeout";
                s_log?.LogDebug($"[Fix:DHHCharge] Ended authoritative Charging state ({reason}).");
            }

            return true;
        }

        private static void TryStopAtBounceLimit(ChargeHandler chargeHandler)
        {
            var maxBounces = Mathf.Max(0, Mathf.CeilToInt(chargeHandler.maxBounces));
            if (maxBounces > 0 && chargeHandler.bounceColliders.Count >= maxBounces)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHCharge.BounceLimit", 5))
                {
                    s_log?.LogDebug(
                        $"[Fix:DHHCharge] Ending charge at bounce limit: " +
                        $"bounces={chargeHandler.bounceColliders.Count} maxBounces={maxBounces} " +
                        $"chargeTimer={chargeHandler.chargeTimer:F2}.");
                }
                chargeHandler.EndCharge();
            }
        }

        private static void PatchChargeAbilityHoldReleaseIfPossible(Harmony harmony, Assembly asm)
        {
            var mOnAbilityDown = AccessTools.Method(typeof(ChargeAbility), nameof(ChargeAbility.OnAbilityDown), Type.EmptyTypes);
            var mOnAbilityUp = AccessTools.Method(typeof(ChargeAbility), nameof(ChargeAbility.OnAbilityUp), Type.EmptyTypes);
            var mOnAbilityCancel = AccessTools.Method(typeof(ChargeAbility), nameof(ChargeAbility.OnAbilityCancel), Type.EmptyTypes);
            if (mOnAbilityUp == null)
                return;

            var onAbilityUpPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityUp_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (onAbilityUpPrefix == null)
                return;

            var onAbilityDownPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityDown_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var onAbilityCancelPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityCancel_Postfix), BindingFlags.Static | BindingFlags.NonPublic);

            if (mOnAbilityDown != null && onAbilityDownPostfix != null)
                harmony.Patch(mOnAbilityDown, postfix: new HarmonyMethod(onAbilityDownPostfix));
            harmony.Patch(mOnAbilityUp, prefix: new HarmonyMethod(onAbilityUpPrefix));
            if (mOnAbilityCancel != null && onAbilityCancelPostfix != null)
                harmony.Patch(mOnAbilityCancel, postfix: new HarmonyMethod(onAbilityCancelPostfix));
        }

        private static void PatchStunHandlerHoldScalingIfPossible(Harmony harmony, Assembly asm)
        {
            var stunDurationGetter = AccessTools.PropertyGetter(typeof(StunHandler), nameof(StunHandler.StunDuration));
            if (stunDurationGetter == null)
                return;

            var prefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(StunHandler_StunDuration_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(stunDurationGetter, prefix: new HarmonyMethod(prefix));
        }

        private static void ChargeHandler_CancelCharge_Postfix(ChargeHandler __instance)
        {
            LogChargeMotionEvent("cancel-charge", __instance);
            RemoveChargeMotionDiagnosticTrace(__instance);
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            SynchronizeFinalChargeState(__instance);
        }

        private static void ChargeHandler_SyncChargeStateRPC_Prefix(ChargeHandler __instance, ChargeHandler.ChargeState state)
        {
            if (__instance == null)
                return;

            if (state == ChargeHandler.ChargeState.Windup || state == ChargeHandler.ChargeState.Charging)
                return;

            LogChargeMotionEvent("sync-state-stop", __instance);
            RemoveChargeMotionDiagnosticTrace(__instance);
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
        }

        private static void ChargeHandler_SyncChargeStateRPC_Postfix(ChargeHandler __instance)
        {
            if (__instance == null)
                return;

            if (IsChargeState(__instance, "Windup") || IsChargeState(__instance, "Charging"))
                return;

            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
        }

        private static bool ChargeHandler_EnemyHit_Prefix(ChargeHandler __instance)
        {
            if (__instance == null)
                return true;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var holdState))
                return true;

            __instance.enemiesHit++;

            var abilityLevel = __instance.AbilityLevel;
            var vanillaMax = Mathf.FloorToInt(EvaluateStatWithDiminishingReturns(1f, 0.5f, abilityLevel, 20, 0.9f).FinalValue);
            var scaledMax = Mathf.Max(1, Mathf.RoundToInt(vanillaMax * Mathf.Clamp01(holdState.LaunchScale)));
            if (__instance.enemiesHit >= scaledMax)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHCharge.EnemyHitLimit", 5))
                {
                    s_log?.LogDebug(
                        $"[Fix:DHHCharge] Ending charge at enemy-hit limit: " +
                        $"enemiesHit={__instance.enemiesHit} maxEnemies={scaledMax} " +
                        $"bounces={__instance.bounceColliders.Count} chargeTimer={__instance.chargeTimer:F2}.");
                }
                __instance.EndCharge();
            }

            return false;
        }

        private static bool StunHandler_StunDuration_Prefix(StunHandler __instance, ref float __result)
        {
            if (__instance == null)
                return true;

            var chargeHandler = __instance.chargeHandler;
            if (chargeHandler == null)
                return true;

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var holdState))
                return true;

            var abilityLevel = chargeHandler.AbilityLevel;
            var vanillaStun = 5f + (1f * abilityLevel);
            __result = vanillaStun * Mathf.Clamp01(holdState.LaunchScale);
            return false;
        }

        private static bool ChargeAbility_OnAbilityUp_Prefix()
        {
            return TryReleaseHeldCharge();
        }

        private static void ChargeAbility_OnAbilityDown_Postfix()
        {
            s_lastLocalHoldInputStartTime = Time.time;
            s_localHoldInputPending = true;
            AbilityModule.SetChargeSlotActivationProgress(0f);

            var chargeHandler = GetLocalChargeHandler();
            LogChargeMotionEvent("input-down", chargeHandler);
            if (chargeHandler != null && !IsChargeState(chargeHandler, "Windup"))
            {
                ClearChargeHoldState(chargeHandler);
            }
        }

        private static void ChargeAbility_OnAbilityCancel_Postfix()
        {
            s_localHoldInputPending = false;
            s_localHoldUiActive = false;
            AbilityModule.SetChargeSlotActivationProgress(0f);

            var chargeHandler = GetLocalChargeHandler();
            LogChargeMotionEvent("input-cancel", chargeHandler);
            if (chargeHandler == null)
                return;

            ClearChargeHoldState(chargeHandler);
        }

        private static bool TryReleaseHeldCharge()
        {
            var chargeHandler = GetLocalChargeHandler();
            LogChargeMotionEvent("input-release", chargeHandler);
            if (chargeHandler == null)
            {
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                return true;
            }

            if (!IsChargeState(chargeHandler, "Windup"))
            {
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                return true;
            }

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id == 0)
            {
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                return true;
            }
            if (!s_chargeHoldStates.TryGetValue(id, out var state))
            {
                state = GetOrCreateChargeHoldState(id);
                state.StartTime = s_lastLocalHoldInputStartTime > 0f ? s_lastLocalHoldInputStartTime : Time.time;
                state.IsHolding = true;
                state.LaunchScale = 1f;
            }
            if (!state.IsHolding)
                return true;

            var isRemoteClient = SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClientOrSingleplayer();

            if (isRemoteClient)
            {
                if (!ConfigSyncManager.IsRemoteHostFixCompatible())
                {
                    state.IsHolding = false;
                    state.LaunchScale = 0f;
                    s_localHoldInputPending = false;
                    s_localHoldUiActive = false;
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                    StopChargeWindupLoop(chargeHandler);
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(ChargePermissiveFallbackLogKey, 120))
                    {
                        Debug.Log("[Fix:DHHCharge][PermissiveGate] Host fix marker missing. Sending authoritative cancel fallback.");
                    }

                    TrySendVanillaRemoteCancelCommand(chargeHandler);
                    return false;
                }

                state.IsHolding = false;
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                // Remote client sends only "release input"; host computes scale/threshold authoritatively.
                if (!TrySendRemoteReleaseCommand(chargeHandler))
                {
                    StopChargeWindupLoop(chargeHandler);
                    TrySendVanillaRemoteCancelCommand(chargeHandler);
                }
                return false;
            }

            var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
            var scale = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
            var requiredScale = GetMinimumChargeReleaseScale(chargeHandler);
            if (scale < requiredScale)
            {
                state.IsHolding = false;
                state.LaunchScale = 0f;
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                StopChargeWindupLoop(chargeHandler);
                chargeHandler.CancelCharge();
                return false;
            }

            state.IsHolding = false;
            state.LaunchScale = scale;

            chargeHandler.chargeStrength *= scale;

            chargeHandler.maxBounces = Mathf.Max(0f, chargeHandler.maxBounces * scale);

            chargeHandler.windupTimer = -1f;

            s_localHoldInputPending = false;
            s_localHoldUiActive = false;
            AbilityModule.SetChargeSlotActivationProgress(0f);
            return true;
        }

        private static bool ChargeHandler_UpdateWindupDirection_Prefix(ChargeHandler __instance, Vector3 chargeDirection)
        {
            if (__instance == null)
                return true;

            if (!SemiFunc.IsMasterClientOrSingleplayer())
                return true;

            if (Mathf.Abs(chargeDirection.x - RemoteCancelCommandTag) < 0.001f)
            {
                __instance.CancelCharge();
                return false;
            }

            if (Mathf.Abs(chargeDirection.x - RemoteReleaseCommandTag) > 0.001f)
                return true;

            LogChargeMotionEvent("remote-release-received", __instance);
            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var state))
                return false;

            if (!IsChargeState(__instance, "Windup"))
                return false;

            var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
            var scale = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
            var requiredScale = GetMinimumChargeReleaseScale(__instance);
            state.IsHolding = false;
            if (scale < requiredScale)
            {
                state.LaunchScale = 0f;
                __instance.CancelCharge();
                return false;
            }

            state.LaunchScale = scale;

            __instance.chargeStrength *= scale;

            __instance.maxBounces = Mathf.Max(0f, __instance.maxBounces * scale);

            __instance.windupTimer = -1f;
            return false;
        }

        private static bool TrySendRemoteReleaseCommand(ChargeHandler chargeHandler)
        {
            var pv = GetChargePhotonView(chargeHandler);
            if (pv == null || pv.ViewID <= 0)
                return false;
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                return false;

            pv.RPC("UpdateWindupDirection", RpcTarget.MasterClient, new object[] { new Vector3(RemoteReleaseCommandTag, 0f, 0f) });
            return true;
        }

        private static bool TrySendVanillaRemoteCancelCommand(ChargeHandler chargeHandler)
        {
            var pv = GetChargePhotonView(chargeHandler);
            if (pv == null || pv.ViewID <= 0)
                return false;
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                return false;

            pv.RPC("CancelCharge", RpcTarget.MasterClient, Array.Empty<object>());
            return true;
        }

        private static PhotonView? GetChargePhotonView(ChargeHandler chargeHandler)
        {
            if (chargeHandler is Component component)
            {
                var componentPhotonView = component.GetComponent<PhotonView>();
                if (componentPhotonView != null && componentPhotonView.ViewID > 0)
                    return componentPhotonView;
            }

            return GetDhhInputManagerHeadPhotonView();
        }

        private static PhotonView? GetDhhInputManagerHeadPhotonView()
        {
            return DHHInputManager.instance != null ? DHHInputManager.instance.headPhotonView : null;
        }

        private static float GetMinimumChargeReleaseScale(ChargeHandler chargeHandler)
        {
            if (chargeHandler == null)
                return 0f;

            var required = 0f;
            TryGetEffectiveChargeAbilityLevel(chargeHandler, out var abilityLevel);

            var chargeStrength = GetEffectiveChargeStrengthForThreshold(chargeHandler, abilityLevel);
            if (chargeStrength > 0f)
            {
                required = Mathf.Max(required, RequiredScaleForMinimumOne(chargeStrength));
            }

            var maxBounces = GetEffectiveMaxBouncesForThreshold(chargeHandler, abilityLevel);
            if (maxBounces > 0f)
            {
                required = Mathf.Max(required, RequiredScaleForMinimumOne(maxBounces));
            }

            if (abilityLevel > 0)
            {
                var stunBase = 5f + (1f * abilityLevel);
                required = Mathf.Max(required, RequiredScaleForMinimumOne(stunBase));
            }

            if (float.IsNaN(required) || float.IsInfinity(required))
                return 1f;

            return Mathf.Clamp01(required);
        }

        private static float GetEffectiveChargeStrengthForThreshold(ChargeHandler chargeHandler, int abilityLevel)
        {
            if (chargeHandler.chargeStrength > 0f)
            {
                return chargeHandler.chargeStrength;
            }

            if (abilityLevel <= 0)
                return 0f;

            return EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHChargeStrengthBaseValue,
                FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                abilityLevel,
                FeatureFlags.DHHChargeStrengthThresholdLevel,
                FeatureFlags.DHHChargeStrengthDiminishingFactor).FinalValue;
        }

        private static float GetEffectiveMaxBouncesForThreshold(ChargeHandler chargeHandler, int abilityLevel)
        {
            if (chargeHandler.maxBounces > 0f)
            {
                return chargeHandler.maxBounces;
            }

            if (abilityLevel <= 0)
                return 0f;

            var baseMaxBounces = chargeHandler.baseMaxBounces > 0 ? chargeHandler.baseMaxBounces : 3f;

            return Mathf.FloorToInt(EvaluateStatWithDiminishingReturns(baseMaxBounces, 0.5f, abilityLevel, 20, 0.9f).FinalValue);
        }

        private static bool TryGetEffectiveChargeAbilityLevel(ChargeHandler chargeHandler, out int abilityLevel)
        {
            abilityLevel = Mathf.Max(0, chargeHandler.AbilityLevel);
            if (abilityLevel > 0)
                return true;

            // On non-master local clients, ChargeWindup/ResetState is authoritative on host.
            // During that phase the local ChargeHandler can transiently report level 0, causing UI threshold=100%.
            // Fallback to local stats entry only for local preview path.
            if (!SemiFunc.IsMasterClientOrSingleplayer() && IsLocalChargeHandler(chargeHandler) && TryGetLocalPlayerChargeUpgrade(out var localUpgrade))
            {
                abilityLevel = Mathf.Max(0, localUpgrade);
                return true;
            }

            return abilityLevel >= 0;
        }

        private static bool TryGetLocalPlayerChargeUpgrade(out int upgrade)
        {
            upgrade = 0;

            var avatar = PlayerAvatar.instance;
            var steamId = avatar != null ? SemiFunc.PlayerGetSteamID(avatar) : null;
            if (string.IsNullOrWhiteSpace(steamId))
                return false;

            try
            {
                upgrade = DHHStatsManager.GetHeadChargeUpgrade(steamId!);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float RequiredScaleForMinimumOne(float baseValue)
        {
            if (baseValue <= 0f)
                return float.PositiveInfinity;
            return 1f / baseValue;
        }

        private static bool IsChargeHandlerHeadGrabbed(ChargeHandler chargeHandler)
        {
            var impactDetector = chargeHandler.impactDetector;
            if (impactDetector == null)
                return false;

            var physGrabObject = impactDetector.physGrabObject;
            if (physGrabObject == null)
                return false;

            return physGrabObject.grabbed;
        }

        private static int GetUnityObjectInstanceId(UnityEngine.Object? obj)
        {
            return obj != null ? obj.GetInstanceID() : 0;
        }

        private static ChargeHoldState GetOrCreateChargeHoldState(int id)
        {
            if (!s_chargeHoldStates.TryGetValue(id, out var state))
            {
                state = new ChargeHoldState();
                s_chargeHoldStates[id] = state;
            }

            return state;
        }

        private static bool IsChargeState(ChargeHandler chargeHandler, string stateName)
        {
            if (chargeHandler == null)
                return false;

            return string.Equals(chargeHandler.State.ToString(), stateName, StringComparison.Ordinal);
        }

        private static bool IsLocalChargeHandler(ChargeHandler chargeHandler)
        {
            var local = GetLocalChargeHandler();
            if (local == null)
                return false;
            return ReferenceEquals(local, chargeHandler);
        }

        private static ChargeHandler? GetLocalChargeHandler()
        {
            var avatar = PlayerAvatar.instance;
            if (avatar?.playerDeathHead == null)
                return null;

            return avatar.playerDeathHead.GetComponent<DeathHeadController>()?.chargeHandler;
        }

        private static void ClearChargeHoldState(ChargeHandler? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id != 0)
            {
                s_chargeHoldStates.Remove(id);
            }
            AbilityModule.SetChargeSlotActivationProgress(0f);
        }

        private static void ChargeHandler_ResetState_Postfix(ChargeHandler __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            if (__instance == null)
                return;

            var level = __instance.AbilityLevel;

            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHChargeStrengthBaseValue,
                FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                level,
                FeatureFlags.DHHChargeStrengthThresholdLevel,
                FeatureFlags.DHHChargeStrengthDiminishingFactor);

            __instance.chargeStrength = stat.FinalValue;
            LogChargeStrength(__instance, stat);
        }

        private static void ChargeHandler_EndCharge_Postfix(ChargeHandler __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            SynchronizeFinalChargeState(__instance);
        }

        private static void SynchronizeFinalChargeState(ChargeHandler? chargeHandler)
        {
            if (chargeHandler == null ||
                chargeHandler.State != ChargeHandler.ChargeState.None ||
                chargeHandler.previousState == ChargeHandler.ChargeState.None ||
                !SemiFunc.IsMultiplayer() ||
                !SemiFunc.IsMasterClient())
            {
                return;
            }

            try
            {
                var photonView = chargeHandler.controller?.photonView;
                if (photonView == null || photonView.ViewID <= 0)
                    return;

                photonView.RPC(
                    nameof(ChargeHandler.SyncChargeStateRPC),
                    RpcTarget.Others,
                    new object[] { ChargeHandler.ChargeState.None });
                chargeHandler.previousState = ChargeHandler.ChargeState.None;

                if (FeatureFlags.DebugLogging)
                    s_log?.LogDebug($"[Fix:DHHChargeAudio] Synchronized final Charge state immediately for view {photonView.ViewID}.");
            }
            catch (Exception ex)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHChargeAudio.FinalStateSync", 30))
                {
                    s_log?.LogDebug($"[Fix:DHHChargeAudio] Immediate final-state sync failed: {ex.GetType().Name}; native Update sync remains available.");
                }
            }
        }

        private static void StopChargeWindupLoop(ChargeHandler? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            try
            {
                var controller = chargeHandler.controller;
                if (controller == null)
                    return;

                var chargeEffects = controller.GetComponentInChildren<ChargeEffects>(true);
                if (chargeEffects != null)
                {
                    try
                    {
                        chargeEffects.StopWindupState();
                    }
                    catch
                    {
                        // Ignore and continue with the remaining cleanup path.
                    }

                    try
                    {
                        chargeEffects.StopChargeState();
                    }
                    catch
                    {
                        // Ignore and continue with the remaining cleanup path.
                    }

                    return;
                }

                var audioHandler = controller.audioHandler;
                if (audioHandler == null)
                    return;

                audioHandler.StopWindupSound();
            }
            catch
            {
                // Audio stop is cosmetic; failures must not affect stun/charge logic.
            }
        }

        private static DiminishingReturnsResult EvaluateStatWithDiminishingReturns(float baseValue, float increasePerLevel, int currentLevel, int thresholdLevel, float diminishingFactor)
        {
            var appliedLevel = currentLevel;
            var normalizedLevel = Math.Max(0, appliedLevel - 1);
            var normalizedThreshold = Math.Max(0, thresholdLevel - 1);
            var linearLevels = Mathf.Min(normalizedLevel, normalizedThreshold);
            var extraLevels = Mathf.Max(0, normalizedLevel - normalizedThreshold);
            var diminishingComponent = extraLevels * Mathf.Pow(diminishingFactor, extraLevels);
            var linearContribution = increasePerLevel * linearLevels;
            var diminishingContribution = increasePerLevel * diminishingComponent;
            var finalValue = baseValue + linearContribution + diminishingContribution;

            return new DiminishingReturnsResult(
                baseValue,
                increasePerLevel,
                appliedLevel,
                thresholdLevel,
                diminishingFactor,
                linearLevels,
                extraLevels,
                linearContribution,
                diminishingContribution,
                diminishingComponent,
                finalValue);
        }

        private readonly struct DiminishingReturnsResult
        {
            public DiminishingReturnsResult(float baseValue, float increasePerLevel, int appliedLevel, int thresholdLevel, float diminishingFactor,
                int linearLevels, int extraLevels, float linearContribution, float diminishingContribution, float diminishingComponent, float finalValue)
            {
                BaseValue = baseValue;
                IncreasePerLevel = increasePerLevel;
                AppliedLevel = appliedLevel;
                ThresholdLevel = thresholdLevel;
                DiminishingFactor = diminishingFactor;
                LinearLevels = linearLevels;
                ExtraLevels = extraLevels;
                LinearContribution = linearContribution;
                DiminishingContribution = diminishingContribution;
                DiminishingComponent = diminishingComponent;
                FinalValue = finalValue;
            }

            public float BaseValue { get; }
            public float IncreasePerLevel { get; }
            public int AppliedLevel { get; }
            public int ThresholdLevel { get; }
            public float DiminishingFactor { get; }
            public int LinearLevels { get; }
            public int ExtraLevels { get; }
            public float LinearContribution { get; }
            public float DiminishingContribution { get; }
            public float DiminishingComponent { get; }
            public float FinalValue { get; }
        }

        private static string GetHandlerLabel(object? handler, string fallback)
        {
            if (handler is Component component)
            {
                return component.name ?? component.GetType().Name;
            }

            return handler?.GetType().Name ?? fallback;
        }

        private static void LogChargeStrength(object chargeHandler, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(ChargeStrengthLogKey, 60))
                return;

            var label = GetHandlerLabel(chargeHandler, "ChargeHandler");
            var message = $"[Fix:Charge] {label} Strength={stat.FinalValue:F3} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} level={stat.AppliedLevel} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            s_log?.LogInfo(message);
            Debug.Log(message);
        }

        private static Vector3 CalculateEnemyBounceNormal(Transform? self, Vector3 enemyCenterPoint)
        {
            if (self == null)
                return Vector3.up;

            var frontPoint = self.TransformPoint(Vector3.up * 0.3f);
            var directionVector = frontPoint - enemyCenterPoint;
            return Vector3.ProjectOnPlane(directionVector, Vector3.up).normalized;
        }
    }
}
