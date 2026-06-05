using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DebugUtils;
using SimpleJSON;
using ToySerialController.UI;
using ToySerialController.Utils;
using UnityEngine;

namespace ToySerialController.MotionSource
{
    public class DrivenMode
    {
        private readonly IDrivableReference _reference;
        private readonly string _keyPrefix;
        private readonly DrivenAnchorEmpty _referencePointAnchor;

        private UIDynamicButton _drivenTitle;

        private UIGroup _drivenGroup;
        private UIGroup _drivenContentGroup;
        private UIGroup _restGroup;
        private UIGroup _compensationGroup;
        private UIGroup _followGroup;
        private UIGroup _springGroup;
        private UIGroup _damperGroup;

        private JSONStorableBool _enabled;

        private JSONStorableFloat _restUpSlider;
        private JSONStorableFloat _restRightSlider;
        private JSONStorableFloat _restForwardSlider;
        private JSONStorableFloat _restTwistSlider;
        private JSONStorableFloat _restRollSlider;
        private JSONStorableFloat _restPitchSlider;

        private JSONStorableBool _compensationEnabled;
        private UIDynamicButton _referencePointEmptyTitle;
        private JSONStorableStringChooser _referencePointEmptyChooser;
        private UIDynamicButton _findOrCreateReferencePointButton;
        private JSONStorableFloat _referencePointUpOffsetSlider;
        private UIDynamicButton _resetReferencePointButton;
        private JSONStorableFloat _compensationUpFactor;
        private JSONStorableFloat _compensationRightFactor;
        private JSONStorableFloat _compensationForwardFactor;
        private JSONStorableFloat _compensationUpLimit;
        private JSONStorableFloat _compensationRightLimit;
        private JSONStorableFloat _compensationForwardLimit;
        private JSONStorableFloat _compensationTwistFactor;
        private JSONStorableFloat _compensationRollFactor;
        private JSONStorableFloat _compensationPitchFactor;
        private JSONStorableFloat _compensationTwistLimit;
        private JSONStorableFloat _compensationRollLimit;
        private JSONStorableFloat _compensationPitchLimit;

        private JSONStorableFloat _followUpFactor;
        private JSONStorableFloat _followRightFactor;
        private JSONStorableFloat _followForwardFactor;
        private JSONStorableFloat _followTwistFactor;
        private JSONStorableFloat _followRollFactor;
        private JSONStorableFloat _followPitchFactor;
        private JSONStorableBool _velocityFollowEnabled;
        private JSONStorableBool _speedFilterEnabled;

        private JSONStorableFloat _springUp;
        private JSONStorableFloat _springRight;
        private JSONStorableFloat _springForward;
        private JSONStorableFloat _springTwist;
        private JSONStorableFloat _springRoll;
        private JSONStorableFloat _springPitch;

        private JSONStorableFloat _velocityDampingUp;
        private JSONStorableFloat _velocityDampingRight;
        private JSONStorableFloat _velocityDampingForward;
        private JSONStorableFloat _velocityDampingTwist;
        private JSONStorableFloat _velocityDampingRoll;
        private JSONStorableFloat _velocityDampingPitch;

        private JSONStorableFloat _targetSmoothingUp;
        private JSONStorableFloat _targetSmoothingRight;
        private JSONStorableFloat _targetSmoothingForward;
        private JSONStorableFloat _targetSmoothingTwist;
        private JSONStorableFloat _targetSmoothingRoll;
        private JSONStorableFloat _targetSmoothingPitch;

        private Vector3 _targetPosition;
        private Vector3 _targetUp;
        private Vector3 _targetRight;
        private Vector3 _targetForward;
        private AbstractPersonTarget _currentPersonTarget;
        private Vector3 _prevTargetPosition;
        private Quaternion _prevTargetRotation;
        private Vector3 _referencePointPosition;
        private Quaternion _referencePointRotation;

        private Vector3 _referencePosition;
        private Quaternion _referenceRotation;
        private Vector3 _vrLinearVelocity;
        private Vector3 _vrAngularVelocity;
        private Vector3 _debugOffset0TargetPosition;
        private Quaternion _debugOffset0TargetRotation;
        private Vector3 _debugOffset01TargetPosition;
        private Quaternion _debugOffset01TargetRotation;
        private Vector3 _debugFinalTargetPosition;
        private Quaternion _debugFinalTargetRotation;
        private Vector3 _referenceToControllerLocalPosition;
        private Vector3 _controllerTargetPosition;
        private Quaternion _controllerTargetRotation;
        private Vector3 _filteredSpringTargetLocalPosition;
        private Vector3 _prevRawSpringTargetLocalPosition;
        private bool _filteredSpringTargetInitialized;
        private readonly Dictionary<Rigidbody, bool> _savedGravityStates = new Dictionary<Rigidbody, bool>();
        private bool _gravityStatesSaved;
        private Vector3 _smoothedSpringTargetLocalPosition;
        private Vector3 _smoothedSpringTargetLocalEuler;

        private bool _initialized;
        private bool _smoothedSpringTargetInitialized;

        private FreeControllerV3.PositionState _savedPositionState;
        private FreeControllerV3.RotationState _savedRotationState;
        private bool _stateSaved;

        private bool _drivenVisible = true;
        private bool _restVisible;
        private bool _compensationVisible;
        private bool _followVisible;
        private bool _springVisible;
        private bool _damperVisible;

        public bool Enabled => _enabled != null && _enabled.val;

        public DrivenMode(IDrivableReference reference)
        {
            _reference = reference;
            _keyPrefix = "Driven" + (reference != null ? reference.DrivenKind : "Unknown");
            _referencePointAnchor = new DrivenAnchorEmpty(_keyPrefix);
        }

        private bool UsesPercentSpringUnits => _reference == null || _reference.DrivenKind != DrivenReferenceKind.Empty;
        private bool UsesEngineHoldSpring => _reference != null && _reference.DrivenKind != DrivenReferenceKind.Empty;

        private float ReferenceLength
        {
            get
            {
                var motionSourceReference = _reference as IMotionSourceReference;
                var referenceLengthScale = UIManager.GetFloat("Device:ReferenceLengthScale");
                var scale = referenceLengthScale != null ? referenceLengthScale.val : 1f;
                if (motionSourceReference != null && motionSourceReference.Length > 0.0001f)
                    return motionSourceReference.Length * scale;
                return 0.14f * scale;
            }
        }

