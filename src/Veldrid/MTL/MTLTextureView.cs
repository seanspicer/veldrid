using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal class MTLTextureView : TextureView
    {
        public MetalBindings.MTLTexture TargetDeviceTexture { get; }

        public override bool IsDisposed => _disposed;

        public override string Name { get; set; }
        private readonly bool _hasTextureView;
        private bool _disposed;

        public MTLTextureView(ref TextureViewDescription description, MTLGraphicsDevice gd)
            : base(ref description)
        {
            var targetMTLTexture = Util.AssertSubtype<Texture, MTLTexture>(description.Target);

            if (BaseMipLevel != 0 || MipLevels != Target.MipLevels
                                  || BaseArrayLayer != 0 || ArrayLayers != Target.ArrayLayers
                                  || Format != Target.Format)
            {
                _hasTextureView = true;
                uint effectiveArrayLayers = Target.Usage.HasFlag(TextureUsage.Cubemap) ? ArrayLayers * 6 : ArrayLayers;
                TargetDeviceTexture = targetMTLTexture.DeviceTexture.newTextureView(
                    MTLFormats.VdToMTLPixelFormat(Format, (description.Target.Usage & TextureUsage.DepthStencil) != 0),
                    targetMTLTexture.MTLTextureType,
                    new NSRange(BaseMipLevel, MipLevels),
                    new NSRange(BaseArrayLayer, effectiveArrayLayers));
            }
            else
                TargetDeviceTexture = targetMTLTexture.DeviceTexture;
        }

        #region Disposal

        public override void Dispose()
        {
            if (_hasTextureView && !_disposed)
            {
                _disposed = true;
                ObjectiveCRuntime.release(TargetDeviceTexture.NativePtr);
            }
        }

        #endregion
    }
}
