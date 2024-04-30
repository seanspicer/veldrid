namespace Veldrid.D3D11
{
    internal class D3D11ResourceLayout : ResourceLayout
    {
        public int UniformBufferCount { get; }
        public int StorageBufferCount { get; }
        public int TextureCount { get; }
        public int SamplerCount { get; }

        public override bool IsDisposed => disposed;

        public override string Name { get; set; }

        private readonly ResourceBindingInfo[] bindingInfosByVdIndex;
        private bool disposed;

        public D3D11ResourceLayout(ref ResourceLayoutDescription description)
            : base(ref description)
        {
            var elements = description.Elements;
            bindingInfosByVdIndex = new ResourceBindingInfo[elements.Length];

            int cbIndex = 0;
            int texIndex = 0;
            int samplerIndex = 0;
            int unorderedAccessIndex = 0;

            for (int i = 0; i < bindingInfosByVdIndex.Length; i++)
            {
                int slot;

                switch (elements[i].Kind)
                {
                    case ResourceKind.UniformBuffer:
                        slot = cbIndex++;
                        break;

                    case ResourceKind.StructuredBufferReadOnly:
                        slot = texIndex++;
                        break;

                    case ResourceKind.StructuredBufferReadWrite:
                        slot = unorderedAccessIndex++;
                        break;

                    case ResourceKind.TextureReadOnly:
                        slot = texIndex++;
                        break;

                    case ResourceKind.TextureReadWrite:
                        slot = unorderedAccessIndex++;
                        break;

                    case ResourceKind.Sampler:
                        slot = samplerIndex++;
                        break;

                    default: throw Illegal.Value<ResourceKind>();
                }

                bindingInfosByVdIndex[i] = new ResourceBindingInfo(
                    slot,
                    elements[i].Stages,
                    elements[i].Kind,
                    (elements[i].Options & ResourceLayoutElementOptions.DynamicBinding) != 0);
            }

            UniformBufferCount = cbIndex;
            StorageBufferCount = unorderedAccessIndex;
            TextureCount = texIndex;
            SamplerCount = samplerIndex;
        }

        #region Disposal

        public override void Dispose()
        {
            disposed = true;
        }

        #endregion

        public ResourceBindingInfo GetDeviceSlotIndex(int resourceLayoutIndex)
        {
            if (resourceLayoutIndex >= bindingInfosByVdIndex.Length) throw new VeldridException($"Invalid resource index: {resourceLayoutIndex}. Maximum is: {bindingInfosByVdIndex.Length - 1}.");

            return bindingInfosByVdIndex[resourceLayoutIndex];
        }

        public bool IsDynamicBuffer(int index)
        {
            return bindingInfosByVdIndex[index].DynamicBuffer;
        }

        internal struct ResourceBindingInfo
        {
            public int Slot;
            public ShaderStages Stages;
            public ResourceKind Kind;
            public bool DynamicBuffer;

            public ResourceBindingInfo(int slot, ShaderStages stages, ResourceKind kind, bool dynamicBuffer)
            {
                Slot = slot;
                Stages = stages;
                Kind = kind;
                DynamicBuffer = dynamicBuffer;
            }
        }
    }
}
