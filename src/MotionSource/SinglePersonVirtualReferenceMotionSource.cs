using DebugUtils;
using SimpleJSON;
using ToySerialController.UI;
using UnityEngine;

namespace ToySerialController.MotionSource
{
    // =====================================================================
    // SinglePersonVirtualReferenceMotionSource (rewritten)
    //
    // Design overview:
    //  1. Reference Point – a world-space pose captured from the current
    //                      target point. It acts as the follow baseline.
    //  2. Offset0        – spring rest position relative to the follow anchor.
    //                      Length sets the scale; Up/Right/Forward in %*Length,
    //                      Pitch/Yaw/Roll in degrees. Up defaults to 50 %.
    //  3. Follow         – target translation/rotation deltas advance the
    //                      follow anchor and the VR pose by per-axis %. 0 % is
    //                      world-stationary, 100 % fully follows target motion.
    //  4. Offset1        – optional. Measures how far VR currently sits from
    //                      the Offset0 rest pose in the follow-anchor frame,
    //                      scales it per-axis, clamps it, and adds it to Offset0.
    //  5. Spring+Damper  – per-axis (3 pos + 3 rot), pulls VR toward the
    //                      follow anchor plus Offset0+Offset1.
    // =====================================================================
    public class SinglePersonVirtualReferenceMotionSource : AbstractRefreshableMotionSource
    {
        private class ReferenceProxy : IMotionSourceReference
        {
            private readonly SinglePersonVirtualReferenceMotionSource _source;

            public ReferenceProxy(SinglePersonVirtualReferenceMotionSource source)
            {
                _source = source;
            }

            public Vector3 Position => _source.ReferencePosition;
            public Vector3 Up => _source.ReferenceUp;
            public Vector3 Right => _source.ReferenceRight;
            public Vector3 Forward => _source.ReferenceForward;
            public float Length => _source.ReferenceLength;
            public float Radius => _source.ReferenceRadius;
            public Vector3 PlaneNormal => _source.ReferencePlaneNormal;
            public Vector3 PlaneTangent => _source.ReferencePlaneTangent;

            public void Refresh() { }
            public bool Update() => true;
            public void CreateUI(IUIBuilder builder) { }
            public void DestroyUI(IUIBuilder builder) { }
            public void StoreConfig(JSONNode config) { }
            public void RestoreConfig(JSONNode config) { }
        }

        private readonly IMotionSourceTarget _target;
        private readonly ReferenceProxy _referenceProxy;
        private readonly string _keyPrefix;

        // ── UI title buttons ───────────────────────────────────────────────
        private UIDynamicButton _targetTitle;
        private UIDynamicButton _referenceTitle;
        private UIDynamicButton _baseSettingsTitle;
        private UIDynamicButton _offset1SettingsTitle;
        private UIDynamicButton _followSettingsTitle;
        private UIDynamicButton _springSettingsTitle;
        // ── UI groups ──────────────────────────────────────────────────────
        private UIGroup _baseGroup;
        private UIGroup _offset1Group;
        private UIGroup _followGroup;
        private UIGroup _springGroup;

        // ── Base (Offset0) parameters ──────────────────────────────────────
        private JSONStorableFloat _lengthSlider;          // cm
        private JSONStorableFloat _offset0UpSlider;       // insertion depth %, 0 = not inserted, 100 = fully inserted
        private JSONStorableFloat _offset0RightSlider;
        private JSONStorableFloat _offset0ForwardSlider;
        private JSONStorableFloat _offset0PitchSlider;    // degrees
        private JSONStorableFloat _offset0YawSlider;
        private JSONStorableFloat _offset0RollSlider;

        // ── Offset1 parameters ─────────────────────────────────────────────
        private JSONStorableBool  _offset1Enabled;
        private JSONStorableFloat _referencePointUpOffsetSlider; // cm, applied on top of base height * size * 0.48
        // position factor & limit (% of length)
        private JSONStorableFloat _offset1UpFactor;
        private JSONStorableFloat _offset1RightFactor;
        private JSONStorableFloat _offset1ForwardFactor;
        private JSONStorableFloat _offset1UpLimit;
        private JSONStorableFloat _offset1RightLimit;
        private JSONStorableFloat _offset1ForwardLimit;
        // rotation factor & limit (degrees)
        private JSONStorableFloat _offset1PitchFactor;
        private JSONStorableFloat _offset1YawFactor;
        private JSONStorableFloat _offset1RollFactor;
        private JSONStorableFloat _offset1PitchLimit;
        private JSONStorableFloat _offset1YawLimit;
        private JSONStorableFloat _offset1RollLimit;