        public void CreateUI(IUIBuilder builder)
        {
            _drivenTitle = builder.CreateButton("Driven", ToggleDrivenVisibility, new Color(0.3f, 0.3f, 0.3f), Color.white);
            _drivenTitle.buttonText.fontStyle = FontStyle.Bold;

            _drivenGroup = new UIGroup(builder);
            _enabled = _drivenGroup.CreateToggle($"MotionSource:{_keyPrefix}:Enabled", "Enabled", false, OnEnabledChanged);
            _drivenContentGroup = new UIGroup(_drivenGroup);
            CreateSectionDivider(_drivenContentGroup);

            var restSectionGroup = new UIGroup(_drivenContentGroup);
            restSectionGroup.CreateButton("Rest Position", ToggleRestVisibility, new Color(0.3f, 0.3f, 0.3f), Color.white);
            _restGroup = new UIGroup(restSectionGroup);
            _restUpSlider = _restGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Up", "Rest Insertion (%)", 0.5f, 0f, 1f, true, true, valueFormat: "P0");
            _restRightSlider = _restGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Right", "Rest Right (%)", 0f, -1f, 1f, true, true, valueFormat: "P0");
            _restForwardSlider = _restGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Fwd", "Rest Forward (%)", 0f, -1f, 1f, true, true, valueFormat: "P0");
            _restTwistSlider = _restGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Yaw", "Rest Twist (°)", 0f, -180f, 180f, true, true, valueFormat: "F0");
            _restRollSlider = _restGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Roll", "Rest Roll (°)", 0f, -180f, 180f, true, true, valueFormat: "F0");
            _restPitchSlider = _restGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset0Pitch", "Rest Pitch (°)", 0f, -180f, 180f, true, true, valueFormat: "F0");

            var compensationSectionGroup = new UIGroup(_drivenContentGroup);
            compensationSectionGroup.CreateButton("Displacement Weighting on Position", ToggleCompensationVisibility, new Color(0.3f, 0.3f, 0.3f), Color.white);
            _compensationGroup = new UIGroup(compensationSectionGroup);
            _referencePointEmptyTitle = _compensationGroup.CreateDisabledButton("Reference Point Empty", new Color(1.0f, 1.0f, 1.0f, 0.075f), Color.white);
            _referencePointEmptyChooser = _compensationGroup.CreatePopup($"MotionSource:{_keyPrefix}:ReferencePointEmpty", "Select Reference Point Empty", null, null, ReferencePointEmptyChooserCallback);
            _findOrCreateReferencePointButton = _compensationGroup.CreateButton("Find/Rebuild Reference Point Empty", FindOrCreateReferencePointEmpty, new Color(0f, 0.75f, 1f) * 0.8f, Color.white);
            _referencePointUpOffsetSlider = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:ReferencePointUpOffsetCm", "Reference Point Up Offset (cm)", 0f, -50f, 50f, OnReferencePointUpOffsetChanged, true, true, valueFormat: "F0");
            _resetReferencePointButton = _compensationGroup.CreateButton("Reset Reference Point", ResetReferencePointToDefault, new Color(0f, 0.75f, 1f) * 0.8f, Color.white);
            _compensationEnabled = _compensationGroup.CreateToggle($"MotionSource:{_keyPrefix}:Offset1Enabled", "Compensation Enabled", true);
            _compensationUpFactor = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1UpFactor", "Compensation Up Factor (%)", 0.4f, 0f, 1f, true, true, valueFormat: "P0");
            _compensationRightFactor = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RightFactor", "Compensation Right Factor (%)", 0.02f, 0f, 1f, true, true, valueFormat: "P0");
            _compensationForwardFactor = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1FwdFactor", "Compensation Forward Factor (%)", 0.02f, 0f, 1f, true, true, valueFormat: "P0");
            _compensationUpLimit = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1UpLimit", "Compensation Up Limit (%)", 0.3f, 0f, 2f, true, true, valueFormat: "P0");
            _compensationRightLimit = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RightLimit", "Compensation Right Limit (%)", 0.1f, 0f, 2f, true, true, valueFormat: "P0");
            _compensationForwardLimit = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1FwdLimit", "Compensation Forward Limit (%)", 0.1f, 0f, 2f, true, true, valueFormat: "P0");
            _compensationTwistFactor = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1YawFactor", "Compensation Twist Factor (%)", 0.2f, 0f, 1f, true, true, valueFormat: "P0");
            _compensationRollFactor = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RollFactor", "Compensation Roll Factor (%)", 0.3f, 0f, 1f, true, true, valueFormat: "P0");
            _compensationPitchFactor = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1PitchFactor", "Compensation Pitch Factor (%)", 0.2f, 0f, 1f, true, true, valueFormat: "P0");
            _compensationTwistLimit = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1YawLimit", "Compensation Twist Limit (°)", 30f, 0f, 180f, true, true, valueFormat: "F0");
            _compensationRollLimit = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1RollLimit", "Compensation Roll Limit (°)", 50f, 0f, 180f, true, true, valueFormat: "F0");
            _compensationPitchLimit = _compensationGroup.CreateSlider($"MotionSource:{_keyPrefix}:Offset1PitchLimit", "Compensation Pitch Limit (°)", 30f, 0f, 180f, true, true, valueFormat: "F0");

            var followSectionGroup = new UIGroup(_drivenContentGroup);
            followSectionGroup.CreateButton("Velocity Effected to Dildo", ToggleFollowVisibility, new Color(0.3f, 0.3f, 0.3f), Color.white);
            _followGroup = new UIGroup(followSectionGroup);
            _velocityFollowEnabled = _followGroup.CreateToggle($"MotionSource:{_keyPrefix}:UseVelocityFollow", "Use Velocity Follow", true, OnVelocityFollowChanged);
            if (UsesEngineHoldSpring)
                _speedFilterEnabled = _followGroup.CreateToggle($"MotionSource:{_keyPrefix}:UseSpeedFilter", "Use Speed Filter", false, OnSpeedFilterChanged);
            _followUpFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowUp", "Follow Up (%)", 0.2f, 0f, 1f, true, true, valueFormat: "P0");
            _followRightFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowRight", "Follow Right (%)", 0.8f, 0f, 1f, true, true, valueFormat: "P0");
            _followForwardFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowForward", "Follow Forward (%)", 0.8f, 0f, 1f, true, true, valueFormat: "P0");
            _followTwistFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowYaw", "Follow Twist (%)", 0.3f, 0f, 1f, true, true, valueFormat: "P0");
            _followRollFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowRoll", "Follow Roll (%)", 0.3f, 0f, 1f, true, true, valueFormat: "P0");
            _followPitchFactor = _followGroup.CreateSlider($"MotionSource:{_keyPrefix}:FollowPitch", "Follow Pitch (%)", 0.3f, 0f, 1f, true, true, valueFormat: "P0");

            var springSectionGroup = new UIGroup(_drivenContentGroup);
            springSectionGroup.CreateButton("Spring", ToggleSpringVisibility, new Color(0.3f, 0.3f, 0.3f), Color.white);
            _springGroup = new UIGroup(springSectionGroup);
            _springUp = CreateSpringSlider(_springGroup, $"MotionSource:{_keyPrefix}:SpringUpK", "Spring Up", UsesPercentSpringUnits ? 0.3f : 30f);
            _springRight = CreateSpringSlider(_springGroup, $"MotionSource:{_keyPrefix}:SpringRightK", "Spring Right", UsesPercentSpringUnits ? 1f : 100f);
            _springForward = CreateSpringSlider(_springGroup, $"MotionSource:{_keyPrefix}:SpringFwdK", "Spring Forward", UsesPercentSpringUnits ? 1f : 100f);
            _springTwist = CreateSpringSlider(_springGroup, $"MotionSource:{_keyPrefix}:SpringYawK", "Spring Twist", UsesPercentSpringUnits ? 0.3f : 30f);
            _springRoll = CreateSpringSlider(_springGroup, $"MotionSource:{_keyPrefix}:SpringRollK", "Spring Roll", UsesPercentSpringUnits ? 0.3f : 30f);
            _springPitch = CreateSpringSlider(_springGroup, $"MotionSource:{_keyPrefix}:SpringPitchK", "Spring Pitch", UsesPercentSpringUnits ? 0.3f : 30f);

            var damperSectionGroup = new UIGroup(_drivenContentGroup);
            damperSectionGroup.CreateButton("Damper", ToggleDamperVisibility, new Color(0.3f, 0.3f, 0.3f), Color.white);
            _damperGroup = new UIGroup(damperSectionGroup);
            _damperGroup.CreateDisabledButton("Velocity Damping", new Color(1.0f, 1.0f, 1.0f, 0.075f), Color.white);
            _velocityDampingUp = CreateVelocityDampingSlider(_damperGroup, $"MotionSource:{_keyPrefix}:VelocityDampingUp", "Up", 20f);
            _velocityDampingRight = CreateVelocityDampingSlider(_damperGroup, $"MotionSource:{_keyPrefix}:VelocityDampingRight", "Right", 8f);
            _velocityDampingForward = CreateVelocityDampingSlider(_damperGroup, $"MotionSource:{_keyPrefix}:VelocityDampingForward", "Forward", 8f);
            _velocityDampingTwist = CreateVelocityDampingSlider(_damperGroup, $"MotionSource:{_keyPrefix}:VelocityDampingTwist", "Twist", 8f);
            _velocityDampingRoll = CreateVelocityDampingSlider(_damperGroup, $"MotionSource:{_keyPrefix}:VelocityDampingRoll", "Roll", 8f);
            _velocityDampingPitch = CreateVelocityDampingSlider(_damperGroup, $"MotionSource:{_keyPrefix}:VelocityDampingPitch", "Pitch", 8f);
            _damperGroup.CreateSpacer(8f);
            _damperGroup.CreateDisabledButton("Target Smoothing", new Color(1.0f, 1.0f, 1.0f, 0.075f), Color.white);
            _targetSmoothingUp = CreateSmoothingTimeSlider(_damperGroup, $"MotionSource:{_keyPrefix}:TargetSmoothingUp", "Up", LegacyDamperPercentToSmoothingTime(0.2f));
            _targetSmoothingRight = CreateSmoothingTimeSlider(_damperGroup, $"MotionSource:{_keyPrefix}:TargetSmoothingRight", "Right", LegacyDamperPercentToSmoothingTime(0.08f));
            _targetSmoothingForward = CreateSmoothingTimeSlider(_damperGroup, $"MotionSource:{_keyPrefix}:TargetSmoothingForward", "Forward", LegacyDamperPercentToSmoothingTime(0.08f));
            _targetSmoothingTwist = CreateSmoothingTimeSlider(_damperGroup, $"MotionSource:{_keyPrefix}:TargetSmoothingTwist", "Twist", LegacyDamperPercentToSmoothingTime(0.08f));
            _targetSmoothingRoll = CreateSmoothingTimeSlider(_damperGroup, $"MotionSource:{_keyPrefix}:TargetSmoothingRoll", "Roll", LegacyDamperPercentToSmoothingTime(0.08f));
            _targetSmoothingPitch = CreateSmoothingTimeSlider(_damperGroup, $"MotionSource:{_keyPrefix}:TargetSmoothingPitch", "Pitch", LegacyDamperPercentToSmoothingTime(0.08f));

            RefreshVisibility();
            RefreshReferencePointEmptyChoices();
        }

