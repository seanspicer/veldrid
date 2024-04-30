using Vulkan;
using static Veldrid.Vk.VulkanUtil;
using static Vulkan.VulkanNative;

namespace Veldrid.Vk
{
    internal unsafe class VkBuffer : DeviceBuffer
    {
        public ResourceRefCount RefCount { get; }
        public override bool IsDisposed => _destroyed;

        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }

        public Vulkan.VkBuffer DeviceBuffer => _deviceBuffer;
        public VkMemoryBlock Memory => _memory;

        public VkMemoryRequirements BufferMemoryRequirements => _bufferMemoryRequirements;

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        private readonly VkGraphicsDevice _gd;
        private readonly Vulkan.VkBuffer _deviceBuffer;
        private readonly VkMemoryBlock _memory;
        private readonly VkMemoryRequirements _bufferMemoryRequirements;
        private bool _destroyed;
        private string _name;

        public VkBuffer(VkGraphicsDevice gd, uint sizeInBytes, BufferUsage usage, string callerMember = null)
        {
            _gd = gd;
            SizeInBytes = sizeInBytes;
            Usage = usage;

            var vkUsage = VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst;
            if ((usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer) vkUsage |= VkBufferUsageFlags.VertexBuffer;

            if ((usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer) vkUsage |= VkBufferUsageFlags.IndexBuffer;

            if ((usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer) vkUsage |= VkBufferUsageFlags.UniformBuffer;

            if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite
                || (usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly)
                vkUsage |= VkBufferUsageFlags.StorageBuffer;

            if ((usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer) vkUsage |= VkBufferUsageFlags.IndirectBuffer;

            var bufferCI = VkBufferCreateInfo.New();
            bufferCI.size = sizeInBytes;
            bufferCI.usage = vkUsage;
            var result = vkCreateBuffer(gd.Device, ref bufferCI, null, out _deviceBuffer);
            CheckResult(result);

            bool prefersDedicatedAllocation;

            if (_gd.GetBufferMemoryRequirements2 != null)
            {
                var memReqInfo2 = VkBufferMemoryRequirementsInfo2KHR.New();
                memReqInfo2.buffer = _deviceBuffer;
                var memReqs2 = VkMemoryRequirements2KHR.New();
                var dedicatedReqs = VkMemoryDedicatedRequirementsKHR.New();
                memReqs2.pNext = &dedicatedReqs;
                _gd.GetBufferMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
                _bufferMemoryRequirements = memReqs2.memoryRequirements;
                prefersDedicatedAllocation = dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation;
            }
            else
            {
                vkGetBufferMemoryRequirements(gd.Device, _deviceBuffer, out _bufferMemoryRequirements);
                prefersDedicatedAllocation = false;
            }

            bool isStaging = (usage & BufferUsage.Staging) == BufferUsage.Staging;
            bool hostVisible = isStaging || (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

            var memoryPropertyFlags =
                hostVisible
                    ? VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent
                    : VkMemoryPropertyFlags.DeviceLocal;

            if (isStaging)
            {
                // Use "host cached" memory for staging when available, for better performance of GPU -> CPU transfers
                bool hostCachedAvailable = TryFindMemoryType(
                    gd.PhysicalDeviceMemProperties,
                    _bufferMemoryRequirements.memoryTypeBits,
                    memoryPropertyFlags | VkMemoryPropertyFlags.HostCached,
                    out _);
                if (hostCachedAvailable) memoryPropertyFlags |= VkMemoryPropertyFlags.HostCached;
            }

            var memoryToken = gd.MemoryManager.Allocate(
                gd.PhysicalDeviceMemProperties,
                _bufferMemoryRequirements.memoryTypeBits,
                memoryPropertyFlags,
                hostVisible,
                _bufferMemoryRequirements.size,
                _bufferMemoryRequirements.alignment,
                prefersDedicatedAllocation,
                VkImage.Null,
                _deviceBuffer);
            _memory = memoryToken;
            result = vkBindBufferMemory(gd.Device, _deviceBuffer, _memory.DeviceMemory, _memory.Offset);
            CheckResult(result);

            RefCount = new ResourceRefCount(DisposeCore);
        }

        #region Disposal

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        #endregion

        private void DisposeCore()
        {
            if (!_destroyed)
            {
                _destroyed = true;
                vkDestroyBuffer(_gd.Device, _deviceBuffer, null);
                _gd.MemoryManager.Free(Memory);
            }
        }
    }
}
