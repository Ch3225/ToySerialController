using SimpleJSON;
using System.Collections.Generic;
using DebugUtils;
using ToySerialController.UI;
using ToySerialController.Utils;
using UnityEngine;

namespace ToySerialController.MotionSource
{
    // Per-frame logic:
    //   Qe = current reference pose (read from reference colliders/transform)
    //   Anchor = user-controlled cyan Empty (rigidly parented to person)
    //   P_pos = anchor.position + anchor.rotation * Offset0_pos
    //   P_rot = anchor.rotation * Quat(Offset0_euler)
    //   In TARGET-LOCAL frame:
    //     Qp_local = lerp_per_axis(Qe_local, P_local, alpha)
    //     if Limit enabled: clamp (Qp_local - P_local) per axis to ± limit
    //   Qp_world = target + targetRot * Qp_local
    //   Push Qp into damper ring buffer; output = average of last N samples
    //   Write Qp_smoothed to the driven atom's mainController.transform
    //
    // For Dildo/CUA we also flip the controller to PositionState/RotationState.On
    // so Hold Spring pulls the rigid body. For Empty we just write transform.
    public class DrivenMode
    {
        private const float AlphaMin = 0f;
        private const float AlphaMax = 2f;
        private const float Length = 0.14f; // baseline 14 cm used for "% of length" sliders

        private readonly IDrivableReference _reference;
        private readonly string _key;

        private UIGroup _group;
        private UIDynamicButton _title;
        private bool _visible;

        private JSONStorableBool _enabled;

        // Offset0 (rest pose relative to the cyan anchor, in anchor-local frame)
        private JSONStorableFloat _o0Up, _o0Right, _o0Forward;     // % of Length
        private JSONStorableFloat _o0Pitch, _o0Yaw, _o0Roll;       // degrees

        // Alpha per axis (Qp = lerp(Qe, P, alpha))
        private JSONStorableFloat _aUp, _aRight, _aForward;
        private JSONStorableFloat _aPitch, _aYaw, _aRoll;

        // Limit (clamp Qp deviation from P)
        private JSONStorableBool _limitEnabled;
        private JSONStorableFloat _lUp, _lRight, _lForward;        // % of Length
        private JSONStorableFloat _lPitch, _lYaw, _lRoll;          // degrees

        // Temporal damper: average of last N frames of Qp.
        private JSONStorableFloat _damperFrames;
        private readonly Queue<Vector3> _damperPos = new Queue<Vector3>();
        private readonly Queue<Quaternion> _damperRot = new Queue<Quaternion>();

        private UIDynamicButton _resetAnchorBtn;

        private DrivenAnchorEmpty _anchor;

        // Saved controller state so Disable() can restore.
        private FreeControllerV3.PositionState _savedPositionState;
        private FreeControllerV3.RotationState _savedRotationState;
        private bool _stateSaved;

        public bool Enabled => _enabled != null && _enabled.val;

        public DrivenMode(IDrivableReference reference)
        {
            _reference = reference;
            _key = reference != null ? reference.DrivenKind.ToString() : "Driven";
            _anchor = new DrivenAnchorEmpty(_key);
        }