        public void DestroyUI(IUIBuilder builder)
        {
            if (_enabled != null && _enabled.val)
                _enabled.val = false;
            else
                RestoreAtomState();

            _drivenGroup?.Destroy();

            builder.Destroy(_drivenTitle);

            _drivenGroup = null;
            _drivenContentGroup = null;
            _restGroup = null;
            _compensationGroup = null;
            _followGroup = null;
            _springGroup = null;
            _damperGroup = null;
            _drivenTitle = null;
        }

        public void StoreConfig(JSONNode config)
        {
            _drivenGroup?.StoreConfig(config);
        }

        public void RestoreConfig(JSONNode config)
        {
            _drivenGroup?.RestoreConfig(config);

            MigrateLegacySpringValues();
            MigrateLegacyDamperValues(config);
            RefreshReferencePointEmptyChoices(_referencePointEmptyChooser != null ? _referencePointEmptyChooser.val : null);

            _initialized = false;
            _smoothedSpringTargetInitialized = false;
            ResetVelocityModeStates();
            RefreshVisibility();
        }

        public void Disable()
        {
            if (_enabled == null) return;
            if (_enabled.val)
                _enabled.val = false;
            else
                RestoreAtomState();
        }

        public void Update(IMotionSourceTarget target)
        {
            if (target == null) return;

            _targetPosition = target.Position;
            _targetUp = target.Up;
            _targetRight = target.Right;
            _targetForward = target.Forward;
            _currentPersonTarget = target as AbstractPersonTarget;
            if (_targetUp.sqrMagnitude < 1e-8f || _targetForward.sqrMagnitude < 1e-8f)
                return;

            var targetRotation = Quaternion.LookRotation(_targetForward, _targetUp);
            var personTarget = _currentPersonTarget;
            UpdateReferencePoint(target);

            if (!_initialized)
            {
                Initialize(targetRotation);
            }
            else
            {
                if (personTarget != null && personTarget.ConsumeSelectionChanged())
                {
                    ResetReferencePointToDefault(personTarget);
                    Initialize(targetRotation);
                }
                else
                {
                    float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.1f);
                    Vector3 tpDeltaPos = _targetPosition - _prevTargetPosition;
                    Quaternion tpDeltaRot = targetRotation * Quaternion.Inverse(_prevTargetRotation);
                    UpdatePositionAndRotation(targetRotation, tpDeltaPos, tpDeltaRot, dt);
                }
            }

            _prevTargetPosition = _targetPosition;
            _prevTargetRotation = targetRotation;

            if (Enabled)
            {
                var ctrl = _reference?.DrivenAtom?.mainController;
                if (ctrl != null) WriteToController(ctrl);
            }

            DrawDebug();
        }

