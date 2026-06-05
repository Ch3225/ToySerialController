using System.Collections;
using UnityEngine;

namespace ToySerialController.MotionSource
{
    public class DrivenAnchorEmpty
    {
        private const string AtomUidPrefix = "TSC_DrivenReferencePoint_";

        private readonly string _key;

        private Atom _atom;
        private bool _creating;
        private string _linkedPersonUid;
        private string _selectedAtomUid;

        public DrivenAnchorEmpty(string key)
        {
            _key = key;
            _selectedAtomUid = DefaultAtomUid;
        }

        public string DefaultAtomUid => AtomUidPrefix + _key;
        public string SelectedAtomUid => string.IsNullOrEmpty(_selectedAtomUid) ? "None" : _selectedAtomUid;

        public bool HasAtom => _atom != null && _atom.on;

        public Vector3 Position
        {
            get
            {
                var transform = GetTransform();
                return transform != null ? transform.position : Vector3.zero;
            }
        }

        public Quaternion Rotation
        {
            get
            {
                var transform = GetTransform();
                return transform != null ? transform.rotation : Quaternion.identity;
            }
        }

        public bool EnsureExists(AbstractPersonTarget personTarget, Vector3 defaultPosition, Quaternion defaultRotation)
        {
            if (HasAtom)
            {
                EnsureParentLink(personTarget);
                return true;
            }

            _atom = SuperController.singleton.GetAtomByUid(_selectedAtomUid);
            if (_atom != null && _atom.on)
            {
                EnsureParentLink(personTarget);
                return true;
            }

            if (_selectedAtomUid != DefaultAtomUid)
                return false;

            if (_creating)
                return false;

            _creating = true;
            SuperController.singleton.StartCoroutine(CreateAnchorCoroutine(personTarget, defaultPosition, defaultRotation));
            return false;
        }

        public void ResetToDefault(AbstractPersonTarget personTarget, Vector3 defaultPosition, Quaternion defaultRotation)
        {
            // Re-point both _selectedAtomUid AND _atom at the default atom; otherwise if the user previously
            // chose a non-default Empty in the chooser, _atom still references that one and HasAtom would
            // make EnsureExists operate on the wrong atom — the default atom never moves and Reset appears
            // to do nothing.
            _selectedAtomUid = DefaultAtomUid;
            _atom = SuperController.singleton.GetAtomByUid(DefaultAtomUid);
            if (!EnsureExists(personTarget, defaultPosition, defaultRotation))
                return;

            // VaM's SelectLinkToRigidbody won't recapture the relative offset if the controller is already
            // linked to the same rigidbody. Explicitly unlink first so ApplyPose's new position becomes the
            // new anchor offset when we re-link below.
            var controller = _atom != null ? _atom.mainController : null;
            if (controller != null)
                controller.SelectLinkToRigidbody(null, FreeControllerV3.SelectLinkState.PositionAndRotation);
            _linkedPersonUid = null;

            ApplyPose(defaultPosition, defaultRotation);
            EnsureParentLink(personTarget);
        }

        public void SelectAtom(Atom atom)
        {
            _atom = atom;
            _selectedAtomUid = atom != null ? atom.uid : null;
            _linkedPersonUid = null;
        }

        public void SelectAtomUid(string atomUid)
        {
            if (string.IsNullOrEmpty(atomUid) || atomUid == "None")
            {
                _atom = null;
                _selectedAtomUid = null;
                _linkedPersonUid = null;
                return;
            }

            SelectAtom(SuperController.singleton.GetAtomByUid(atomUid));
            if (_atom == null)
                _selectedAtomUid = atomUid;
        }

        public bool FindExistingOrCreateDefault(AbstractPersonTarget personTarget, Vector3 defaultPosition, Quaternion defaultRotation)
        {
            _selectedAtomUid = DefaultAtomUid;
            _atom = SuperController.singleton.GetAtomByUid(DefaultAtomUid);
            if (_atom != null && _atom.on)
            {
                EnsureParentLink(personTarget);
                return true;
            }

            if (_creating)
                return false;

            _creating = true;
            SuperController.singleton.StartCoroutine(CreateAnchorCoroutine(personTarget, defaultPosition, defaultRotation));
            return false;
        }

        private IEnumerator CreateAnchorCoroutine(AbstractPersonTarget personTarget, Vector3 defaultPosition, Quaternion defaultRotation)
        {
            yield return SuperController.singleton.AddAtomByType("Empty", DefaultAtomUid);

            _creating = false;
            _atom = SuperController.singleton.GetAtomByUid(DefaultAtomUid);
            _selectedAtomUid = DefaultAtomUid;
            if (_atom == null || !_atom.on)
                yield break;

            ApplyPose(defaultPosition, defaultRotation);
            EnsureParentLink(personTarget);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            var controller = _atom != null ? _atom.mainController : null;
            if (controller != null)
            {
                var control = controller.control;
                if (control != null)
                {
                    control.position = position;
                    control.rotation = rotation;
                }

                controller.transform.position = position;
                controller.transform.rotation = rotation;

                if (controller.followWhenOff != null)
                {
                    controller.followWhenOff.position = position;
                    controller.followWhenOff.rotation = rotation;
                }

                controller.onPositionChangeHandlers?.Invoke(controller);
                return;
            }

            var transform = GetTransform();
            if (transform == null)
                return;

            transform.position = position;
            transform.rotation = rotation;
        }

        private void EnsureParentLink(AbstractPersonTarget personTarget)
        {
            if (personTarget == null || personTarget.PersonAtom == null)
                return;

            var controller = _atom != null ? _atom.mainController : null;
            var personController = personTarget.PersonAtom.mainController;
            if (controller == null || personController == null)
                return;

            var personUid = personTarget.PersonAtom.uid;
            if (_linkedPersonUid == personUid)
                return;

            var rigidbody = personController.GetComponent<Rigidbody>();
            if (rigidbody == null)
                return;

            controller.SelectLinkToRigidbody(rigidbody, FreeControllerV3.SelectLinkState.PositionAndRotation);
            _linkedPersonUid = personUid;
        }

        private Transform GetTransform()
        {
            if (_atom == null || !_atom.on)
                return null;

            return _atom.mainController != null ? _atom.mainController.transform : _atom.transform;
        }
    }
}