        // ── Follow parameters ──────────────────────────────────────────────
        // Translation follow: applied to VR velocity
        private JSONStorableFloat _followUpFactor;
        private JSONStorableFloat _followRightFactor;
        private JSONStorableFloat _followForwardFactor;
        // Rotation follow: applied to VR angular velocity
        private JSONStorableFloat _followPitchFactor;
        private JSONStorableFloat _followYawFactor;
        private JSONStorableFloat _followRollFactor;

        // ── Spring + Damper parameters (per axis) ─────────────────────────
        private JSONStorableFloat _springUpK;
        private JSONStorableFloat _springRightK;
        private JSONStorableFloat _springForwardK;
        private JSONStorableFloat _springPitchK;
        private JSONStorableFloat _springYawK;
        private JSONStorableFloat _springRollK;
        private JSONStorableFloat _damperUpK;
        private JSONStorableFloat _damperRightK;
        private JSONStorableFloat _damperForwardK;
        private JSONStorableFloat _damperPitchK;
        private JSONStorableFloat _damperYawK;
        private JSONStorableFloat _damperRollK;

        // ── Runtime state ──────────────────────────────────────────────────
        private Vector3    _targetPosition;
        private Vector3    _targetUp;
        private Vector3    _targetRight;
        private Vector3    _targetForward;
        private Vector3    _prevTargetPosition;
        private Quaternion _prevTargetRotation;
        private Vector3    _referencePointPosition;
        private Quaternion _referencePointRotation;

        private Vector3    _referencePosition;
        private Quaternion _referenceRotation;
        private Vector3    _vrLinearVelocity;    // spring-only residual velocity, world-space
        private Vector3    _vrAngularVelocity;   // spring-only residual angular velocity in follow-anchor local frame (deg/s)
        private Vector3    _debugOffset0TargetPosition;
        private Vector3    _debugOffset01TargetPosition;

        private bool _initialized;

        // ── UI fold state ──────────────────────────────────────────────────
        private bool _baseVisible;
        private bool _offset1Visible;
        private bool _followVisible;
        private bool _springVisible;

        public SinglePersonVirtualReferenceMotionSource(IMotionSourceTarget target, string keyPrefix)
        {
            _target = target;
            _referenceProxy = new ReferenceProxy(this);
            _keyPrefix = keyPrefix;
        }

        public override Vector3 ReferencePosition => _referencePosition;
        public override Vector3 ReferenceUp => _referenceRotation * Vector3.up;
        public override Vector3 ReferenceRight => _referenceRotation * Vector3.right;
        public override Vector3 ReferenceForward => _referenceRotation * Vector3.forward;
        public override float ReferenceLength => _lengthSlider != null ? _lengthSlider.val / 100f : 0.14f;
        public override float ReferenceRadius => Mathf.Max(0.01f, ReferenceLength * 0.3f);
        public override Vector3 ReferencePlaneNormal => ReferenceUp;
        public override Vector3 ReferencePlaneTangent => ReferenceRight;

        public override Vector3 TargetPosition => _targetPosition;
        public override Vector3 TargetUp => _targetUp;
        public override Vector3 TargetRight => _targetRight;
        public override Vector3 TargetForward => _targetForward;

