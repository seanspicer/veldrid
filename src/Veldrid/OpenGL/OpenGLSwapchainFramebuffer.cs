using System.Collections.Generic;

namespace Veldrid.OpenGL
{
    internal class OpenGLSwapchainFramebuffer : Framebuffer
    {
        public override uint Width => colorTexture.Width;
        public override uint Height => colorTexture.Height;

        public override OutputDescription OutputDescription { get; }
        public override bool IsDisposed => disposed;

        public override IReadOnlyList<FramebufferAttachment> ColorTargets => colorTargets;
        public override FramebufferAttachment? DepthTarget { get; }

        public bool DisableSrgbConversion { get; }
        public override string Name { get; set; }

        private readonly OpenGLPlaceholderTexture colorTexture;
        private readonly OpenGLPlaceholderTexture depthTexture;

        private readonly FramebufferAttachment[] colorTargets;
        private bool disposed;

        internal OpenGLSwapchainFramebuffer(
            uint width, uint height,
            PixelFormat colorFormat,
            PixelFormat? depthFormat,
            bool disableSrgbConversion)
        {
            // This is wrong, but it's not really used.
            var depthDesc = depthFormat != null
                ? new OutputAttachmentDescription(depthFormat.Value)
                : (OutputAttachmentDescription?)null;
            OutputDescription = new OutputDescription(
                depthDesc,
                new OutputAttachmentDescription(colorFormat));

            colorTexture = new OpenGLPlaceholderTexture(
                width,
                height,
                colorFormat,
                TextureUsage.RenderTarget,
                TextureSampleCount.Count1);
            colorTargets = new[] { new FramebufferAttachment(colorTexture, 0) };

            if (depthFormat != null)
            {
                depthTexture = new OpenGLPlaceholderTexture(
                    width,
                    height,
                    depthFormat.Value,
                    TextureUsage.DepthStencil,
                    TextureSampleCount.Count1);
                DepthTarget = new FramebufferAttachment(depthTexture, 0);
            }

            OutputDescription = OutputDescription.CreateFromFramebuffer(this);

            DisableSrgbConversion = disableSrgbConversion;
        }

        #region Disposal

        public override void Dispose()
        {
            disposed = true;
        }

        #endregion

        public void Resize(uint width, uint height)
        {
            colorTexture.Resize(width, height);
            depthTexture?.Resize(width, height);
        }
    }
}