        // ─────────────────────────────────────────────────────────────────
        // UI
        // ─────────────────────────────────────────────────────────────────
        public void CreateUI(IUIBuilder builder)
        {
            _group = new UIGroup(builder);
            _title = builder.CreateButton("Driven Mode", () => _group.SetVisible(_visible = !_visible), new Color(0f, 0.65f, 0.9f) * 0.8f, Color.white);

            _enabled = _group.CreateToggle($"Driven:{_key}:Enabled", "Enable Driven Mode", false, OnEnabledChanged);

            _resetAnchorBtn = _group.CreateButton("Reset Anchor Empty", ResetAnchor, new Color(0f, 0.85f, 1f) * 0.8f, Color.white);

            // Offset0
            _o0Up      = _group.CreateSlider($"Driven:{_key}:Offset0Up",      "Offset0 Up (%)",      0.5f, -1f, 1f, true, true, valueFormat: "P0");
            _o0Right   = _group.CreateSlider($"Driven:{_key}:Offset0Right",   "Offset0 Right (%)",   0f,   -1f, 1f, true, true, valueFormat: "P0");
            _o0Forward = _group.CreateSlider($"Driven:{_key}:Offset0Forward", "Offset0 Forward (%)", 0f,   -1f, 1f, true, true, valueFormat: "P0");
            _o0Pitch   = _group.CreateSlider($"Driven:{_key}:Offset0Pitch",   "Offset0 Pitch (°)",   0f, -180f, 180f, true, true, valueFormat: "F0");
            _o0Yaw     = _group.CreateSlider($"Driven:{_key}:Offset0Yaw",     "Offset0 Yaw (°)",     0f, -180f, 180f, true, true, valueFormat: "F0");
            _o0Roll    = _group.CreateSlider($"Driven:{_key}:Offset0Roll",    "Offset0 Roll (°)",    0f, -180f, 180f, true, true, valueFormat: "F0");

            // Alpha
            _aUp      = _group.CreateSlider($"Driven:{_key}:AlphaUp",      "Alpha Up",      1f, AlphaMin, AlphaMax, true, true, valueFormat: "F2");
            _aRight   = _group.CreateSlider($"Driven:{_key}:AlphaRight",   "Alpha Right",   1f, AlphaMin, AlphaMax, true, true, valueFormat: "F2");
            _aForward = _group.CreateSlider($"Driven:{_key}:AlphaForward", "Alpha Forward", 1f, AlphaMin, AlphaMax, true, true, valueFormat: "F2");
            _aPitch   = _group.CreateSlider($"Driven:{_key}:AlphaPitch",   "Alpha Pitch",   1f, AlphaMin, AlphaMax, true, true, valueFormat: "F2");
            _aYaw     = _group.CreateSlider($"Driven:{_key}:AlphaYaw",     "Alpha Yaw",     1f, AlphaMin, AlphaMax, true, true, valueFormat: "F2");
            _aRoll    = _group.CreateSlider($"Driven:{_key}:AlphaRoll",    "Alpha Roll",    1f, AlphaMin, AlphaMax, true, true, valueFormat: "F2");

            // Limit
            _limitEnabled = _group.CreateToggle($"Driven:{_key}:LimitEnabled", "Limit Enabled", false);
            _lUp      = _group.CreateSlider($"Driven:{_key}:LimitUp",      "Limit Up (%)",      0.3f, 0f, 2f, true, true, valueFormat: "P0");
            _lRight   = _group.CreateSlider($"Driven:{_key}:LimitRight",   "Limit Right (%)",   0.2f, 0f, 2f, true, true, valueFormat: "P0");
            _lForward = _group.CreateSlider($"Driven:{_key}:LimitForward", "Limit Forward (%)", 0.2f, 0f, 2f, true, true, valueFormat: "P0");
            _lPitch   = _group.CreateSlider($"Driven:{_key}:LimitPitch",   "Limit Pitch (°)",   30f,  0f, 180f, true, true, valueFormat: "F0");
            _lYaw     = _group.CreateSlider($"Driven:{_key}:LimitYaw",     "Limit Yaw (°)",     30f,  0f, 180f, true, true, valueFormat: "F0");
            _lRoll    = _group.CreateSlider($"Driven:{_key}:LimitRoll",    "Limit Roll (°)",    45f,  0f, 180f, true, true, valueFormat: "F0");

            // Damper
            _damperFrames = _group.CreateSlider($"Driven:{_key}:DamperFrames", "Damper Frames", 1f, 1f, 30f, true, true, valueFormat: "F0");

            _group.SetVisible(false);
        }

        public void DestroyUI(IUIBuilder builder)
        {
            if (_enabled != null && _enabled.val)
                _enabled.val = false; // triggers OnEnabledChanged -> Restore
            else
                RestoreAtomState();

            if (_group != null)
            {
                _group.Destroy();
                _group = null;
            }
            if (_title != null) builder.Destroy(_title);
            _title = null;
        }

        public void StoreConfig(JSONNode config)
        {
            _group?.StoreConfig(config);
        }