        // =================================================================
        // UI
        // =================================================================
        public override void CreateUI(IUIBuilder builder)
        {
            _targetTitle = builder.CreateDisabledButton("Target", new Color(1.0f, 1.0f, 1.0f, 0.075f), Color.white);
            _targetTitle.buttonText.fontStyle = FontStyle.Bold;
            _target.CreateUI(builder);

            _referenceTitle = builder.CreateDisabledButton("Virtual Reference", new Color(1.0f, 1.0f, 1.0f, 0.075f), Color.white);
            _referenceTitle.buttonText.fontStyle = FontStyle.Bold;

            _baseGroup    = new UIGroup(builder);
            _offset1Group = new UIGroup(builder);
            _followGroup  = new UIGroup(builder);
            _springGroup  = new UIGroup(builder);

            // ---- Base (Offset0) ----
            _baseSettingsTitle = builder.CreateButton("Virtual Reference Base", () =>
            {
                _baseVisible = !_baseVisible;
                _baseGroup.SetVisible(_baseVisible);
            }, new Color(0.3f, 0.3f, 0.3f), Color.white);

            _lengthSlider        = _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:LengthCm",      "Reference Length (cm)",  14f,   2f,    100f,  true, true, valueFormat: "F1");
            _offset0UpSlider     = _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Up",     "Offset0 Up (%)",         0.5f,  0f,   1f,    true, true, valueFormat: "P0");
            _offset0RightSlider  = _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Right",  "Offset0 Right (%)",      0f,   -1f,   1f,    true, true, valueFormat: "P0");
            _offset0ForwardSlider= _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Fwd",    "Offset0 Forward (%)",    0f,   -1f,   1f,    true, true, valueFormat: "P0");
            _offset0PitchSlider  = _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Pitch",  "Offset0 Pitch (°)",      0f,  -180f,  180f,  true, true, valueFormat: "F0");
            _offset0YawSlider    = _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Yaw",    "Offset0 Yaw (°)",        0f,  -180f,  180f,  true, true, valueFormat: "F0");
            _offset0RollSlider   = _baseGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Roll",   "Offset0 Roll (°)",       0f,  -180f,  180f,  true, true, valueFormat: "F0");

            // ---- Offset1 ----
            _offset1SettingsTitle = builder.CreateButton("Virtual Reference Offset1", () =>
            {
                _offset1Visible = !_offset1Visible;
                _offset1Group.SetVisible(_offset1Visible);
            }, new Color(0.3f, 0.3f, 0.3f), Color.white);

            _referencePointUpOffsetSlider = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:ReferencePointUpOffsetCm", "Reference Point Up Offset (cm)", 0f, -50f, 50f, true, true, valueFormat: "F0");
            _offset1Enabled      = _offset1Group.CreateToggle($"MotionSource:{_keyPrefix}:Offset1Enabled", "Offset1 Enabled", true);
            _offset1UpFactor     = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1UpFactor",      "Offset1 Up Factor (%)",       0.4f, 0f, 1f, true, true, valueFormat: "P0");
            _offset1RightFactor  = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RightFactor",   "Offset1 Right Factor (%)",    0.02f, 0f, 1f, true, true, valueFormat: "P0");
            _offset1ForwardFactor= _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1FwdFactor",     "Offset1 Forward Factor (%)",  0.02f, 0f, 1f, true, true, valueFormat: "P0");
            _offset1UpLimit      = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1UpLimit",       "Offset1 Up Limit (%)",        0.3f, 0f, 2f, true, true, valueFormat: "P0");
            _offset1RightLimit   = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RightLimit",    "Offset1 Right Limit (%)",     0.1f, 0f, 2f, true, true, valueFormat: "P0");
            _offset1ForwardLimit = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1FwdLimit",      "Offset1 Forward Limit (%)",   0.1f, 0f, 2f, true, true, valueFormat: "P0");
            _offset1PitchFactor  = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1PitchFactor",   "Offset1 Pitch Factor (%)",    0.2f, 0f, 1f, true, true, valueFormat: "P0");
            _offset1YawFactor    = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1YawFactor",     "Offset1 Yaw Factor (%)",      0.2f, 0f, 1f, true, true, valueFormat: "P0");
            _offset1RollFactor   = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RollFactor",    "Offset1 Roll Factor (%)",     0.3f, 0f, 1f, true, true, valueFormat: "P0");
            _offset1PitchLimit   = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1PitchLimit",    "Offset1 Pitch Limit (°)",     30f,  0f, 180f, true, true, valueFormat: "F0");
            _offset1YawLimit     = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1YawLimit",      "Offset1 Yaw Limit (°)",       30f,  0f, 180f, true, true, valueFormat: "F0");
            _offset1RollLimit    = _offset1Group.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RollLimit",     "Offset1 Roll Limit (°)",      50f,  0f, 180f, true, true, valueFormat: "F0");

            // ---- Follow ----
            _followSettingsTitle = builder.CreateButton("Virtual Reference Follow", () =>
            {
                _followVisible = !_followVisible;
                _followGroup.SetVisible(_followVisible);
            }, new Color(0.3f, 0.3f, 0.3f), Color.white);

            _followUpFactor      = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowUp",      "Follow Up (%)",      0.2f, 0f, 1f, true, true, valueFormat: "P0");
            _followRightFactor   = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowRight",   "Follow Right (%)",   0.8f, 0f, 1f, true, true, valueFormat: "P0");
            _followForwardFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowForward", "Follow Forward (%)", 0.8f, 0f, 1f, true, true, valueFormat: "P0");
            _followPitchFactor   = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowPitch",   "Follow Pitch (%)",   0.3f, 0f, 1f, true, true, valueFormat: "P0");
            _followYawFactor     = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowYaw",     "Follow Yaw (%)",     0.3f, 0f, 1f, true, true, valueFormat: "P0");
            _followRollFactor    = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowRoll",    "Follow Roll (%)",    0.3f, 0f, 1f, true, true, valueFormat: "P0");

            // ---- Spring + Damper ----
            _springSettingsTitle = builder.CreateButton("Virtual Reference Spring", () =>
            {
                _springVisible = !_springVisible;
                _springGroup.SetVisible(_springVisible);
            }, new Color(0.3f, 0.3f, 0.3f), Color.white);

            _springUpK      = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:SpringUpK",      "Spring Up K",      30f, 0f, 200f, true, true, valueFormat: "F1");
            _springRightK   = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:SpringRightK",   "Spring Right K",   100f, 0f, 200f, true, true, valueFormat: "F1");
            _springForwardK = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:SpringFwdK",     "Spring Forward K", 100f, 0f, 200f, true, true, valueFormat: "F1");
            _springPitchK   = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:SpringPitchK",   "Spring Pitch K",   30f, 0f, 200f, true, true, valueFormat: "F1");
            _springYawK     = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:SpringYawK",     "Spring Yaw K",     30f, 0f, 200f, true, true, valueFormat: "F1");
            _springRollK    = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:SpringRollK",    "Spring Roll K",    30f, 0f, 200f, true, true, valueFormat: "F1");
            _damperUpK      = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:DamperUpK",      "Damper Up K",      20f,  0f, 100f,  true, true, valueFormat: "F1");
            _damperRightK   = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:DamperRightK",   "Damper Right K",   8f,  0f, 100f,  true, true, valueFormat: "F1");
            _damperForwardK = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:DamperFwdK",     "Damper Forward K", 8f,  0f, 100f,  true, true, valueFormat: "F1");
            _damperPitchK   = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:DamperPitchK",   "Damper Pitch K",   8f,  0f, 100f,  true, true, valueFormat: "F1");
            _damperYawK     = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:DamperYawK",     "Damper Yaw K",     8f,  0f, 100f,  true, true, valueFormat: "F1");
            _damperRollK    = _springGroup.CreateSlider($"MotionSource:{_keyPrefix}:DamperRollK",    "Damper Roll K",    8f,  0f, 100f,  true, true, valueFormat: "F1");

            _baseGroup.SetVisible(false);
            _offset1Group.SetVisible(false);
            _followGroup.SetVisible(false);
            _springGroup.SetVisible(false);

            base.CreateUI(builder);
        }