        private void UpdatePositionAndRotation(Quaternion tpRot, Vector3 tpDeltaPos, Quaternion tpDeltaRot, float dt)
        {
            if (UsesEngineHoldSpring)
            {
                UpdateControllerTarget(tpRot, dt);
                return;
            }

            float length = ReferenceLength;
            Vector3 springFramePosition = _targetPosition;
            Quaternion springFrameRotation = tpRot;
            // Compensation must be a feed-forward function of the person's pose only (target vs. reference
            // anchor), NOT of the dildo's current pose. Otherwise yellow follows the dildo and creates a
            // feedback loop with Spring/Damper tuning.
            Vector3 referencePositionForCompensation = _targetPosition;
            Quaternion referenceRotationForCompensation = tpRot;

            Quaternion prevTpRot = _prevTargetRotation;
            Vector3 tpLocalDelta = Quaternion.Inverse(prevTpRot) * tpDeltaPos;
            Vector3 tpLocalEuler = SignedEuler(Quaternion.Inverse(prevTpRot) * tpDeltaRot * prevTpRot);

            bool useVelocityFollow = IsVelocityFollowEnabled;
            Vector3 followTranslationLocal = useVelocityFollow
                ? new Vector3(
                    tpLocalDelta.x * _followRightFactor.val,
                    tpLocalDelta.y * _followUpFactor.val,
                    tpLocalDelta.z * _followForwardFactor.val
                )
                : Vector3.zero;
            Vector3 followTranslationWorld = prevTpRot * followTranslationLocal;

            Quaternion followRotationLocal = useVelocityFollow
                ? Quaternion.Euler(
                    tpLocalEuler.x * _followPitchFactor.val,
                    tpLocalEuler.y * _followTwistFactor.val,
                    tpLocalEuler.z * _followRollFactor.val
                )
                : Quaternion.identity;
            Quaternion followRotationWorld = prevTpRot * followRotationLocal * Quaternion.Inverse(prevTpRot);

            Vector3 referenceToPreviousTarget = _referencePosition - _prevTargetPosition;
            _referencePosition = _prevTargetPosition + tpDeltaRot * referenceToPreviousTarget;
            _referencePosition += followTranslationWorld;
            _referenceRotation = NormalizeQuaternion(followRotationWorld * _referenceRotation);

            Vector3 restLocalPosition = GetRestLocalPosition(length);
            Vector3 restLocalEuler = GetRestLocalEuler();
            Quaternion restLocalRotation = Quaternion.Euler(restLocalEuler);

            Vector3 springTargetLocalPosition = restLocalPosition;
            Vector3 springTargetLocalEuler = restLocalEuler;

            if (_compensationEnabled != null && _compensationEnabled.val)
            {
                Vector3 deviationWorld = referencePositionForCompensation - _referencePointPosition;
                Vector3 deviationLocal = Quaternion.Inverse(_referencePointRotation) * deviationWorld;

                float compensationUp = Mathf.Clamp(deviationLocal.y * _compensationUpFactor.val,
                    -_compensationUpLimit.val * length, _compensationUpLimit.val * length);
                float compensationRight = Mathf.Clamp(deviationLocal.x * _compensationRightFactor.val,
                    -_compensationRightLimit.val * length, _compensationRightLimit.val * length);
                float compensationForward = Mathf.Clamp(deviationLocal.z * _compensationForwardFactor.val,
                    -_compensationForwardLimit.val * length, _compensationForwardLimit.val * length);

                springTargetLocalPosition -= new Vector3(compensationRight, compensationUp, compensationForward);

                Quaternion rotationDeviationLocal = Quaternion.Inverse(_referencePointRotation) * referenceRotationForCompensation * Quaternion.Inverse(restLocalRotation);
                Vector3 rotationDeviationEuler = SignedEuler(rotationDeviationLocal);

                float compensationPitch = Mathf.Clamp(rotationDeviationEuler.x * _compensationPitchFactor.val,
                    -_compensationPitchLimit.val, _compensationPitchLimit.val);
                float compensationTwist = Mathf.Clamp(rotationDeviationEuler.y * _compensationTwistFactor.val,
                    -_compensationTwistLimit.val, _compensationTwistLimit.val);
                float compensationRoll = Mathf.Clamp(rotationDeviationEuler.z * _compensationRollFactor.val,
                    -_compensationRollLimit.val, _compensationRollLimit.val);

                springTargetLocalEuler = new Vector3(
                    _restPitchSlider.val - compensationPitch,
                    _restTwistSlider.val - compensationTwist,
                    _restRollSlider.val - compensationRoll
                );
            }

            _debugOffset0TargetPosition = springFramePosition + springFrameRotation * restLocalPosition;
            _debugOffset0TargetRotation = springFrameRotation * restLocalRotation;

            springTargetLocalPosition = SmoothSpringTargetPosition(springTargetLocalPosition, dt);
            springTargetLocalEuler = SmoothSpringTargetEuler(springTargetLocalEuler, dt);

            Vector3 springTargetWorldPosition = springFramePosition + springFrameRotation * springTargetLocalPosition;
            Quaternion springTargetWorldRotation = springFrameRotation * Quaternion.Euler(springTargetLocalEuler);
            _debugOffset01TargetPosition = springTargetWorldPosition;
            _debugOffset01TargetRotation = springTargetWorldRotation;

            Vector3 positionErrorWorld = springTargetWorldPosition - _referencePosition;
            Vector3 positionErrorLocal = Quaternion.Inverse(springFrameRotation) * positionErrorWorld;
            Vector3 referenceLocalVelocity = Quaternion.Inverse(springFrameRotation) * _vrLinearVelocity;

            Vector3 springForceLocal = new Vector3(
                positionErrorLocal.x * GetSpringValue(_springRight) - referenceLocalVelocity.x * GetVelocityDampingValue(_velocityDampingRight),
                positionErrorLocal.y * GetSpringValue(_springUp) - referenceLocalVelocity.y * GetVelocityDampingValue(_velocityDampingUp),
                positionErrorLocal.z * GetSpringValue(_springForward) - referenceLocalVelocity.z * GetVelocityDampingValue(_velocityDampingForward)
            );
            _vrLinearVelocity += springFrameRotation * springForceLocal * dt;
            _referencePosition += _vrLinearVelocity * dt;

            Quaternion rotationError = springTargetWorldRotation * Quaternion.Inverse(_referenceRotation);
            Vector3 rotationErrorLocal = SignedEuler(Quaternion.Inverse(springFrameRotation) * rotationError * springFrameRotation);

            Vector3 springTorque = new Vector3(
                rotationErrorLocal.x * GetSpringValue(_springPitch) - _vrAngularVelocity.x * GetVelocityDampingValue(_velocityDampingPitch),
                rotationErrorLocal.y * GetSpringValue(_springTwist) - _vrAngularVelocity.y * GetVelocityDampingValue(_velocityDampingTwist),
                rotationErrorLocal.z * GetSpringValue(_springRoll) - _vrAngularVelocity.z * GetVelocityDampingValue(_velocityDampingRoll)
            );
            _vrAngularVelocity += springTorque * dt;

            Quaternion angularVelocityRotation = Quaternion.Euler(_vrAngularVelocity * dt);
            Quaternion angularVelocityWorldRotation = springFrameRotation * angularVelocityRotation * Quaternion.Inverse(springFrameRotation);
            _referenceRotation = NormalizeQuaternion(angularVelocityWorldRotation * _referenceRotation);
        }

        private void Initialize(Quaternion targetRotation)
        {
            if (UsesEngineHoldSpring)
            {
                InitializeControllerTarget(targetRotation);
                return;
            }

            float length = ReferenceLength;
            Vector3 restLocalPosition = GetRestLocalPosition(length);
            Vector3 restLocalEuler = GetRestLocalEuler();
            Quaternion restLocalRotation = Quaternion.Euler(restLocalEuler);

            _referencePosition = _targetPosition + targetRotation * restLocalPosition;
            _referenceRotation = targetRotation * restLocalRotation;
            _debugOffset0TargetPosition = _referencePosition;
            _debugOffset0TargetRotation = _referenceRotation;
            _debugOffset01TargetPosition = _referencePosition;
            _debugOffset01TargetRotation = _referenceRotation;
            _vrLinearVelocity = Vector3.zero;
            _vrAngularVelocity = Vector3.zero;
            _smoothedSpringTargetLocalPosition = restLocalPosition;
            _smoothedSpringTargetLocalEuler = restLocalEuler;
            _smoothedSpringTargetInitialized = true;

            _prevTargetPosition = _targetPosition;
            _prevTargetRotation = targetRotation;

            _initialized = true;
        }

        private void WriteToController(FreeControllerV3 ctrl)
        {
            var targetPosition = UsesEngineHoldSpring ? _controllerTargetPosition : _referencePosition;
            var targetRotation = UsesEngineHoldSpring ? _controllerTargetRotation : _referenceRotation;
            var controllerRotation = targetRotation;

            if (UsesEngineHoldSpring)
            {
                if (ctrl.currentPositionState != FreeControllerV3.PositionState.On)
                    ctrl.currentPositionState = FreeControllerV3.PositionState.On;
                if (ctrl.currentRotationState != FreeControllerV3.RotationState.On)
                    ctrl.currentRotationState = FreeControllerV3.RotationState.On;
                ApplyGravityOverride(true);

                if (ctrl.control != null)
                {
                    ctrl.control.position = targetPosition;
                    ctrl.control.rotation = controllerRotation;
                }

                ctrl.onPositionChangeHandlers?.Invoke(ctrl);
                return;
            }

            ctrl.transform.position = targetPosition;
            ctrl.transform.rotation = controllerRotation;
        }

        private Quaternion GetControllerRestOffset()
        {
            var kind = _reference != null ? _reference.DrivenKind : null;
            if (kind == DrivenReferenceKind.Dildo || kind == DrivenReferenceKind.CustomUnityAsset)
                return Quaternion.Euler(-90f, 0f, 0f);
            return Quaternion.identity;
        }

