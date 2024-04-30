using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal class MTLBuffer : DeviceBuffer
    {
        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }

        public uint ActualCapacity { get; }

        public override bool IsDisposed => _disposed;

        public override string Name
        {
            get => _name;
            set
            {
                var nameNSS = NSString.New(value);
                DeviceBuffer.addDebugMarker(nameNSS, new NSRange(0, SizeInBytes));
                ObjectiveCRuntime.release(nameNSS.NativePtr);
                _name = value;
            }
        }

        public MetalBindings.MTLBuffer DeviceBuffer { get; }

        public unsafe void* Pointer { get; private set; }
        private string _name;
        private bool _disposed;

        public MTLBuffer(ref BufferDescription bd, MTLGraphicsDevice gd)
        {
            SizeInBytes = bd.SizeInBytes;
            uint roundFactor = (4 - SizeInBytes % 4) % 4;
            ActualCapacity = SizeInBytes + roundFactor;
            Usage = bd.Usage;

            bool sharedMemory = Usage == BufferUsage.Staging || (Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;
            var bufferOptions = sharedMemory ? MTLResourceOptions.StorageModeShared : MTLResourceOptions.StorageModePrivate;

            DeviceBuffer = gd.Device.newBufferWithLengthOptions(
                ActualCapacity,
                bufferOptions);

            unsafe
            {
                if (sharedMemory)
                    Pointer = DeviceBuffer.contents();
            }
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                ObjectiveCRuntime.release(DeviceBuffer.NativePtr);
            }
        }

        #endregion
    }
}
