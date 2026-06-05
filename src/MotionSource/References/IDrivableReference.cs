namespace ToySerialController.MotionSource
{
    // Reference kinds that the driven-mode controller can drive.
    public enum DrivenReferenceKind
    {
        Dildo,
        CustomUnityAsset,
        Empty
    }

    // Implemented by Reference types whose backing atom can be driven by
    // our algorithm (Dildo / CUA / Empty). MaleReference does NOT implement
    // this because the character penis is part of the character rig.
    public interface IDrivableReference
    {
        Atom DrivenAtom { get; }
        DrivenReferenceKind DrivenKind { get; }
        DrivenMode Driven { get; }
    }
}
