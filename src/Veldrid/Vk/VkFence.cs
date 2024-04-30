using Vulkan;
using static Vulkan.VulkanNative;

namespace Veldrid.Vk
{
    internal unsafe class VkFence : Fence
    {
        public Vulkan.VkFence DeviceFence => _fence;

        public override bool Signaled => vkGetFenceStatus(_gd.Device, _fence) == VkResult.Success;
        public override bool IsDisposed => _destroyed;

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
        private readonly Vulkan.VkFence _fence;
        private string _name;
        private bool _destroyed;

        public VkFence(VkGraphicsDevice gd, bool signaled)
        {
            _gd = gd;
            var fenceCI = VkFenceCreateInfo.New();
            fenceCI.flags = signaled ? VkFenceCreateFlags.Signaled : VkFenceCreateFlags.None;
            var result = vkCreateFence(_gd.Device, ref fenceCI, null, out _fence);
            VulkanUtil.CheckResult(result);
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_destroyed)
            {
                vkDestroyFence(_gd.Device, _fence, null);
                _destroyed = true;
            }
        }

        #endregion

        public override void Reset()
        {
            _gd.ResetFence(this);
        }
    }
}
