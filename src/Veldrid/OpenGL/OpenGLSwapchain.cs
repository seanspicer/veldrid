using System;

namespace Veldrid.OpenGL
{
    internal class OpenGLSwapchain : Swapchain
    {
        public override Framebuffer Framebuffer => _framebuffer;
        public override bool IsDisposed => _disposed;
        public override bool SyncToVerticalBlank { get => _gd.SyncToVerticalBlank; set => _gd.SyncToVerticalBlank = value; }
        public override string Name { get; set; } = "OpenGL Context Swapchain";
        private readonly OpenGLGraphicsDevice _gd;
        private readonly OpenGLSwapchainFramebuffer _framebuffer;
        private readonly Action<uint, uint> _resizeAction;
        private bool _disposed;

        public OpenGLSwapchain(
            OpenGLGraphicsDevice gd,
            OpenGLSwapchainFramebuffer framebuffer,
            Action<uint, uint> resizeAction)
        {
            _gd = gd;
            _framebuffer = framebuffer;
            _resizeAction = resizeAction;
        }

        #region Disposal

        public override void Dispose()
        {
            _disposed = true;
        }

        #endregion

        public override void Resize(uint width, uint height)
        {
            _framebuffer.Resize(width, height);
            _resizeAction?.Invoke(width, height);
        }
    }
}