        public override void DestroyUI(IUIBuilder builder)
        {
            base.DestroyUI(builder);

            _baseGroup?.Destroy();
            _offset1Group?.Destroy();
            _followGroup?.Destroy();
            _springGroup?.Destroy();

            builder.Destroy(_springSettingsTitle);
            builder.Destroy(_followSettingsTitle);
            builder.Destroy(_offset1SettingsTitle);
            builder.Destroy(_baseSettingsTitle);
            builder.Destroy(_referenceTitle);

            _target.DestroyUI(builder);
            builder.Destroy(_targetTitle);
        }

        public override void StoreConfig(JSONNode config)
        {
            _target.StoreConfig(config);
            _baseGroup?.StoreConfig(config);
            _offset1Group?.StoreConfig(config);
            _followGroup?.StoreConfig(config);
            _springGroup?.StoreConfig(config);
        }

        public override void RestoreConfig(JSONNode config)
        {
            _target.RestoreConfig(config);
            _baseGroup?.RestoreConfig(config);
            _offset1Group?.RestoreConfig(config);
            _followGroup?.RestoreConfig(config);
            _springGroup?.RestoreConfig(config);
            _initialized = false;
        }

        // =================================================================
        // Update (called every frame)
        // =================================================================
        public override bool Update()
        {
            if (!_target.Update(_referenceProxy))
                return false;

            _targetPosition = _target.Position;
            _targetUp       = _target.Up;
            _targetRight    = _target.Right;
            _targetForward  = _target.Forward;

            var targetRotation = Quaternion.LookRotation(_targetForward, _targetUp);
            UpdateReferencePoint();

            if (!_initialized)
            {
                Initialize(targetRotation);
                return true;
            }

            var personTarget = _target as AbstractPersonTarget;
            if (personTarget != null && personTarget.ConsumeSelectionChanged())
            {
                Initialize(targetRotation);
                return true;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.1f);

            // ── compute target motion this frame ──────────────────────────
            Vector3    tpDeltaPos = _targetPosition - _prevTargetPosition;
            Quaternion tpDeltaRot = targetRotation * Quaternion.Inverse(_prevTargetRotation);

            UpdatePositionAndRotation(targetRotation, tpDeltaPos, tpDeltaRot, dt);

            _prevTargetPosition = _targetPosition;
            _prevTargetRotation = targetRotation;

            DrawDebug(targetRotation);
            return true;
        }

