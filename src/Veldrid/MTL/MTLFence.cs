using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal class MTLFence : Fence
    {
        public const ulong NOT_SIGNALED = 0;
        public const ulong SIGNALED = 1;

        private MTLSharedEvent _event;
        private bool _disposed;

        public MTLFence(bool signaled, MTLGraphicsDevice gd)
        {
            _event = gd.Device.newSharedEvent();

            // _event.signaledValue = signaled ? SIGNALED : NOT_SIGNALED;
        }

        public override string Name { get; set; }

        public override void Reset() => _event.signaledValue = NOT_SIGNALED;
        public MTLSharedEvent SharedEvent => _event;

        public override bool Signaled => _event.signaledValue == SIGNALED;
        public override bool IsDisposed => _disposed;

        public override void Dispose()
        {
            if (!_disposed)
            {
                ObjectiveCRuntime.release(_event.NativePtr);
                _disposed = true;
            }
        }
    }
}