        private void DrawDebug()
        {
            if (!DebugDraw.Enabled)
                return;

            DebugDraw.DrawTransform(_targetPosition, _targetUp, _targetRight, _targetForward, 0.12f);

            // Final attracted position: Qp for Dildo/CUA, physics position for Empty
            var finalDebugPos = UsesEngineHoldSpring ? _debugFinalTargetPosition : _referencePosition;
            var finalDebugRot = UsesEngineHoldSpring ? _debugFinalTargetRotation : _referenceRotation;
            DebugDraw.DrawLine(finalDebugPos, _targetPosition, Color.white);

            var referencePointUp = _referencePointRotation * Vector3.up;
            var referencePointRight = _referencePointRotation * Vector3.right;
            var referencePointForward = _referencePointRotation * Vector3.forward;
            DebugDraw.DrawPoint(_referencePointPosition, Color.cyan, 0.012f);
            DebugDraw.DrawTransform(_referencePointPosition, referencePointUp, referencePointRight, referencePointForward, 0.06f);

            // Red: rest offset only (no compensation)
            DebugDraw.DrawPoint(_debugOffset0TargetPosition, Color.red, 0.01f);
            DebugDraw.DrawTransform(_debugOffset0TargetPosition, _debugOffset0TargetRotation * Vector3.up, _debugOffset0TargetRotation * Vector3.right, _debugOffset0TargetRotation * Vector3.forward, 0.08f);

            // Yellow: rest + displacement compensation
            DebugDraw.DrawPoint(_debugOffset01TargetPosition, Color.yellow, 0.01f);
            DebugDraw.DrawTransform(_debugOffset01TargetPosition, _debugOffset01TargetRotation * Vector3.up, _debugOffset01TargetRotation * Vector3.right, _debugOffset01TargetRotation * Vector3.forward, 0.1f);

            // Green: final attracted position
            DebugDraw.DrawPoint(finalDebugPos, Color.green, 0.012f);
            DebugDraw.DrawTransform(finalDebugPos, finalDebugRot * Vector3.up, finalDebugRot * Vector3.right, finalDebugRot * Vector3.forward, 0.12f);
        }

        private void UpdateReferencePoint(IMotionSourceTarget target)
        {
            var personTarget = target as AbstractPersonTarget;
            if (personTarget == null)
            {
                _referencePointRotation = Quaternion.LookRotation(_targetForward, _targetUp);
                _referencePointPosition = _targetPosition;
                return;
            }

            var defaultRotation = GetDefaultReferencePointRotation(personTarget);
            var defaultPosition = GetDefaultReferencePointPosition(personTarget, defaultRotation);

            if (_referencePointAnchor != null && _referencePointAnchor.EnsureExists(personTarget, defaultPosition, defaultRotation))
            {
                _referencePointRotation = _referencePointAnchor.Rotation;
                _referencePointPosition = _referencePointAnchor.Position;
                return;
            }

            _referencePointRotation = defaultRotation;
            _referencePointPosition = defaultPosition;
        }

        private void ResetReferencePointToDefault()
        {
            ResetReferencePointToDefault(_currentPersonTarget);
        }

        private void ResetReferencePointToDefault(AbstractPersonTarget personTarget)
        {
            if (personTarget == null || _referencePointAnchor == null)
                return;

            var defaultRotation = GetDefaultReferencePointRotation(personTarget);
            var defaultPosition = GetDefaultReferencePointPosition(personTarget, defaultRotation);
            _referencePointAnchor.ResetToDefault(personTarget, defaultPosition, defaultRotation);
            RefreshReferencePointEmptyChoices(_referencePointAnchor.SelectedAtomUid);
            _referencePointRotation = defaultRotation;
            _referencePointPosition = defaultPosition;
        }

        private void FindOrCreateReferencePointEmpty()
        {
            var personTarget = _currentPersonTarget;
            if (personTarget == null || _referencePointAnchor == null)
                return;

            var defaultRotation = GetDefaultReferencePointRotation(personTarget);
            var defaultPosition = GetDefaultReferencePointPosition(personTarget, defaultRotation);
            _referencePointAnchor.FindExistingOrCreateDefault(personTarget, defaultPosition, defaultRotation);
            RefreshReferencePointEmptyChoices(_referencePointAnchor.SelectedAtomUid);
        }

        private void ReferencePointEmptyChooserCallback(string atomUid)
        {
            if (_referencePointAnchor == null)
                return;

            _referencePointAnchor.SelectAtomUid(atomUid);
            if (_referencePointEmptyChooser != null)
                _referencePointEmptyChooser.valNoCallback = _referencePointAnchor.SelectedAtomUid;
        }

        private void RefreshReferencePointEmptyChoices(string preferredUid = null)
        {
            if (_referencePointEmptyChooser == null)
                return;

            var emptyUids = SuperController.singleton.GetAtoms()
                .Where(atom => atom.type == "Empty")
                .Select(atom => atom.uid)
                .ToList();

            var desiredUid = preferredUid;
            if (string.IsNullOrEmpty(desiredUid) || !emptyUids.Contains(desiredUid))
                desiredUid = _referencePointAnchor != null ? _referencePointAnchor.SelectedAtomUid : null;
            if (string.IsNullOrEmpty(desiredUid) || desiredUid == "None" || !emptyUids.Contains(desiredUid))
                desiredUid = emptyUids.Contains(_referencePointAnchor.DefaultAtomUid) ? _referencePointAnchor.DefaultAtomUid : emptyUids.FirstOrDefault();
            if (string.IsNullOrEmpty(desiredUid))
                desiredUid = "None";

            emptyUids.Insert(0, "None");
            _referencePointEmptyChooser.choices = emptyUids;
            ReferencePointEmptyChooserCallback(desiredUid);
        }

        private void OnReferencePointUpOffsetChanged(float _)
        {
            if (_currentPersonTarget == null)
                return;

            ResetReferencePointToDefault(_currentPersonTarget);
        }

        private Quaternion GetDefaultReferencePointRotation(AbstractPersonTarget personTarget)
        {
            return personTarget.RootRotation;
        }

        private Vector3 GetDefaultReferencePointPosition(AbstractPersonTarget personTarget, Quaternion referencePointRotation)
        {
            float referenceHeight = personTarget.BaseHeightMeters * personTarget.SizeScale * 0.48f;
            float userOffsetMeters = (_referencePointUpOffsetSlider?.val ?? 0f) / 100f;
            return personTarget.RootPosition + (referencePointRotation * Vector3.up) * (referenceHeight + userOffsetMeters);
        }

        private Vector3 GetRestLocalPosition(float length)
        {
            return new Vector3(
                length * (_restRightSlider?.val ?? 0f),
                length * GetRestUpFactor(),
                length * (_restForwardSlider?.val ?? 0f)
            );
        }

        private float GetRestUpFactor()
        {
            float insertion = _restUpSlider?.val ?? 0.5f;
            return insertion - 1f;
        }

        private Vector3 GetRestLocalEuler()
        {
            return new Vector3(
                _restPitchSlider?.val ?? 0f,
                _restTwistSlider?.val ?? 0f,
                _restRollSlider?.val ?? 0f
            );
        }

        private JSONStorableFloat CreateSpringSlider(UIGroup group, string key, string label, float defaultValue)
        {
            if (UsesPercentSpringUnits)
                return group.CreateSlider(key, label + " (%)", defaultValue, 0f, 2f, true, true, valueFormat: "P0");

            bool angular = label.Contains("Twist") || label.Contains("Roll") || label.Contains("Pitch");
            return group.CreateSlider(key, angular ? label + " (deg/s²)" : label + " (m/s²)", defaultValue, 0f, 200f, true, true, valueFormat: "F1");
        }

        private JSONStorableFloat CreateVelocityDampingSlider(UIGroup group, string key, string label, float defaultValue)
        {
            return group.CreateSlider(key, label + " (1/s)", defaultValue, 0f, 200f, true, true, valueFormat: "F1");
        }

        private JSONStorableFloat CreateSmoothingTimeSlider(UIGroup group, string key, string label, float defaultValue)
        {
            return group.CreateSlider(key, label + " (s)", defaultValue, 0f, 1f, true, true, valueFormat: "F3");
        }

        private float GetSpringValue(JSONStorableFloat slider)
        {
            if (slider == null)
                return 0f;
            return UsesPercentSpringUnits ? slider.val * 100f : slider.val;
        }

        private static float GetBlendValue(JSONStorableFloat slider)
        {
            return slider != null ? slider.val : 0f;
        }

        private float GetVelocityDampingValue(JSONStorableFloat slider)
        {
            if (slider == null)
                return 0f;
            return slider.val;
        }