        // =================================================================
        // Core physics update
        // =================================================================
        private void UpdatePositionAndRotation(Quaternion tpRot, Vector3 tpDeltaPos, Quaternion tpDeltaRot, float dt)
        {
            float length = ReferenceLength;
            Vector3 springFramePosition = _targetPosition;
            Quaternion springFrameRotation = tpRot;
            Vector3 referencePositionForOffset1 = _referencePosition;
            Quaternion referenceRotationForOffset1 = _referenceRotation;

            // Decompose TP delta into TP-local space (using PREVIOUS frame's rotation as reference)
            Quaternion prevTpRot = _prevTargetRotation;
            Vector3 tpLocalDelta = Quaternion.Inverse(prevTpRot) * tpDeltaPos;
            // tpDeltaRot expressed in TP local frame: L = Inv(prevTpRot) * tpDeltaRot * prevTpRot
            Vector3 tpLocalEuler = SignedEuler(Quaternion.Inverse(prevTpRot) * tpDeltaRot * prevTpRot);

            Vector3 followTranslationLocal = new Vector3(
                tpLocalDelta.x * _followRightFactor.val,
                tpLocalDelta.y * _followUpFactor.val,
                tpLocalDelta.z * _followForwardFactor.val
            );
            Vector3 followTranslationWorld = prevTpRot * followTranslationLocal;

            Quaternion followRotationLocal = Quaternion.Euler(
                tpLocalEuler.x * _followPitchFactor.val,
                tpLocalEuler.y * _followYawFactor.val,
                tpLocalEuler.z * _followRollFactor.val
            );
            Quaternion followRotationWorld = prevTpRot * followRotationLocal * Quaternion.Inverse(prevTpRot);

            // ── Step 1: Lever pivot (100%, always) ────────────────────────
            // Rotate VR position around prevTpPos by tpDeltaRot (pure orbit, no spin)
            Vector3 vrToPrevTarget = _referencePosition - _prevTargetPosition;
            _referencePosition = _prevTargetPosition + tpDeltaRot * vrToPrevTarget;
            _referencePosition += followTranslationWorld;
            _referenceRotation = NormalizeQuaternion(followRotationWorld * _referenceRotation);

            // ── Step 4: Compute spring target (Offset0 + Offset1) ─────────
            // Offset acts on the current target point. Spring target must move with the target
            // even when Follow is 0, otherwise the spring has nothing to pull toward.
            Vector3 restLocalPos = GetOffset0LocalPosition(length);
            Quaternion restLocalRot = Quaternion.Euler(
                _offset0PitchSlider.val,
                _offset0YawSlider.val,
                _offset0RollSlider.val
            );

            Vector3    springTargetLocalPos = restLocalPos;
            Quaternion springTargetLocalRot = restLocalRot;

            if (_offset1Enabled != null && _offset1Enabled.val)
            {
                // Offset1 is measured from the dynamic reference point (cyan marker).
                Vector3 devWorld = referencePositionForOffset1 - _referencePointPosition;
                Vector3 devLocal = Quaternion.Inverse(_referencePointRotation) * devWorld;

                float o1Up  = Mathf.Clamp(devLocal.y * _offset1UpFactor.val,
                                           -_offset1UpLimit.val * length, _offset1UpLimit.val * length);
                float o1Rt  = Mathf.Clamp(devLocal.x * _offset1RightFactor.val,
                                           -_offset1RightLimit.val * length, _offset1RightLimit.val * length);
                float o1Fwd = Mathf.Clamp(devLocal.z * _offset1ForwardFactor.val,
                                           -_offset1ForwardLimit.val * length, _offset1ForwardLimit.val * length);

                springTargetLocalPos -= new Vector3(o1Rt, o1Up, o1Fwd);

                // Rotation Offset1
                Quaternion rotDevLocalQ = Quaternion.Inverse(_referencePointRotation) * referenceRotationForOffset1 * Quaternion.Inverse(restLocalRot);
                Vector3 rotDevLocal = SignedEuler(rotDevLocalQ);

                float o1Pitch = Mathf.Clamp(rotDevLocal.x * _offset1PitchFactor.val,
                                             -_offset1PitchLimit.val, _offset1PitchLimit.val);
                float o1Yaw   = Mathf.Clamp(rotDevLocal.y * _offset1YawFactor.val,
                                             -_offset1YawLimit.val, _offset1YawLimit.val);
                float o1Roll  = Mathf.Clamp(rotDevLocal.z * _offset1RollFactor.val,
                                             -_offset1RollLimit.val, _offset1RollLimit.val);

                springTargetLocalRot = Quaternion.Euler(
                    _offset0PitchSlider.val - o1Pitch,
                    _offset0YawSlider.val   - o1Yaw,
                    _offset0RollSlider.val  - o1Roll
                );
            }

            _debugOffset0TargetPosition = springFramePosition + springFrameRotation * restLocalPos;

            // ── Step 5: Spring + Damper on position (additive to velocity) ──
            Vector3 springTargetWorldPos = springFramePosition + springFrameRotation * springTargetLocalPos;
            _debugOffset01TargetPosition = springTargetWorldPos;
            Vector3 errWorld = springTargetWorldPos - _referencePosition;
            Vector3 errLocal = Quaternion.Inverse(springFrameRotation) * errWorld;
            Vector3 vrLocalVel = Quaternion.Inverse(springFrameRotation) * _vrLinearVelocity;

            Vector3 springForceLocal = new Vector3(
                errLocal.x * _springRightK.val   - vrLocalVel.x * _damperRightK.val,
                errLocal.y * _springUpK.val       - vrLocalVel.y * _damperUpK.val,
                errLocal.z * _springForwardK.val  - vrLocalVel.z * _damperForwardK.val
            );
            _vrLinearVelocity += springFrameRotation * springForceLocal * dt;

            // Integrate position
            _referencePosition += _vrLinearVelocity * dt;

            // ── Step 6: Spring + Damper on rotation (additive to angular velocity) ──
            Quaternion springTargetWorldRot = springFrameRotation * springTargetLocalRot;
            Quaternion rotError = springTargetWorldRot * Quaternion.Inverse(_referenceRotation);
            Vector3 rotErrLocal = SignedEuler(Quaternion.Inverse(springFrameRotation) * rotError * springFrameRotation);

            Vector3 springTorque = new Vector3(
                rotErrLocal.x * _springPitchK.val - _vrAngularVelocity.x * _damperPitchK.val,
                rotErrLocal.y * _springYawK.val   - _vrAngularVelocity.y * _damperYawK.val,
                rotErrLocal.z * _springRollK.val  - _vrAngularVelocity.z * _damperRollK.val
            );
            _vrAngularVelocity += springTorque * dt;

            // Integrate rotation: apply angular velocity in TP-local frame
            Quaternion angVelQ = Quaternion.Euler(_vrAngularVelocity * dt);
            Quaternion angVelWorldQ = springFrameRotation * angVelQ * Quaternion.Inverse(springFrameRotation);
            _referenceRotation = NormalizeQuaternion(angVelWorldQ * _referenceRotation);
        }

