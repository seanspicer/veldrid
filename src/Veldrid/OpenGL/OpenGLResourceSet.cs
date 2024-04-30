namespace Veldrid.OpenGL
{
    internal class OpenGLResourceSet : ResourceSet
    {
        public new OpenGLResourceLayout Layout { get; }
        public new BindableResource[] Resources { get; }

        public override bool IsDisposed => _disposed;
        public override string Name { get; set; }
        private bool _disposed;

        public OpenGLResourceSet(ref ResourceSetDescription description)
            : base(ref description)
        {
            Layout = Util.AssertSubtype<ResourceLayout, OpenGLResourceLayout>(description.Layout);
            Resources = Util.ShallowClone(description.BoundResources);
        }

        #region Disposal

        public override void Dispose()
        {
            _disposed = true;
        }

        #endregion
    }
}
