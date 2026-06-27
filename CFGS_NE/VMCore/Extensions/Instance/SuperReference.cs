namespace CFGS_VM.VMCore.Extensions.Instance
{
    /// <summary>
    /// Runtime value produced by <c>super</c> in an <b>instance</b> method context. Carries the
    /// <see cref="Derived"/> instance — which stays bound as <c>this</c> for the dispatched base
    /// method, so field access sees the real instance — and the <see cref="BaseLayer"/>, where member
    /// resolution starts so a derived override is correctly skipped. Consumed by the member-access
    /// path (GetIndexedValue), which resolves the member on the base layer and rebinds the resulting
    /// bound method's receiver to <see cref="Derived"/>.
    /// </summary>
    public sealed record SuperReference(ClassInstance Derived, ClassInstance BaseLayer);
}