        // =================================================================
        // Helpers
        // =================================================================
        protected override void RefreshButtonCallback()
        {
            _target.Refresh();
            _initialized = false;
        }

        private void Initialize(Quaternion targetRotation)
        {
            float length = ReferenceLength;
            Vector3 restLocalPos = GetOffset0LocalPosition(length);
            Quaternion restLocalRot = Quaternion.Euler(
                _offset0PitchSlider?.val ?? 0f,
                _offset0YawSlider?.val ?? 0f,
                _offset0RollSlider?.val ?? 0f
            );

            UpdateReferencePoint();
            _referencePosition = _targetPosition + targetRotation * restLocalPos;
            _referenceRotation = targetRotation * restLocalRot;
            _debugOffset0TargetPosition = _referencePosition;
            _debugOffset01TargetPosition = _referencePosition;
            _vrLinearVelocity  = Vector3.zero;
            _vrAngularVelocity = Vector3.zero;

            _prevTargetPosition = _targetPosition;
            _prevTargetRotation = targetRotation;

            _initialized = true;
        }

        private void DrawDebug(Quaternion targetRotation)
        {
            if (!DebugDraw.Enabled)
                return;

            DebugDraw.DrawTransform(_targetPosition, _targetUp, _targetRight, _targetForward, 0.12f);
            DebugDraw.DrawLine(_referencePosition, _targetPosition, Color.yellow);
            DebugDraw.DrawTransform(_referencePosition, ReferenceUp, ReferenceRight, ReferenceForward, 0.15f);
            DebugDraw.DrawPoint(_referencePointPosition, Color.cyan, 0.012f);

            // Spring targets
            DebugDraw.DrawPoint(_debugOffset0TargetPosition, Color.blue, 0.01f);
            DebugDraw.DrawPoint(_debugOffset01TargetPosition, Color.green, 0.01f);
        }

