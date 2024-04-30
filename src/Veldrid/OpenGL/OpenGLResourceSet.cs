namespace Veldrid.OpenGL
{
    internal class OpenGLResourceSet : ResourceSet
    {
        public new OpenGLResourceLayout Layout { get; }
        public new IBindableResource[] Resources { get; }

        public override bool IsDisposed => disposed;
        public override string Name { get; set; }
        private bool disposed;

        public OpenGLResourceSet(ref ResourceSetDescription description)
            : base(ref description)
        {
            Layout = Util.AssertSubtype<ResourceLayout, OpenGLResourceLayout>(description.Layout);
            Resources = Util.ShallowClone(description.BoundResources);
        }

        #region Disposal

        public override void Dispose()
        {
            disposed = true;
        }

        #endregion
    }
}