        private Vector3 SmoothSpringTargetPosition(Vector3 target, float dt)
        {
            if (!_smoothedSpringTargetInitialized)
            {
                _smoothedSpringTargetLocalPosition = target;
                _smoothedSpringTargetInitialized = true;
                return target;
            }

            _smoothedSpringTargetLocalPosition = new Vector3(
                SmoothScalar(_smoothedSpringTargetLocalPosition.x, target.x, _targetSmoothingRight, dt, false),
                SmoothScalar(_smoothedSpringTargetLocalPosition.y, target.y, _targetSmoothingUp, dt, false),
                SmoothScalar(_smoothedSpringTargetLocalPosition.z, target.z, _targetSmoothingForward, dt, false)
            );
            return _smoothedSpringTargetLocalPosition;
        }

        private Vector3 SmoothSpringTargetEuler(Vector3 target, float dt)
        {
            if (!_smoothedSpringTargetInitialized)
            {
                _smoothedSpringTargetLocalEuler = target;
                _smoothedSpringTargetInitialized = true;
                return target;
            }

            _smoothedSpringTargetLocalEuler = new Vector3(
                SmoothScalar(_smoothedSpringTargetLocalEuler.x, target.x, _targetSmoothingPitch, dt, true),
                SmoothScalar(_smoothedSpringTargetLocalEuler.y, target.y, _targetSmoothingTwist, dt, true),
                SmoothScalar(_smoothedSpringTargetLocalEuler.z, target.z, _targetSmoothingRoll, dt, true)
            );
            return _smoothedSpringTargetLocalEuler;
        }

        private static float SmoothScalar(float current, float target, JSONStorableFloat slider, float dt, bool angle)
        {
            float timeConstant = slider != null ? Mathf.Max(0f, slider.val) : 0f;
            if (timeConstant <= 0.0001f)
                return target;

            float alpha = 1f - Mathf.Exp(-dt / timeConstant);
            return angle ? Mathf.LerpAngle(current, target, alpha) : Mathf.Lerp(current, target, alpha);
        }

        private static float LerpAngleUnclamped(float a, float b, float t)
        {
            return a + Mathf.DeltaAngle(a, b) * t;
        }

        private void InitializeControllerTarget(Quaternion targetRotation)
        {
            Vector3 qePosition;
            Quaternion qeRotation;
            if (!TryGetReferencePose(out qePosition, out qeRotation))
            {
                float length = ReferenceLength;
                Vector3 restLocalPosition = GetRestLocalPosition(length);
                Vector3 restLocalEuler = GetRestLocalEuler();
                qePosition = _targetPosition + targetRotation * restLocalPosition;
                qeRotation = targetRotation * Quaternion.Euler(restLocalEuler);
            }

            Vector3 qeControllerPosition;
            Quaternion qeControllerRotation;
            if (TryGetDrivenControllerPose(out qeControllerPosition, out qeControllerRotation))
                _referenceToControllerLocalPosition = Quaternion.Inverse(qeRotation) * (qeControllerPosition - qePosition);
            else
                _referenceToControllerLocalPosition = Vector3.zero;

            _referencePosition = qePosition;
            _referenceRotation = qeRotation;
            RefreshReferenceToControllerOffset(qePosition, qeRotation);
            _vrLinearVelocity = Vector3.zero;
            _vrAngularVelocity = Vector3.zero;

            UpdateControllerTarget(targetRotation, 0f);

            _prevTargetPosition = _targetPosition;
            _prevTargetRotation = targetRotation;
            _initialized = true;
        }

        private void UpdateControllerTarget(Quaternion targetRotation, float dt)
        {
            Vector3 qePosition;
            Quaternion qeRotation;
            if (!TryGetReferencePose(out qePosition, out qeRotation))
                return;

            _referencePosition = qePosition;
            _referenceRotation = qeRotation;

            float length = ReferenceLength;
            Vector3 restLocalPosition = GetRestLocalPosition(length);
            Vector3 restLocalEuler = GetRestLocalEuler();
            Quaternion restLocalRotation = Quaternion.Euler(restLocalEuler);

            Vector3 targetLocalPosition = restLocalPosition;
            Vector3 targetLocalEuler = restLocalEuler;

            if (_compensationEnabled != null && _compensationEnabled.val)
            {
                // Feed-forward compensation from the person's pose only — independent of dildo state.
                Vector3 deviationWorld = _targetPosition - _referencePointPosition;
                Vector3 deviationLocal = Quaternion.Inverse(_referencePointRotation) * deviationWorld;

                float compensationUp = Mathf.Clamp(deviationLocal.y * _compensationUpFactor.val,
                    -_compensationUpLimit.val * length, _compensationUpLimit.val * length);
                float compensationRight = Mathf.Clamp(deviationLocal.x * _compensationRightFactor.val,
                    -_compensationRightLimit.val * length, _compensationRightLimit.val * length);
                float compensationForward = Mathf.Clamp(deviationLocal.z * _compensationForwardFactor.val,
                    -_compensationForwardLimit.val * length, _compensationForwardLimit.val * length);

                targetLocalPosition -= new Vector3(compensationRight, compensationUp, compensationForward);

                Quaternion rotationDeviationLocal = Quaternion.Inverse(_referencePointRotation) * targetRotation * Quaternion.Inverse(restLocalRotation);
                Vector3 rotationDeviationEuler = SignedEuler(rotationDeviationLocal);

                float compensationPitch = Mathf.Clamp(rotationDeviationEuler.x * _compensationPitchFactor.val,
                    -_compensationPitchLimit.val, _compensationPitchLimit.val);
                float compensationTwist = Mathf.Clamp(rotationDeviationEuler.y * _compensationTwistFactor.val,
                    -_compensationTwistLimit.val, _compensationTwistLimit.val);
                float compensationRoll = Mathf.Clamp(rotationDeviationEuler.z * _compensationRollFactor.val,
                    -_compensationRollLimit.val, _compensationRollLimit.val);

                targetLocalEuler = new Vector3(
                    _restPitchSlider.val - compensationPitch,
                    _restTwistSlider.val - compensationTwist,
                    _restRollSlider.val - compensationRoll
                );
            }

            // Smooth ONLY the target (rest+compensation). Smoothing must not be applied to the qe term;
            // otherwise at Spring=0 the resulting qpLocal lags behind the moving person frame and the engine
            // hold spring drags the dildo along with the person (apparent constant-velocity drift).
            targetLocalPosition = SmoothSpringTargetPosition(targetLocalPosition, dt);
            targetLocalEuler = SmoothSpringTargetEuler(targetLocalEuler, dt);

            var targetReferenceRotation = NormalizeQuaternion(targetRotation * Quaternion.Euler(targetLocalEuler));

            _debugOffset0TargetPosition = _targetPosition + targetRotation * restLocalPosition;
            _debugOffset0TargetRotation = targetRotation * restLocalRotation;
            // Yellow: rest + compensation target in dildo/reference space.
            _debugOffset01TargetPosition = _targetPosition + targetRotation * targetLocalPosition;
            _debugOffset01TargetRotation = targetReferenceRotation;

            Vector3 qeLocalPosition = Quaternion.Inverse(targetRotation) * (qePosition - _targetPosition);
            Vector3 qeLocalEuler = SignedEuler(Quaternion.Inverse(targetRotation) * qeRotation);

            Vector3 rawSpringTargetLocalPosition = new Vector3(
                Mathf.LerpUnclamped(qeLocalPosition.x, targetLocalPosition.x, GetBlendValue(_springRight)),
                Mathf.LerpUnclamped(qeLocalPosition.y, targetLocalPosition.y, GetBlendValue(_springUp)),
                Mathf.LerpUnclamped(qeLocalPosition.z, targetLocalPosition.z, GetBlendValue(_springForward))
            );
            Vector3 qpLocalEuler = new Vector3(
                LerpAngleUnclamped(qeLocalEuler.x, targetLocalEuler.x, GetBlendValue(_springPitch)),
                LerpAngleUnclamped(qeLocalEuler.y, targetLocalEuler.y, GetBlendValue(_springTwist)),
                LerpAngleUnclamped(qeLocalEuler.z, targetLocalEuler.z, GetBlendValue(_springRoll))
            );

            Vector3 qpLocalPosition = _speedFilterEnabled != null && _speedFilterEnabled.val
                ? ApplySpeedFilterToSpringTarget(rawSpringTargetLocalPosition, length, dt)
                : rawSpringTargetLocalPosition;

            _debugFinalTargetPosition = _targetPosition + targetRotation * qpLocalPosition;
            _debugFinalTargetRotation = NormalizeQuaternion(targetRotation * Quaternion.Euler(qpLocalEuler));
            _controllerTargetPosition = _debugFinalTargetPosition + _debugFinalTargetRotation * _referenceToControllerLocalPosition;
            _controllerTargetRotation = NormalizeQuaternion(_debugFinalTargetRotation * GetControllerRestOffset());
        }