        private void UpdateReferencePoint()
        {
            var personTarget = _target as AbstractPersonTarget;
            if (personTarget == null)
            {
                _referencePointRotation = Quaternion.LookRotation(_targetForward, _targetUp);
                _referencePointPosition = _targetPosition;
                return;
            }

            _referencePointRotation = personTarget.RootRotation;
            float referenceHeight = personTarget.BaseHeightMeters * personTarget.SizeScale * 0.48f;
            float userOffsetMeters = (_referencePointUpOffsetSlider?.val ?? 0f) / 100f;
            _referencePointPosition = personTarget.RootPosition + (_referencePointRotation * Vector3.up) * (referenceHeight + userOffsetMeters);
        }

        private Vector3 GetOffset0LocalPosition(float length)
        {
            return new Vector3(
                length * (_offset0RightSlider?.val ?? 0f),
                length * GetOffset0UpFactor(),
                length * (_offset0ForwardSlider?.val ?? 0f)
            );
        }

        private float GetOffset0UpFactor()
        {
            float insertion = _offset0UpSlider?.val ?? 0.5f;
            return insertion - 1f;
        }

        // Returns euler angles in (-180, 180] range for each component.
        private static Vector3 SignedEuler(Quaternion q)
        {
            var e = q.eulerAngles;
            return new Vector3(Norm180(e.x), Norm180(e.y), Norm180(e.z));
        }

        private static float Norm180(float a)
        {
            a %= 360f;
            if (a > 180f)  a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        private static Quaternion NormalizeQuaternion(Quaternion q)
        {
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (magnitude <= Mathf.Epsilon)
                return Quaternion.identity;

            float inverseMagnitude = 1f / magnitude;
            return new Quaternion(
                q.x * inverseMagnitude,
                q.y * inverseMagnitude,
                q.z * inverseMagnitude,
                q.w * inverseMagnitude
            );
        }
    }
}
