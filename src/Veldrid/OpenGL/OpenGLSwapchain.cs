using System;

namespace Veldrid.OpenGL
{
    internal class OpenGLSwapchain : Swapchain
    {
        public override Framebuffer Framebuffer => framebuffer;
        public override bool IsDisposed => disposed;
        public override bool SyncToVerticalBlank { get => gd.SyncToVerticalBlank; set => gd.SyncToVerticalBlank = value; }
        public override string Name { get; set; } = "OpenGL Context Swapchain";
        private readonly OpenGLGraphicsDevice gd;
        private readonly OpenGLSwapchainFramebuffer framebuffer;
        private readonly Action<uint, uint> resizeAction;
        private bool disposed;

        public OpenGLSwapchain(
            OpenGLGraphicsDevice gd,
            OpenGLSwapchainFramebuffer framebuffer,
            Action<uint, uint> resizeAction)
        {
            this.gd = gd;
            this.framebuffer = framebuffer;
            this.resizeAction = resizeAction;
        }

        #region Disposal

        public override void Dispose()
        {
            disposed = true;
        }

        #endregion

        public override void Resize(uint width, uint height)
        {
            framebuffer.Resize(width, height);
            resizeAction?.Invoke(width, height);
        }
    }
}