        private void RefreshReferenceToControllerOffset(Vector3 referencePosition, Quaternion referenceRotation)
        {
            Vector3 controllerPosition;
            Quaternion controllerRotation;
            if (TryGetDrivenControllerPose(out controllerPosition, out controllerRotation))
                _referenceToControllerLocalPosition = Quaternion.Inverse(referenceRotation) * (controllerPosition - referencePosition);
        }

        private Vector3 ApplySpeedFilterToSpringTarget(Vector3 rawSpringTargetLocalPosition, float length, float dt)
        {
            if (dt <= 1e-6f)
                return rawSpringTargetLocalPosition;

            if (!_filteredSpringTargetInitialized)
            {
                _filteredSpringTargetLocalPosition = rawSpringTargetLocalPosition;
                _prevRawSpringTargetLocalPosition = rawSpringTargetLocalPosition;
                _filteredSpringTargetInitialized = true;
                return rawSpringTargetLocalPosition;
            }

            Vector3 filteredSpringTargetLocalPosition = new Vector3(
                FilterSpringTargetAxis(_filteredSpringTargetLocalPosition.x, _prevRawSpringTargetLocalPosition.x, rawSpringTargetLocalPosition.x, -length, length),
                FilterSpringTargetAxis(_filteredSpringTargetLocalPosition.y, _prevRawSpringTargetLocalPosition.y, rawSpringTargetLocalPosition.y, -length, 0f),
                FilterSpringTargetAxis(_filteredSpringTargetLocalPosition.z, _prevRawSpringTargetLocalPosition.z, rawSpringTargetLocalPosition.z, -length, length)
            );

            _filteredSpringTargetLocalPosition = filteredSpringTargetLocalPosition;
            _prevRawSpringTargetLocalPosition = rawSpringTargetLocalPosition;
            return _filteredSpringTargetLocalPosition;
        }

        private static float FilterSpringTargetAxis(float currentSpringTargetPosition, float previousRawSpringTargetPosition, float rawSpringTargetPosition, float minLimit, float maxLimit)
        {
            float transformedDelta = TransformRelativeDelta(previousRawSpringTargetPosition, rawSpringTargetPosition, minLimit, maxLimit);
            return Mathf.Clamp(currentSpringTargetPosition + transformedDelta, minLimit, maxLimit);
        }

        private static float TransformRelativeDelta(float previousRawSpringTargetPosition, float rawSpringTargetPosition, float minLimit, float maxLimit)
        {
            float delta = rawSpringTargetPosition - previousRawSpringTargetPosition;
            if (Mathf.Abs(delta) <= 1e-7f)
                return 0f;

            float center = (minLimit + maxLimit) * 0.5f;
            bool crossesCenter = (previousRawSpringTargetPosition - center) * (rawSpringTargetPosition - center) < 0f;
            if (!crossesCenter)
                return TransformRelativeDeltaSegment(previousRawSpringTargetPosition, rawSpringTargetPosition, minLimit, maxLimit);

            return TransformRelativeDeltaSegment(previousRawSpringTargetPosition, center, minLimit, maxLimit)
                + TransformRelativeDeltaSegment(center, rawSpringTargetPosition, minLimit, maxLimit);
        }

        private static float TransformRelativeDeltaSegment(float fromPosition, float toPosition, float minLimit, float maxLimit)
        {
            float delta = toPosition - fromPosition;
            if (Mathf.Abs(delta) <= 1e-7f)
                return 0f;

            float fromLogMultiplier = ComputeScalarFieldLogMultiplier(ComputeSignedRe(fromPosition, minLimit, maxLimit));
            float toLogMultiplier = ComputeScalarFieldLogMultiplier(ComputeSignedRe(toPosition, minLimit, maxLimit));
            float logRatio = Mathf.Clamp(toLogMultiplier - fromLogMultiplier, -0.69314718f, 0.69314718f);
            return delta * Mathf.Exp(logRatio);
        }

        private static float ComputeSignedRe(float springTargetPosition, float minLimit, float maxLimit)
        {
            float range = maxLimit - minLimit;
            if (range <= 1e-6f)
                return 0f;

            float center = (minLimit + maxLimit) * 0.5f;
            float halfRange = range * 0.5f;
            return Mathf.Clamp((springTargetPosition - center) / halfRange, -1f, 1f);
        }

        private static float ComputeScalarFieldLogMultiplier(float re)
        {
            return Mathf.Log(1f + ComputeScalarFieldK(re));
        }

        private static float ComputeScalarFieldK(float re)
        {
            float absRe = Mathf.Clamp01(Mathf.Abs(re));
            if (absRe <= 0.02f)
                return 0f;

            // Current-frame scalar field: inactive through the central travel and active only
            // near the boundary. Relative velocity is transformed by the field-multiplier ratio
            // between A and B, so outward paths gain and inward paths attenuate continuously.
            float t = Mathf.InverseLerp(0.70f, 1f, absRe);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        private bool TryGetReferencePose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var motionSourceReference = _reference as IMotionSourceReference;
            if (motionSourceReference == null)
                return false;

            position = motionSourceReference.Position;
            if (motionSourceReference.Up.sqrMagnitude < 1e-8f || motionSourceReference.Forward.sqrMagnitude < 1e-8f)
                return false;

            rotation = NormalizeQuaternion(Quaternion.LookRotation(motionSourceReference.Forward, motionSourceReference.Up));
            return true;
        }

        private bool TryGetDrivenControllerPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var ctrl = _reference?.DrivenAtom?.mainController;
            if (ctrl == null)
                return false;

            if (ctrl.followWhenOffRB != null)
            {
                position = ctrl.followWhenOffRB.position;
                rotation = NormalizeQuaternion(ctrl.followWhenOffRB.rotation);
                return true;
            }

            if (ctrl.transform == null)
                return false;

