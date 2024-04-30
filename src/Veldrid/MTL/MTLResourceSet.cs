namespace Veldrid.MTL
{
    internal class MTLResourceSet : ResourceSet
    {
        public new BindableResource[] Resources { get; }
        public new MTLResourceLayout Layout { get; }

        public override bool IsDisposed => _disposed;

        public override string Name { get; set; }
        private bool _disposed;

        public MTLResourceSet(ref ResourceSetDescription description, MTLGraphicsDevice gd)
            : base(ref description)
        {
            Resources = Util.ShallowClone(description.BoundResources);
            Layout = Util.AssertSubtype<ResourceLayout, MTLResourceLayout>(description.Layout);
        }

        #region Disposal

        public override void Dispose()
        {
            _disposed = true;
        }

        #endregion
    }
}
