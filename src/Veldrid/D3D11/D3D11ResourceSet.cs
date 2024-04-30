namespace Veldrid.D3D11
{
    internal class D3D11ResourceSet : ResourceSet
    {
        public new BindableResource[] Resources { get; }
        public new D3D11ResourceLayout Layout { get; }

        public override bool IsDisposed => _disposed;

        public override string Name { get; set; }

        private bool _disposed;

        public D3D11ResourceSet(ref ResourceSetDescription description)
            : base(ref description)
        {
            Resources = Util.ShallowClone(description.BoundResources);
            Layout = Util.AssertSubtype<ResourceLayout, D3D11ResourceLayout>(description.Layout);
        }

        #region Disposal

        public override void Dispose()
        {
            _disposed = true;
        }

        #endregion
    }
}
