using System;
using System.Collections.Generic;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Veldrid.D3D11
{
    internal class D3D11Buffer : DeviceBuffer
    {
        public override uint SizeInBytes { get; }

        public override BufferUsage Usage { get; }

        public override bool IsDisposed => Buffer.NativePointer == IntPtr.Zero;

        public ID3D11Buffer Buffer { get; }

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                Buffer.DebugName = value;
                foreach (var kvp in srvs) kvp.Value.DebugName = value + "_SRV";

                foreach (var kvp in uavs) kvp.Value.DebugName = value + "_UAV";
            }
        }

        private readonly ID3D11Device device;
        private readonly object accessViewLock = new object();

        private readonly Dictionary<OffsetSizePair, ID3D11ShaderResourceView> srvs
            = new Dictionary<OffsetSizePair, ID3D11ShaderResourceView>();

        private readonly Dictionary<OffsetSizePair, ID3D11UnorderedAccessView> uavs
            = new Dictionary<OffsetSizePair, ID3D11UnorderedAccessView>();

        private readonly uint structureByteStride;
        private readonly bool rawBuffer;
        private string name;

        public D3D11Buffer(ID3D11Device device, uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool rawBuffer)
        {
            this.device = device;
            SizeInBytes = sizeInBytes;
            Usage = usage;
            this.structureByteStride = structureByteStride;
            this.rawBuffer = rawBuffer;

            var bd = new Vortice.Direct3D11.BufferDescription(
                (int)sizeInBytes,
                D3D11Formats.VdToD3D11BindFlags(usage));

            if ((usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly
                || (usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite)
            {
                if (rawBuffer)
                    bd.MiscFlags = ResourceOptionFlags.BufferAllowRawViews;
                else
                {
                    bd.MiscFlags = ResourceOptionFlags.BufferStructured;
                    bd.StructureByteStride = (int)structureByteStride;
                }
            }

            if ((usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer) bd.MiscFlags = ResourceOptionFlags.DrawIndirectArguments;

            if ((usage & BufferUsage.Dynamic) == BufferUsage.Dynamic)
            {
                bd.Usage = ResourceUsage.Dynamic;
                bd.CPUAccessFlags = CpuAccessFlags.Write;
            }
            else if ((usage & BufferUsage.Staging) == BufferUsage.Staging)
            {
                bd.Usage = ResourceUsage.Staging;
                bd.CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write;
            }

            Buffer = device.CreateBuffer(bd);
        }

        #region Disposal

        public override void Dispose()
        {
            foreach (var kvp in srvs) kvp.Value.Dispose();

            foreach (var kvp in uavs) kvp.Value.Dispose();
            Buffer.Dispose();
        }

        #endregion

        internal ID3D11ShaderResourceView GetShaderResourceView(uint offset, uint size)
        {
            lock (accessViewLock)
            {
                var pair = new OffsetSizePair(offset, size);

                if (!srvs.TryGetValue(pair, out var srv))
                {
                    srv = createShaderResourceView(offset, size);
                    srvs.Add(pair, srv);
                }

                return srv;
            }
        }

        internal ID3D11UnorderedAccessView GetUnorderedAccessView(uint offset, uint size)
        {
            lock (accessViewLock)
            {
                var pair = new OffsetSizePair(offset, size);

                if (!uavs.TryGetValue(pair, out var uav))
                {
                    uav = createUnorderedAccessView(offset, size);
                    uavs.Add(pair, uav);
                }

                return uav;
            }
        }

        private ID3D11ShaderResourceView createShaderResourceView(uint offset, uint size)
        {
            if (rawBuffer)
            {
                var srvDesc = new ShaderResourceViewDescription(Buffer,
                    Format.R32_Typeless,
                    (int)offset / 4,
                    (int)size / 4,
                    BufferExtendedShaderResourceViewFlags.Raw);

                return device.CreateShaderResourceView(Buffer, srvDesc);
            }
            else
            {
                var srvDesc = new ShaderResourceViewDescription
                {
                    ViewDimension = ShaderResourceViewDimension.Buffer
                };
                srvDesc.Buffer.NumElements = (int)(size / structureByteStride);
                srvDesc.Buffer.ElementOffset = (int)(offset / structureByteStride);
                return device.CreateShaderResourceView(Buffer, srvDesc);
            }
        }

        private ID3D11UnorderedAccessView createUnorderedAccessView(uint offset, uint size)
        {
            if (rawBuffer)
            {
                var uavDesc = new UnorderedAccessViewDescription(Buffer,
                    Format.R32_Typeless,
                    (int)offset / 4,
                    (int)size / 4,
                    BufferUnorderedAccessViewFlags.Raw);

                return device.CreateUnorderedAccessView(Buffer, uavDesc);
            }
            else
            {
                var uavDesc = new UnorderedAccessViewDescription(Buffer,
                    Format.Unknown,
                    (int)(offset / structureByteStride),
                    (int)(size / structureByteStride)
                );

                return device.CreateUnorderedAccessView(Buffer, uavDesc);
            }
        }

        private struct OffsetSizePair : IEquatable<OffsetSizePair>
        {
            public readonly uint Offset;
            public readonly uint Size;

            public OffsetSizePair(uint offset, uint size)
            {
                Offset = offset;
                Size = size;
            }

            public bool Equals(OffsetSizePair other)
            {
                return Offset.Equals(other.Offset) && Size.Equals(other.Size);
            }

            public override int GetHashCode()
            {
                return HashHelper.Combine(Offset.GetHashCode(), Size.GetHashCode());
            }
        }
    }
}
