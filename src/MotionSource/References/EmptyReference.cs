using SimpleJSON;
using System.Collections;
using System.Linq;
using DebugUtils;
using ToySerialController.UI;
using ToySerialController.Utils;
using UnityEngine;

namespace ToySerialController.MotionSource
{
    // Reference backed by a VAM Empty atom. The Empty itself acts as the
    // "virtual dildo": its transform.position/up/forward define a virtual
    // shaft of a user-configurable length/radius. Geometry-only here; the
    // driven-mode logic (writing back to the Empty) lives in DrivenMode.
    public class EmptyReference : IMotionSourceReference, IDrivableReference
    {
        // Default atom uid we look for / create on demand. Visible (not hidden)
        // so the user can grab and reposition it.
        private const string DefaultEmptyAtomUid = "TSC_VirtualDildoEmpty";

        private Atom _emptyAtom;
        private bool _autoCreateAttempted;

        private UIDynamicButton EmptyTitle;
        private JSONStorableStringChooser EmptyChooser;
        private JSONStorableFloat LengthSlider;
        private JSONStorableFloat RadiusSlider;
        private UIDynamicButton CreateOrFindButton;

        private DrivenMode _drivenMode;

        private SuperController Controller => SuperController.singleton;

        public Vector3 Position { get; private set; }
        public Vector3 Up { get; private set; }
        public Vector3 Right { get; private set; }
        public Vector3 Forward { get; private set; }
        public float Length { get; private set; }
        public float Radius { get; private set; }
        public Vector3 PlaneNormal => Up;
        public Vector3 PlaneTangent => Right;

        // IDrivableReference: this Reference can be driven by our algorithm.
        public Atom DrivenAtom => _emptyAtom;
        public string DrivenKind => DrivenReferenceKind.Empty;
        public DrivenMode DrivenMode => _drivenMode;

        public EmptyReference()
        {
            _drivenMode = new DrivenMode(this);
        }

        public void CreateUI(IUIBuilder builder)
        {
            EmptyTitle = builder.CreateDisabledButton("Virtual Dildo Empty", new Color(0.3f, 0.3f, 0.3f), Color.white);
            EmptyChooser = builder.CreatePopup("MotionSource:Empty", "Select Virtual Dildo Empty", null, null, EmptyChooserCallback);
            CreateOrFindButton = builder.CreateButton("Find/Rebuild Virtual Dildo Empty", EnsureDefaultEmpty, new Color(0, 0.75f, 1f) * 0.8f, Color.white);
            LengthSlider = builder.CreateSlider("MotionSource:EmptyLengthCm", "Virtual Dildo Length (cm)", 14f, 2f, 100f, true, true, valueFormat: "F1");
            RadiusSlider = builder.CreateSlider("MotionSource:EmptyRadiusCm", "Virtual Dildo Radius (cm)", 1.5f, 0.2f, 10f, true, true, valueFormat: "F2");

            FindEmpties();

            _drivenMode.CreateUI(builder);
        }

        public void DestroyUI(IUIBuilder builder)
        {
            _drivenMode.Disable();
            _drivenMode.DestroyUI(builder);

            builder.Destroy(EmptyTitle);
            builder.Destroy(EmptyChooser);
            builder.Destroy(CreateOrFindButton);
            builder.Destroy(LengthSlider);
            builder.Destroy(RadiusSlider);
        }

        public void StoreConfig(JSONNode config)
        {
            config.Store(EmptyChooser);
            config.Store(LengthSlider);
            config.Store(RadiusSlider);
            _drivenMode.StoreConfig(config);
        }

        public void RestoreConfig(JSONNode config)
        {
            config.Restore(EmptyChooser);
            config.Restore(LengthSlider);
            config.Restore(RadiusSlider);
            FindEmpties(EmptyChooser.val);
            _drivenMode.RestoreConfig(config);
        }

        public bool Update()
        {
            if (_emptyAtom == null || !_emptyAtom.on)
                return false;

            var t = _emptyAtom.mainController != null
                ? _emptyAtom.mainController.transform
                : _emptyAtom.transform;
            if (t == null)
                return false;

            // Treat Empty's local +Y as the shaft axis (matches Dildo convention).
            Position = t.position;
            Up = t.up;
            Right = t.right;
            Forward = t.forward;
            Length = LengthSlider.val / 100f;
            Radius = RadiusSlider.val / 100f;

            if (DebugDraw.Enabled)
            {
                var basePos = Position;
                var tipPos = Position + Up * Length;
                DebugDraw.DrawLine(basePos, tipPos, Color.magenta);
                DebugDraw.DrawTransform(basePos, Up, Right, Forward, 0.05f);
                // crude shaft outline
                for (var i = 0; i < 8; i++)
                {
                    var a = (i / 8f) * Mathf.PI * 2f;
                    var r = Right * Mathf.Cos(a) * Radius + Forward * Mathf.Sin(a) * Radius;
                    DebugDraw.DrawLine(basePos + r, tipPos + r, new Color(1f, 0.3f, 1f, 0.6f));
                }
            }

            return true;
        }

        public void Refresh() => FindEmpties(EmptyChooser.val);

        private void FindEmpties(string defaultUid = null)
        {
            var emptyUids = Controller.GetAtoms()
                .Where(a => a.type == "Empty")
                .Select(a => a.uid)
                .ToList();

            if (!emptyUids.Contains(defaultUid))
            {
                defaultUid = emptyUids.FirstOrDefault(uid => uid == _emptyAtom?.uid)
                             ?? (emptyUids.Contains(DefaultEmptyAtomUid) ? DefaultEmptyAtomUid : null)
                             ?? emptyUids.FirstOrDefault()
                             ?? "None";
            }

            emptyUids.Insert(0, "None");
            EmptyChooser.choices = emptyUids;
            EmptyChooserCallback(defaultUid);
        }

        private void EmptyChooserCallback(string s)
        {
            var newAtom = Controller.GetAtomByUid(s);
            if (newAtom != _emptyAtom)
                _drivenMode?.Disable(); // force off when switching atoms

            _emptyAtom = newAtom;
            EmptyChooser.valNoCallback = _emptyAtom == null ? "None" : s;
        }

        private void EnsureDefaultEmpty()
        {
            var existing = Controller.GetAtomByUid(DefaultEmptyAtomUid);
            if (existing != null)
            {
                FindEmpties(DefaultEmptyAtomUid);
                return;
            }

            if (_autoCreateAttempted)
                return;
            _autoCreateAttempted = true;

            // Spawn asynchronously, then refresh chooser when ready.
            Controller.StartCoroutine(CreateEmptyCoroutine());
        }

        private IEnumerator CreateEmptyCoroutine()
        {
            yield return Controller.AddAtomByType("Empty", DefaultEmptyAtomUid);
            _autoCreateAttempted = false;
            FindEmpties(DefaultEmptyAtomUid);
        }
    }
}