        public void RestoreConfig(JSONNode config)
        {
            // _group.RestoreConfig will set _enabled.val from JSON, which
            // synchronously fires OnEnabledChanged via the toggle callback —
            // do NOT call it again here or CaptureAtomState/EnsureExists run twice.
            _group?.RestoreConfig(config);
        }

        // Force-off (atom switched, motion source destroyed, etc.)
        public void Disable()
        {
            if (_enabled == null) return;
            if (_enabled.val)
                _enabled.val = false; // triggers OnEnabledChanged -> RestoreAtomState
            else
                RestoreAtomState();
        }

        // ─────────────────────────────────────────────────────────────────
        // Per-frame drive
        // ─────────────────────────────────────────────────────────────────
        public void Update(IMotionSourceTarget target)
        {
            if (!Enabled || _reference?.DrivenAtom == null || target == null)
                return;

            var ctrl = _reference.DrivenAtom.mainController;
            if (ctrl == null) return;

            // Read poses
            var targetPos = target.Position;
            var targetUp = target.Up;
            var targetForward = target.Forward;
            if (targetUp.sqrMagnitude < 1e-8f || targetForward.sqrMagnitude < 1e-8f) return;
            var targetRot = Quaternion.LookRotation(targetForward, targetUp);

            // Anchor — fallback to target if not yet created.
            Vector3 anchorPos;
            Quaternion anchorRot;
            if (_anchor.HasAnchor)
            {
                anchorPos = _anchor.Position;
                anchorRot = _anchor.Rotation;
            }
            else
            {
                anchorPos = targetPos;
                anchorRot = targetRot;
            }

            // P (rest target in world space)
            var o0Local = new Vector3(_o0Right.val * Length, _o0Up.val * Length, _o0Forward.val * Length);
            var o0Rot = Quaternion.Euler(_o0Pitch.val, _o0Yaw.val, _o0Roll.val);
            var pPos = anchorPos + anchorRot * o0Local;
            var pRot = anchorRot * o0Rot;

            // Qe (current reference pose). For the Reference we are driving,
            // its Position/Up/Forward already reflects where the rigid body
            // actually is right now (read from colliders / transform).
            var refIface = (IMotionSourceReference)_reference;
            var qeUp = refIface.Up;
            var qeFwd = refIface.Forward;
            if (qeUp.sqrMagnitude < 1e-8f || qeFwd.sqrMagnitude < 1e-8f) return;
            var qePos = refIface.Position;
            var qeRot = Quaternion.LookRotation(qeFwd, qeUp);

            // Per-axis lerp in TARGET-LOCAL frame.
            var invTargetRot = Quaternion.Inverse(targetRot);
            var qeLocal = invTargetRot * (qePos - targetPos);
            var pLocal = invTargetRot * (pPos - targetPos);
            var qpLocal = new Vector3(
                Mathf.LerpUnclamped(qeLocal.x, pLocal.x, _aRight.val),
                Mathf.LerpUnclamped(qeLocal.y, pLocal.y, _aUp.val),
                Mathf.LerpUnclamped(qeLocal.z, pLocal.z, _aForward.val)
            );

            // Per-axis lerp in target-local Euler for rotation.
            var qeRotLocal = invTargetRot * qeRot;
            var pRotLocal = invTargetRot * pRot;
            var qeEuler = SignedEuler(qeRotLocal);
            var pEuler = SignedEuler(pRotLocal);
            var qpEuler = new Vector3(
                LerpAngleUnclamped(qeEuler.x, pEuler.x, _aPitch.val),
                LerpAngleUnclamped(qeEuler.y, pEuler.y, _aYaw.val),
                LerpAngleUnclamped(qeEuler.z, pEuler.z, _aRoll.val)
            );

            // Limit clamp around P
            if (_limitEnabled != null && _limitEnabled.val)
            {
                qpLocal.x = pLocal.x + Mathf.Clamp(qpLocal.x - pLocal.x, -_lRight.val * Length, _lRight.val * Length);
                qpLocal.y = pLocal.y + Mathf.Clamp(qpLocal.y - pLocal.y, -_lUp.val * Length, _lUp.val * Length);
                qpLocal.z = pLocal.z + Mathf.Clamp(qpLocal.z - pLocal.z, -_lForward.val * Length, _lForward.val * Length);
                qpEuler.x = pEuler.x + Mathf.Clamp(Norm180(qpEuler.x - pEuler.x), -_lPitch.val, _lPitch.val);
                qpEuler.y = pEuler.y + Mathf.Clamp(Norm180(qpEuler.y - pEuler.y), -_lYaw.val, _lYaw.val);
                qpEuler.z = pEuler.z + Mathf.Clamp(Norm180(qpEuler.z - pEuler.z), -_lRoll.val, _lRoll.val);
            }

            var qpPosWorld = targetPos + targetRot * qpLocal;
            var qpRotWorld = targetRot * Quaternion.Euler(qpEuler);

            // Damper (running average of last N samples)
            int n = Mathf.Clamp((int)_damperFrames.val, 1, 30);
            _damperPos.Enqueue(qpPosWorld);
            _damperRot.Enqueue(qpRotWorld);
            while (_damperPos.Count > n) _damperPos.Dequeue();
            while (_damperRot.Count > n) _damperRot.Dequeue();
            var smoothedPos = AveragePos(_damperPos);
            var smoothedRot = AverageRot(_damperRot);

            // Write to driven controller transform
            ctrl.transform.position = smoothedPos;
            ctrl.transform.rotation = smoothedRot;

            if (DebugDraw.Enabled)
            {
                DebugDraw.DrawPoint(anchorPos, Color.cyan, 0.015f);
                DebugDraw.DrawPoint(pPos, Color.green, 0.012f);
                DebugDraw.DrawLine(qePos, smoothedPos, Color.red);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Internals
        // ─────────────────────────────────────────────────────────────────
        private void OnEnabledChanged(bool newValue)
        {
            if (newValue)
            {
                // Ensure anchor exists; if not ready yet, we will fall back
                // to using target pose until creation completes.
                _anchor.EnsureExists();
                _damperPos.Clear();
                _damperRot.Clear();
                CaptureAtomState();
            }
            else
            {
                RestoreAtomState();
            }
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
                ctrl.currentPositionState = FreeControllerV3.PositionState.On;
                ctrl.currentRotationState = FreeControllerV3.RotationState.On;
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
            _stateSaved = false;
        }

        private void ResetAnchor()
        {
            // Use the reference's current pose as a seed for the anchor.
            if (_reference == null) return;
            var refIface = (IMotionSourceReference)_reference;
            var refPos = refIface.Position;
            var refUp = refIface.Up;
            var refFwd = refIface.Forward;
            if (refUp.sqrMagnitude < 1e-8f || refFwd.sqrMagnitude < 1e-8f) return;
            var refRot = Quaternion.LookRotation(refFwd, refUp);
            _anchor.Reset(refPos, refRot, null);
            _damperPos.Clear();
            _damperRot.Clear();
        }

        private static Vector3 AveragePos(Queue<Vector3> q)
        {
            var sum = Vector3.zero;
            foreach (var v in q) sum += v;
            return sum / Mathf.Max(1, q.Count);
        }

        private static Quaternion AverageRot(Queue<Quaternion> q)
        {
            if (q.Count == 0) return Quaternion.identity;
            // Cheap average via normalized component sum, sign-aligned to first.
            Quaternion first = default(Quaternion);
            bool firstSet = false;
            float x = 0, y = 0, z = 0, w = 0;
            foreach (var r in q)
            {
                var rr = r;
                if (!firstSet) { first = rr; firstSet = true; }
                if (Quaternion.Dot(first, rr) < 0f)
                    rr = new Quaternion(-rr.x, -rr.y, -rr.z, -rr.w);
                x += rr.x; y += rr.y; z += rr.z; w += rr.w;
            }
            var avg = new Quaternion(x, y, z, w);
            var mag = Mathf.Sqrt(avg.x * avg.x + avg.y * avg.y + avg.z * avg.z + avg.w * avg.w);
            if (mag < 1e-8f) return Quaternion.identity;
            var inv = 1f / mag;
            return new Quaternion(avg.x * inv, avg.y * inv, avg.z * inv, avg.w * inv);
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

        private static float LerpAngleUnclamped(float a, float b, float t)
        {
            var delta = Norm180(b - a);
            return a + delta * t;
        }
    }
}
