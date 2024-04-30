namespace Veldrid.OpenGL
{
    internal class OpenGLResourceLayout : ResourceLayout
    {
        public ResourceLayoutElementDescription[] Elements { get; }

        public override bool IsDisposed => _disposed;

        public override string Name { get; set; }
        private bool _disposed;

        public OpenGLResourceLayout(ref ResourceLayoutDescription description)
            : base(ref description)
        {
            Elements = Util.ShallowClone(description.Elements);
        }

        #region Disposal

        public override void Dispose()
        {
            _disposed = true;
        }

        #endregion

        public bool IsDynamicBuffer(uint slot)
        {
            return (Elements[slot].Options & ResourceLayoutElementOptions.DynamicBinding) != 0;
        }
    }
}
