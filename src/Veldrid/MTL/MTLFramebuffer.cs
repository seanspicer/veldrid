using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal class MTLFramebuffer : Framebuffer
    {
        public override bool IsDisposed => _disposed;

        public override string Name { get; set; }
        private bool _disposed;

        public MTLFramebuffer(MTLGraphicsDevice gd, ref FramebufferDescription description)
            : base(description.DepthTarget, description.ColorTargets)
        {
        }

        public MTLFramebuffer()
        {
        }

        #region Disposal

        public override void Dispose()
        {
            _disposed = true;
        }

        #endregion

        public MTLRenderPassDescriptor CreateRenderPassDescriptor()
        {
            var ret = MTLRenderPassDescriptor.New();

            for (int i = 0; i < ColorTargets.Count; i++)
            {
                var colorTarget = ColorTargets[i];
                var mtlTarget = Util.AssertSubtype<Texture, MTLTexture>(colorTarget.Target);
                var colorDescriptor = ret.colorAttachments[(uint)i];
                colorDescriptor.texture = mtlTarget.DeviceTexture;
                colorDescriptor.loadAction = MTLLoadAction.Load;
                colorDescriptor.slice = colorTarget.ArrayLayer;
                colorDescriptor.level = colorTarget.MipLevel;
            }

            if (DepthTarget != null)
            {
                var mtlDepthTarget = Util.AssertSubtype<Texture, MTLTexture>(DepthTarget.Value.Target);
                var depthDescriptor = ret.depthAttachment;
                depthDescriptor.loadAction = mtlDepthTarget.MTLStorageMode == MTLStorageMode.Memoryless ? MTLLoadAction.DontCare : MTLLoadAction.Load;
                depthDescriptor.storeAction = mtlDepthTarget.MTLStorageMode == MTLStorageMode.Memoryless ? MTLStoreAction.DontCare : MTLStoreAction.Store;
                depthDescriptor.texture = mtlDepthTarget.DeviceTexture;
                depthDescriptor.slice = DepthTarget.Value.ArrayLayer;
                depthDescriptor.level = DepthTarget.Value.MipLevel;

                if (FormatHelpers.IsStencilFormat(mtlDepthTarget.Format))
                {
                    var stencilDescriptor = ret.stencilAttachment;
                    stencilDescriptor.loadAction = mtlDepthTarget.MTLStorageMode == MTLStorageMode.Memoryless ? MTLLoadAction.DontCare : MTLLoadAction.Load;
                    stencilDescriptor.storeAction = mtlDepthTarget.MTLStorageMode == MTLStorageMode.Memoryless ? MTLStoreAction.DontCare : MTLStoreAction.Store;
                    stencilDescriptor.texture = mtlDepthTarget.DeviceTexture;
                    stencilDescriptor.slice = DepthTarget.Value.ArrayLayer;
                }
            }

            return ret;
        }
    }
}
