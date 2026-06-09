using UnityEngine;

namespace ToySerialController.MotionSource
{
    // Reference kinds that the driven-mode controller can drive.
    // NOTE: declared as string constants (NOT an enum) because VaM's embedded
    // Mono MCS crashes the host process when a plugin assembly contains a
    // newly-introduced enum type. Use DrivenReferenceKind.Xxx exactly like an
    // enum at call sites; the underlying type is string.
    public static class DrivenReferenceKind
    {
        public const string Dildo = "Dildo";
        public const string CustomUnityAsset = "CustomUnityAsset";
        public const string Empty = "Empty";
    }

    // Implemented by Reference types whose backing atom can be driven by
    // our algorithm (Dildo / CUA / Empty). MaleReference does NOT implement
    // this because the character penis is part of the character rig.
    public interface IDrivableReference
    {
        Atom DrivenAtom { get; }
        string DrivenKind { get; }
        DrivenMode DrivenMode { get; }

        // W[Pi] (insertion part) - real-time, must be derived from the underlying atom each frame.
        Vector3 InsertionPoint { get; }
        Quaternion InsertionRotation { get; }
    }
}
