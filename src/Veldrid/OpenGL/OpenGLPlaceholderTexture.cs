namespace Veldrid.OpenGL
{
    internal class OpenGLPlaceholderTexture : Texture
    {
        public override PixelFormat Format { get; }

        public override uint Width => width;

        public override uint Height => height;

        public override uint Depth => 1;

        public override uint MipLevels => 1;

        public override uint ArrayLayers => 1;

        public override TextureUsage Usage { get; }

        public override TextureSampleCount SampleCount { get; }

        public override TextureType Type => TextureType.Texture2D;

        public override bool IsDisposed => disposed;

        public override string Name { get; set; }
        private uint height;
        private uint width;
        private bool disposed;

        public OpenGLPlaceholderTexture(
            uint width,
            uint height,
            PixelFormat format,
            TextureUsage usage,
            TextureSampleCount sampleCount)
        {
            this.width = width;
            this.height = height;
            Format = format;
            Usage = usage;
            SampleCount = sampleCount;
        }

        public void Resize(uint width, uint height)
        {
            this.width = width;
            this.height = height;
        }

        private protected override void DisposeCore()
        {
            disposed = true;
        }
    }
}
