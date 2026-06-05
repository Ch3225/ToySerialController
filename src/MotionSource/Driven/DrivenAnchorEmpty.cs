using System.Collections;
using UnityEngine;

namespace ToySerialController.MotionSource
{
    // Manages the cyan "driven anchor" Empty atom that the user freely
    // positions in the scene. Its world pose feeds into DrivenMode as the
    // baseline that Offset0 is applied on top of. It is parented to the
    // person's main controller so it rigidly follows the character.
    //
    // The atom is VISIBLE (not hidden) so the user can grab it in VR/desktop.
    // On Reset, we re-place it at (target position + a sensible default offset)
    // and re-attach the parent link.
    public class DrivenAnchorEmpty
    {
        private const string AtomUidPrefix = "TSC_DrivenAnchor_";

        private readonly string _key;
        private Atom _anchorAtom;
        private bool _creationInFlight;

        public DrivenAnchorEmpty(string key)
        {
            _key = key;
        }

        public string AtomUid => AtomUidPrefix + _key;

        public bool HasAnchor => _anchorAtom != null && _anchorAtom.on;

        public Vector3 Position
        {
            get
            {
                var t = GetTransform();
                return t != null ? t.position : Vector3.zero;
            }
        }

        public Quaternion Rotation
        {
            get
            {
                var t = GetTransform();
                return t != null ? t.rotation : Quaternion.identity;
            }
        }

        // Find existing atom or kick off creation. Returns true if anchor is
        // immediately available; false if a coroutine has been started and
        // the caller should retry next frame.
        public bool EnsureExists()
        {
            if (HasAnchor) return true;

            _anchorAtom = SuperController.singleton.GetAtomByUid(AtomUid);
            if (_anchorAtom != null) return true;

            if (_creationInFlight) return false;
            _creationInFlight = true;
            SuperController.singleton.StartCoroutine(CreateCoroutine());
            return false;
        }

        // Place anchor at default position relative to the given target pose,
        // and parent it to the given atom (person). Safe to call repeatedly.
        public void Reset(Vector3 targetPos, Quaternion targetRot, Atom parentAtom)
        {
            if (!EnsureExists()) return;
            var t = GetTransform();
            if (t == null) return;

            // Default offset = a few cm above target along its up axis.
            // Matches the original VR-mode "reference point" sitting above
            // the pelvis. User can drag from there.
            var offset = Vector3.zero;
            t.position = targetPos + targetRot * offset;
            t.rotation = targetRot;

            TrySetParent(parentAtom);
        }

        // Drop the parent link (e.g. when motion source is destroyed) but
        // leave the atom in the scene so the user keeps their tuning.
        public void Detach() { /* keep atom + parent link */ }

        private Transform GetTransform()
        {
            if (!HasAnchor) return null;
            return _anchorAtom.mainController != null
                ? _anchorAtom.mainController.transform
                : _anchorAtom.transform;
        }

        private void TrySetParent(Atom parentAtom)
        {
            if (_anchorAtom == null || parentAtom == null) return;
            try
            {
                if (_anchorAtom.parentAtom != parentAtom)
                    _anchorAtom.SelectAtomParent(parentAtom);
            }
            catch { /* swallow; user can parent manually */ }
        }

        private IEnumerator CreateCoroutine()
        {
            yield return SuperController.singleton.AddAtomByType("Empty", AtomUid);
            _anchorAtom = SuperController.singleton.GetAtomByUid(AtomUid);
            _creationInFlight = false;
        }
    }
}