            position = ctrl.transform.position;
            rotation = NormalizeQuaternion(ctrl.transform.rotation);
            return true;
        }

        private void OnEnabledChanged(bool newValue)
        {
            if (newValue)
            {
                _initialized = false;
                _smoothedSpringTargetInitialized = false;
                ResetVelocityModeStates();
                CaptureAtomState();
            }
            else
            {
                ResetVelocityModeStates();
                RestoreAtomState();
            }

            RefreshVisibility();
        }

        private void OnSpeedFilterChanged(bool _)
        {
            if (_speedFilterEnabled != null && _speedFilterEnabled.val && _velocityFollowEnabled != null && _velocityFollowEnabled.val)
                _velocityFollowEnabled.valNoCallback = false;
            ResetVelocityModeStates();
        }

        private void OnVelocityFollowChanged(bool _)
        {
            if (_velocityFollowEnabled != null && _velocityFollowEnabled.val && _speedFilterEnabled != null && _speedFilterEnabled.val)
                _speedFilterEnabled.valNoCallback = false;
            ResetVelocityModeStates();
        }

        private void ResetVelocityModeStates()
        {
            _filteredSpringTargetInitialized = false;
            _filteredSpringTargetLocalPosition = Vector3.zero;
            _prevRawSpringTargetLocalPosition = Vector3.zero;
        }

        private bool IsVelocityFollowEnabled => _velocityFollowEnabled == null || _velocityFollowEnabled.val;

        private static void CreateSectionDivider(IUIBuilder builder)
        {
            builder.CreateDisabledButton("", new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0f));
        }

        private void CaptureAtomState()
        {
            var ctrl = _reference?.DrivenAtom?.mainController;
            if (ctrl == null) return;

            _savedPositionState = ctrl.currentPositionState;
            _savedRotationState = ctrl.currentRotationState;
            _stateSaved = true;

            if (_reference.DrivenKind != DrivenReferenceKind.Empty)
            {
                if (ctrl.control != null)
                {
                    Vector3 ctrlPos;
                    Quaternion ctrlRot;
                    if (TryGetDrivenControllerPose(out ctrlPos, out ctrlRot))
                    {
                        ctrl.control.position = ctrlPos;
                        ctrl.control.rotation = ctrlRot;
                    }
                }
                ctrl.currentPositionState = FreeControllerV3.PositionState.On;
                ctrl.currentRotationState = FreeControllerV3.RotationState.On;
                ApplyGravityOverride(true);
            }
        }

        private void RestoreAtomState()
        {
            if (!_stateSaved) return;

            var ctrl = _reference?.DrivenAtom?.mainController;
            if (ctrl != null)
            {
                ctrl.currentPositionState = _savedPositionState;
                ctrl.currentRotationState = _savedRotationState;
            }

            ApplyGravityOverride(false);

            _stateSaved = false;
        }

        private void ApplyGravityOverride(bool enabled)
        {
            if (!UsesEngineHoldSpring)
                return;

            var drivenAtom = _reference?.DrivenAtom;
            if (drivenAtom == null || drivenAtom.transform == null)
                return;

            var rigidbodies = drivenAtom.transform.GetComponentsInChildren<Rigidbody>(true);
            if (rigidbodies == null || rigidbodies.Length == 0)
                return;

            if (enabled)
            {
                if (!_gravityStatesSaved)
                {
                    _savedGravityStates.Clear();
                    foreach (var rigidbody in rigidbodies)
                    {
                        if (rigidbody == null || _savedGravityStates.ContainsKey(rigidbody))
                            continue;

                        _savedGravityStates[rigidbody] = rigidbody.useGravity;
                    }

                    _gravityStatesSaved = true;
                }

                foreach (var rigidbody in rigidbodies)
                {
                    if (rigidbody != null)
                        rigidbody.useGravity = false;
                }

                return;
            }

            if (!_gravityStatesSaved)
                return;

            foreach (var rigidbody in rigidbodies)
            {
                if (rigidbody == null)
                    continue;

                bool useGravity;
                if (_savedGravityStates.TryGetValue(rigidbody, out useGravity))
                    rigidbody.useGravity = useGravity;
            }

            _savedGravityStates.Clear();
            _gravityStatesSaved = false;
        }

        private void ToggleDrivenVisibility()
        {
            _drivenVisible = !_drivenVisible;
            RefreshVisibility();
        }

        private void ToggleRestVisibility()
        {
            _restVisible = !_restVisible;
            RefreshVisibility();
        }

        private void ToggleCompensationVisibility()
        {
            _compensationVisible = !_compensationVisible;
            RefreshVisibility();
        }

        private void ToggleFollowVisibility()
        {
            _followVisible = !_followVisible;
            RefreshVisibility();
        }

        private void ToggleSpringVisibility()
        {
            _springVisible = !_springVisible;
            RefreshVisibility();
        }

        private void ToggleDamperVisibility()
        {
            _damperVisible = !_damperVisible;
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            bool contentVisible = _drivenVisible && Enabled;

            _drivenGroup?.SetVisible(_drivenVisible);
            _drivenContentGroup?.SetVisible(contentVisible);
            _restGroup?.SetVisible(contentVisible && _restVisible);
            _compensationGroup?.SetVisible(contentVisible && _compensationVisible);
            _followGroup?.SetVisible(contentVisible && _followVisible);
            _springGroup?.SetVisible(contentVisible && _springVisible);
            _damperGroup?.SetVisible(contentVisible && _damperVisible);
        }

        private void MigrateLegacySpringValues()
        {
            if (!UsesPercentSpringUnits)
                return;

            MigrateLegacyPercentValue(_springUp);
            MigrateLegacyPercentValue(_springRight);
            MigrateLegacyPercentValue(_springForward);
            MigrateLegacyPercentValue(_springTwist);
            MigrateLegacyPercentValue(_springRoll);
            MigrateLegacyPercentValue(_springPitch);
        }

        private void MigrateLegacyDamperValues(JSONNode config)
        {
            MigrateLegacyDamperValue(config, $"MotionSource:{_keyPrefix}:DamperUpK", _velocityDampingUp, _targetSmoothingUp);
            MigrateLegacyDamperValue(config, $"MotionSource:{_keyPrefix}:DamperRightK", _velocityDampingRight, _targetSmoothingRight);
            MigrateLegacyDamperValue(config, $"MotionSource:{_keyPrefix}:DamperFwdK", _velocityDampingForward, _targetSmoothingForward);
            MigrateLegacyDamperValue(config, $"MotionSource:{_keyPrefix}:DamperYawK", _velocityDampingTwist, _targetSmoothingTwist);
            MigrateLegacyDamperValue(config, $"MotionSource:{_keyPrefix}:DamperRollK", _velocityDampingRoll, _targetSmoothingRoll);
            MigrateLegacyDamperValue(config, $"MotionSource:{_keyPrefix}:DamperPitchK", _velocityDampingPitch, _targetSmoothingPitch);
        }

        private void MigrateLegacyDamperValue(JSONNode config, string legacyPath, JSONStorableFloat velocitySlider, JSONStorableFloat smoothingSlider)
        {
            if (velocitySlider == null || smoothingSlider == null)
                return;

            if (HasConfigValue(config, velocitySlider.name) || HasConfigValue(config, smoothingSlider.name))
                return;

            float legacyValue;
            if (!TryGetConfigFloat(config, legacyPath, out legacyValue))
                return;

            if (legacyValue > 2f)
                legacyValue /= 100f;

            legacyValue = Mathf.Clamp(legacyValue, 0f, 2f);
            velocitySlider.valNoCallback = legacyValue * 100f;
            smoothingSlider.valNoCallback = LegacyDamperPercentToSmoothingTime(legacyValue);
        }

        private static float LegacyDamperPercentToSmoothingTime(float legacyValue)
        {
            return Mathf.Lerp(0.01f, 0.7f, Mathf.Clamp01(legacyValue / 2f));
        }

        private static bool HasConfigValue(JSONNode config, string path)
        {
            var node = GetConfigNode(config, path);
            return node != null && !string.IsNullOrEmpty(node.Value);
        }

        private static bool TryGetConfigFloat(JSONNode config, string path, out float value)
        {
            value = 0f;
            var node = GetConfigNode(config, path);
            if (node == null || string.IsNullOrEmpty(node.Value))
                return false;

            return float.TryParse(node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.TryParse(node.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static JSONNode GetConfigNode(JSONNode config, string path)
        {
            if (config == null || string.IsNullOrEmpty(path))
                return null;

            var node = config;
            foreach (var name in path.Split(':'))
            {
                node = node[name];
                if (node == null)
                    return null;
            }

            return node;
        }

        private static void MigrateLegacyPercentValue(JSONStorableFloat slider)
        {
            if (slider != null && slider.val > 2f)
                slider.valNoCallback = slider.val / 100f;
        }

        private static Vector3 SignedEuler(Quaternion q)
        {
            var e = q.eulerAngles;
            return new Vector3(Norm180(e.x), Norm180(e.y), Norm180(e.z));
        }

        private static float Norm180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
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
