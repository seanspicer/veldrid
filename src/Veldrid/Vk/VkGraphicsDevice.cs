using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vulkan;
using static Veldrid.Vk.VulkanUtil;
using static Vulkan.VulkanNative;

namespace Veldrid.Vk
{
    internal unsafe class VkGraphicsDevice : GraphicsDevice
    {
        public override string DeviceName => _deviceName;

        public override string VendorName => _vendorName;

        public override GraphicsApiVersion ApiVersion => _apiVersion;

        public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

        public override bool IsUvOriginTopLeft => true;

        public override bool IsDepthRangeZeroToOne => true;

        public override bool IsClipSpaceYInverted => !_standardClipYDirection;

        public override Swapchain MainSwapchain => _mainSwapchain;

        public override GraphicsDeviceFeatures Features { get; }

        public VkInstance Instance => _instance;
        public VkDevice Device => _device;
        public VkPhysicalDevice PhysicalDevice { get; private set; }

        public VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties => _physicalDeviceMemProperties;
        public VkQueue GraphicsQueue => _graphicsQueue;
        public uint GraphicsQueueIndex { get; private set; }

        public uint PresentQueueIndex { get; private set; }

        public string DriverName { get; private set; }

        public string DriverInfo { get; private set; }

        public VkDeviceMemoryManager MemoryManager { get; }

        public VkDescriptorPoolManager DescriptorPoolManager { get; private set; }

        public vkCmdDebugMarkerBeginEXT_t MarkerBegin { get; private set; }

        public vkCmdDebugMarkerEndEXT_t MarkerEnd { get; private set; }

        public vkCmdDebugMarkerInsertEXT_t MarkerInsert { get; private set; }

        public vkGetBufferMemoryRequirements2_t GetBufferMemoryRequirements2 { get; private set; }

        public vkGetImageMemoryRequirements2_t GetImageMemoryRequirements2 { get; private set; }

        public vkCreateMetalSurfaceEXT_t CreateMetalSurfaceEXT { get; private set; }

        public override ResourceFactory ResourceFactory { get; }
        private static readonly FixedUtf8String s_name = "Veldrid-VkGraphicsDevice";
        private static readonly Lazy<bool> s_isSupported = new Lazy<bool>(CheckIsSupported, true);
        private readonly object _graphicsCommandPoolLock = new object();
        private readonly object _graphicsQueueLock = new object();
        private readonly ConcurrentDictionary<VkFormat, VkFilter> _filters = new ConcurrentDictionary<VkFormat, VkFilter>();
        private readonly BackendInfoVulkan _vulkanInfo;

        private const int SharedCommandPoolCount = 4;

        // Staging Resources
        private const uint MinStagingBufferSize = 64;
        private const uint MaxStagingBufferSize = 512;

        private readonly object _stagingResourcesLock = new object();
        private readonly List<VkTexture> _availableStagingTextures = new List<VkTexture>();
        private readonly List<VkBuffer> _availableStagingBuffers = new List<VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, VkTexture> _submittedStagingTextures
            = new Dictionary<VkCommandBuffer, VkTexture>();

        private readonly Dictionary<VkCommandBuffer, VkBuffer> _submittedStagingBuffers
            = new Dictionary<VkCommandBuffer, VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, SharedCommandPool> _submittedSharedCommandPools
            = new Dictionary<VkCommandBuffer, SharedCommandPool>();

        private readonly object _submittedFencesLock = new object();
        private readonly ConcurrentQueue<Vulkan.VkFence> _availableSubmissionFences = new ConcurrentQueue<Vulkan.VkFence>();
        private readonly List<FenceSubmissionInfo> _submittedFences = new List<FenceSubmissionInfo>();
        private readonly VkSwapchain _mainSwapchain;

        private readonly List<FixedUtf8String> _surfaceExtensions = new List<FixedUtf8String>();

        private VkInstance _instance;
        private string _deviceName;
        private string _vendorName;
        private GraphicsApiVersion _apiVersion;
        private VkPhysicalDeviceProperties _physicalDeviceProperties;
        private VkPhysicalDeviceFeatures _physicalDeviceFeatures;
        private VkPhysicalDeviceMemoryProperties _physicalDeviceMemProperties;
        private VkDevice _device;
        private VkCommandPool _graphicsCommandPool;
        private VkQueue _graphicsQueue;
        private VkDebugReportCallbackEXT _debugCallbackHandle;
        private PFN_vkDebugReportCallbackEXT _debugCallbackFunc;
        private bool _debugMarkerEnabled;
        private vkDebugMarkerSetObjectNameEXT_t _setObjectNameDelegate;
        private readonly Stack<SharedCommandPool> _sharedGraphicsCommandPools = new Stack<SharedCommandPool>();
        private bool _standardValidationSupported;
        private bool _khronosValidationSupported;
        private bool _standardClipYDirection;
        private vkGetPhysicalDeviceProperties2_t _getPhysicalDeviceProperties2;

        public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc)
            : this(options, scDesc, new VulkanDeviceOptions())
        {
        }

        public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc, VulkanDeviceOptions vkOptions)
        {
            CreateInstance(options.Debug, vkOptions);

            var surface = VkSurfaceKHR.Null;
            if (scDesc != null) surface = VkSurfaceUtil.CreateSurface(this, _instance, scDesc.Value.Source);

            CreatePhysicalDevice();
            CreateLogicalDevice(surface, options.PreferStandardClipSpaceYDirection, vkOptions);

            MemoryManager = new VkDeviceMemoryManager(
                _device,
                PhysicalDevice,
                _physicalDeviceProperties.limits.bufferImageGranularity,
                GetBufferMemoryRequirements2,
                GetImageMemoryRequirements2);

            Features = new GraphicsDeviceFeatures(
                true,
                _physicalDeviceFeatures.geometryShader,
                _physicalDeviceFeatures.tessellationShader,
                _physicalDeviceFeatures.multiViewport,
                true,
                true,
                true,
                true,
                _physicalDeviceFeatures.drawIndirectFirstInstance,
                _physicalDeviceFeatures.fillModeNonSolid,
                _physicalDeviceFeatures.samplerAnisotropy,
                _physicalDeviceFeatures.depthClamp,
                true,
                _physicalDeviceFeatures.independentBlend,
                true,
                true,
                _debugMarkerEnabled,
                true,
                _physicalDeviceFeatures.shaderFloat64);

            ResourceFactory = new VkResourceFactory(this);

            if (scDesc != null)
            {
                var desc = scDesc.Value;
                _mainSwapchain = new VkSwapchain(this, ref desc, surface);
            }

            CreateDescriptorPool();
            CreateGraphicsCommandPool();
            for (int i = 0; i < SharedCommandPoolCount; i++) _sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));

            _vulkanInfo = new BackendInfoVulkan(this);

            PostDeviceCreated();
        }

        public override bool GetVulkanInfo(out BackendInfoVulkan info)
        {
            info = _vulkanInfo;
            return true;
        }

        public bool HasSurfaceExtension(FixedUtf8String extension)
        {
            return _surfaceExtensions.Contains(extension);
        }

        public void EnableDebugCallback(VkDebugReportFlagsEXT flags = VkDebugReportFlagsEXT.WarningEXT | VkDebugReportFlagsEXT.ErrorEXT)
        {
            Debug.WriteLine("Enabling Vulkan Debug callbacks.");
            _debugCallbackFunc = DebugCallback;
            IntPtr debugFunctionPtr = Marshal.GetFunctionPointerForDelegate(_debugCallbackFunc);
            var debugCallbackCI = VkDebugReportCallbackCreateInfoEXT.New();
            debugCallbackCI.flags = flags;
            debugCallbackCI.pfnCallback = debugFunctionPtr;
            IntPtr createFnPtr;
            using (FixedUtf8String debugExtFnName = "vkCreateDebugReportCallbackEXT") createFnPtr = vkGetInstanceProcAddr(_instance, debugExtFnName);

            if (createFnPtr == IntPtr.Zero) return;

            var createDelegate = Marshal.GetDelegateForFunctionPointer<vkCreateDebugReportCallbackEXT_d>(createFnPtr);
            var result = createDelegate(_instance, &debugCallbackCI, IntPtr.Zero, out _debugCallbackHandle);
            CheckResult(result);
        }

        public VkExtensionProperties[] GetDeviceExtensionProperties()
        {
            uint propertyCount = 0;
            var result = vkEnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, null);
            CheckResult(result);
            var props = new VkExtensionProperties[(int)propertyCount];

            fixed (VkExtensionProperties* properties = props)
            {
                result = vkEnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, properties);
                CheckResult(result);
            }

            return props;
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            var usageFlags = VkImageUsageFlags.Sampled;
            usageFlags |= depthFormat ? VkImageUsageFlags.DepthStencilAttachment : VkImageUsageFlags.ColorAttachment;

            vkGetPhysicalDeviceImageFormatProperties(
                PhysicalDevice,
                VkFormats.VdToVkPixelFormat(format),
                VkImageType.Image2D,
                VkImageTiling.Optimal,
                usageFlags,
                VkImageCreateFlags.None,
                out var formatProperties);

            var vkSampleCounts = formatProperties.sampleCounts;
            if ((vkSampleCounts & VkSampleCountFlags.Count32) == VkSampleCountFlags.Count32)
                return TextureSampleCount.Count32;
            if ((vkSampleCounts & VkSampleCountFlags.Count16) == VkSampleCountFlags.Count16)
                return TextureSampleCount.Count16;
            if ((vkSampleCounts & VkSampleCountFlags.Count8) == VkSampleCountFlags.Count8)
                return TextureSampleCount.Count8;
            if ((vkSampleCounts & VkSampleCountFlags.Count4) == VkSampleCountFlags.Count4)
                return TextureSampleCount.Count4;
            if ((vkSampleCounts & VkSampleCountFlags.Count2) == VkSampleCountFlags.Count2) return TextureSampleCount.Count2;

            return TextureSampleCount.Count1;
        }

        public override void ResetFence(Fence fence)
        {
            var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            vkResetFences(_device, 1, ref vkFence);
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            var result = vkWaitForFences(_device, 1, ref vkFence, true, nanosecondTimeout);
            return result == VkResult.Success;
        }

        public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
        {
            int fenceCount = fences.Length;
            var fencesPtr = stackalloc Vulkan.VkFence[fenceCount];
            for (int i = 0; i < fenceCount; i++) fencesPtr[i] = Util.AssertSubtype<Fence, VkFence>(fences[i]).DeviceFence;

            var result = vkWaitForFences(_device, (uint)fenceCount, fencesPtr, waitAll, nanosecondTimeout);
            return result == VkResult.Success;
        }

        internal static bool IsSupported()
        {
            return s_isSupported.Value;
        }

        internal void SetResourceName(DeviceResource resource, string name)
        {
            if (_debugMarkerEnabled)
            {
                switch (resource)
                {
                    case VkBuffer buffer:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.BufferEXT, buffer.DeviceBuffer.Handle, name);
                        break;

                    case VkCommandList commandList:
                        SetDebugMarkerName(
                            VkDebugReportObjectTypeEXT.CommandBufferEXT,
                            (ulong)commandList.CommandBuffer.Handle,
                            string.Format("{0}_CommandBuffer", name));
                        SetDebugMarkerName(
                            VkDebugReportObjectTypeEXT.CommandPoolEXT,
                            commandList.CommandPool.Handle,
                            string.Format("{0}_CommandPool", name));
                        break;

                    case VkFramebuffer framebuffer:
                        SetDebugMarkerName(
                            VkDebugReportObjectTypeEXT.FramebufferEXT,
                            framebuffer.CurrentFramebuffer.Handle,
                            name);
                        break;

                    case VkPipeline pipeline:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.PipelineEXT, pipeline.DevicePipeline.Handle, name);
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.PipelineLayoutEXT, pipeline.PipelineLayout.Handle, name);
                        break;

                    case VkResourceLayout resourceLayout:
                        SetDebugMarkerName(
                            VkDebugReportObjectTypeEXT.DescriptorSetLayoutEXT,
                            resourceLayout.DescriptorSetLayout.Handle,
                            name);
                        break;

                    case VkResourceSet resourceSet:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.DescriptorSetEXT, resourceSet.DescriptorSet.Handle, name);
                        break;

                    case VkSampler sampler:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.SamplerEXT, sampler.DeviceSampler.Handle, name);
                        break;

                    case VkShader shader:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.ShaderModuleEXT, shader.ShaderModule.Handle, name);
                        break;

                    case VkTexture tex:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.ImageEXT, tex.OptimalDeviceImage.Handle, name);
                        break;

                    case VkTextureView texView:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.ImageViewEXT, texView.ImageView.Handle, name);
                        break;

                    case VkFence fence:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.FenceEXT, fence.DeviceFence.Handle, name);
                        break;

                    case VkSwapchain sc:
                        SetDebugMarkerName(VkDebugReportObjectTypeEXT.SwapchainKHREXT, sc.DeviceSwapchain.Handle, name);
                        break;
                }
            }
        }

        internal VkFilter GetFormatFilter(VkFormat format)
        {
            if (!_filters.TryGetValue(format, out var filter))
            {
                vkGetPhysicalDeviceFormatProperties(PhysicalDevice, format, out var vkFormatProps);
                filter = (vkFormatProps.optimalTilingFeatures & VkFormatFeatureFlags.SampledImageFilterLinear) != 0
                    ? VkFilter.Linear
                    : VkFilter.Nearest;
                _filters.TryAdd(format, filter);
            }

            return filter;
        }

        internal void ClearColorTexture(VkTexture texture, VkClearColorValue color)
        {
            uint effectiveLayers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0) effectiveLayers *= 6;
            var range = new VkImageSubresourceRange(
                VkImageAspectFlags.Color,
                0,
                texture.MipLevels,
                0,
                effectiveLayers);
            var pool = GetFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.TransferDstOptimal);
            vkCmdClearColorImage(cb, texture.OptimalDeviceImage, VkImageLayout.TransferDstOptimal, &color, 1, &range);
            var colorLayout = texture.IsSwapchainTexture ? VkImageLayout.PresentSrcKHR : VkImageLayout.ColorAttachmentOptimal;
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, colorLayout);
            pool.EndAndSubmit(cb);
        }

        internal void ClearDepthTexture(VkTexture texture, VkClearDepthStencilValue clearValue)
        {
            uint effectiveLayers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0) effectiveLayers *= 6;
            var aspect = FormatHelpers.IsStencilFormat(texture.Format)
                ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                : VkImageAspectFlags.Depth;
            var range = new VkImageSubresourceRange(
                aspect,
                0,
                texture.MipLevels,
                0,
                effectiveLayers);
            var pool = GetFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.TransferDstOptimal);
            vkCmdClearDepthStencilImage(
                cb,
                texture.OptimalDeviceImage,
                VkImageLayout.TransferDstOptimal,
                &clearValue,
                1,
                &range);
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.DepthStencilAttachmentOptimal);
            pool.EndAndSubmit(cb);
        }

        internal override uint GetUniformBufferMinOffsetAlignmentCore()
        {
            return (uint)_physicalDeviceProperties.limits.minUniformBufferOffsetAlignment;
        }

        internal override uint GetStructuredBufferMinOffsetAlignmentCore()
        {
            return (uint)_physicalDeviceProperties.limits.minStorageBufferOffsetAlignment;
        }

        internal void TransitionImageLayout(VkTexture texture, VkImageLayout layout)
        {
            var pool = GetFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, texture.ActualArrayLayers, layout);
            pool.EndAndSubmit(cb);
        }

        protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
        {
            var memoryBlock = default(VkMemoryBlock);
            IntPtr mappedPtr = IntPtr.Zero;
            uint sizeInBytes;
            uint offset = 0;
            uint rowPitch = 0;
            uint depthPitch = 0;

            if (resource is VkBuffer buffer)
            {
                memoryBlock = buffer.Memory;
                sizeInBytes = buffer.SizeInBytes;
            }
            else
            {
                var texture = Util.AssertSubtype<MappableResource, VkTexture>(resource);
                var layout = texture.GetSubresourceLayout(subresource);
                memoryBlock = texture.Memory;
                sizeInBytes = (uint)layout.size;
                offset = (uint)layout.offset;
                rowPitch = (uint)layout.rowPitch;
                depthPitch = (uint)layout.depthPitch;
            }

            if (memoryBlock.DeviceMemory.Handle != 0)
            {
                if (memoryBlock.IsPersistentMapped)
                    mappedPtr = (IntPtr)memoryBlock.BlockMappedPointer;
                else
                    mappedPtr = MemoryManager.Map(memoryBlock);
            }

            byte* dataPtr = (byte*)mappedPtr.ToPointer() + offset;
            return new MappedResource(
                resource,
                mode,
                (IntPtr)dataPtr,
                sizeInBytes,
                subresource,
                rowPitch,
                depthPitch);
        }

        protected override void UnmapCore(MappableResource resource, uint subresource)
        {
            var memoryBlock = default(VkMemoryBlock);

            if (resource is VkBuffer buffer)
                memoryBlock = buffer.Memory;
            else
            {
                var tex = Util.AssertSubtype<MappableResource, VkTexture>(resource);
                memoryBlock = tex.Memory;
            }

            if (memoryBlock.DeviceMemory.Handle != 0 && !memoryBlock.IsPersistentMapped) vkUnmapMemory(_device, memoryBlock.DeviceMemory);
        }

        protected override void PlatformDispose()
        {
            Debug.Assert(_submittedFences.Count == 0);
            foreach (var fence in _availableSubmissionFences) vkDestroyFence(_device, fence, null);

            _mainSwapchain?.Dispose();

            if (_debugCallbackFunc != null)
            {
                _debugCallbackFunc = null;
                FixedUtf8String debugExtFnName = "vkDestroyDebugReportCallbackEXT";
                IntPtr destroyFuncPtr = vkGetInstanceProcAddr(_instance, debugExtFnName);
                var destroyDel
                    = Marshal.GetDelegateForFunctionPointer<vkDestroyDebugReportCallbackEXT_d>(destroyFuncPtr);
                destroyDel(_instance, _debugCallbackHandle, null);
            }

            DescriptorPoolManager.DestroyAll();
            vkDestroyCommandPool(_device, _graphicsCommandPool, null);

            Debug.Assert(_submittedStagingTextures.Count == 0);
            foreach (var tex in _availableStagingTextures) tex.Dispose();

            Debug.Assert(_submittedStagingBuffers.Count == 0);
            foreach (var buffer in _availableStagingBuffers) buffer.Dispose();

            lock (_graphicsCommandPoolLock)
            {
                while (_sharedGraphicsCommandPools.Count > 0)
                {
                    var sharedPool = _sharedGraphicsCommandPools.Pop();
                    sharedPool.Destroy();
                }
            }

            MemoryManager.Dispose();

            var result = vkDeviceWaitIdle(_device);
            CheckResult(result);
            vkDestroyDevice(_device, null);
            vkDestroyInstance(_instance, null);
        }

        private static bool CheckIsSupported()
        {
            if (!IsVulkanLoaded()) return false;

            var instanceCI = VkInstanceCreateInfo.New();
            var applicationInfo = new VkApplicationInfo();
            applicationInfo.apiVersion = new VkVersion(1, 0, 0);
            applicationInfo.applicationVersion = new VkVersion(1, 0, 0);
            applicationInfo.engineVersion = new VkVersion(1, 0, 0);
            applicationInfo.pApplicationName = s_name;
            applicationInfo.pEngineName = s_name;

            instanceCI.pApplicationInfo = &applicationInfo;

            var result = vkCreateInstance(ref instanceCI, null, out var testInstance);
            if (result != VkResult.Success) return false;

            uint physicalDeviceCount = 0;
            result = vkEnumeratePhysicalDevices(testInstance, ref physicalDeviceCount, null);

            if (result != VkResult.Success || physicalDeviceCount == 0)
            {
                vkDestroyInstance(testInstance, null);
                return false;
            }

            vkDestroyInstance(testInstance, null);

            var instanceExtensions = new HashSet<string>(GetInstanceExtensions());
            if (!instanceExtensions.Contains(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME)) return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return instanceExtensions.Contains(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
#if NET5_0_OR_GREATER

            if (OperatingSystem.IsAndroid())
                return instanceExtensions.Contains(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
#endif

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (RuntimeInformation.OSDescription.Contains("Unix")) // Android
                    return instanceExtensions.Contains(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);
                return instanceExtensions.Contains(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.OSDescription.Contains("Darwin")) // macOS
                    return instanceExtensions.Contains(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME);
                // iOS
                return instanceExtensions.Contains(CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME);
            }

            return false;
        }

        private void SubmitCommandList(
            CommandList cl,
            uint waitSemaphoreCount,
            VkSemaphore* waitSemaphoresPtr,
            uint signalSemaphoreCount,
            VkSemaphore* signalSemaphoresPtr,
            Fence fence)
        {
            var vkCL = Util.AssertSubtype<CommandList, VkCommandList>(cl);
            var vkCB = vkCL.CommandBuffer;

            vkCL.CommandBufferSubmitted(vkCB);
            SubmitCommandBuffer(vkCL, vkCB, waitSemaphoreCount, waitSemaphoresPtr, signalSemaphoreCount, signalSemaphoresPtr, fence);
        }

        private void SubmitCommandBuffer(
            VkCommandList vkCL,
            VkCommandBuffer vkCB,
            uint waitSemaphoreCount,
            VkSemaphore* waitSemaphoresPtr,
            uint signalSemaphoreCount,
            VkSemaphore* signalSemaphoresPtr,
            Fence fence)
        {
            CheckSubmittedFences();

            bool useExtraFence = fence != null;
            var si = VkSubmitInfo.New();
            si.commandBufferCount = 1;
            si.pCommandBuffers = &vkCB;
            var waitDstStageMask = VkPipelineStageFlags.ColorAttachmentOutput;
            si.pWaitDstStageMask = &waitDstStageMask;

            si.pWaitSemaphores = waitSemaphoresPtr;
            si.waitSemaphoreCount = waitSemaphoreCount;
            si.pSignalSemaphores = signalSemaphoresPtr;
            si.signalSemaphoreCount = signalSemaphoreCount;

            var vkFence = Vulkan.VkFence.Null;
            var submissionFence = Vulkan.VkFence.Null;

            if (useExtraFence)
            {
                vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
                submissionFence = GetFreeSubmissionFence();
            }
            else
            {
                vkFence = GetFreeSubmissionFence();
                submissionFence = vkFence;
            }

            lock (_graphicsQueueLock)
            {
                var result = vkQueueSubmit(_graphicsQueue, 1, ref si, vkFence);
                CheckResult(result);

                if (useExtraFence)
                {
                    result = vkQueueSubmit(_graphicsQueue, 0, null, submissionFence);
                    CheckResult(result);
                }
            }

            lock (_submittedFencesLock) _submittedFences.Add(new FenceSubmissionInfo(submissionFence, vkCL, vkCB));
        }

        private void CheckSubmittedFences()
        {
            lock (_submittedFencesLock)
            {
                for (int i = 0; i < _submittedFences.Count; i++)
                {
                    var fsi = _submittedFences[i];

                    if (vkGetFenceStatus(_device, fsi.Fence) == VkResult.Success)
                    {
                        CompleteFenceSubmission(fsi);
                        _submittedFences.RemoveAt(i);
                        i -= 1;
                    }
                    else
                        break; // Submissions are in order; later submissions cannot complete if this one hasn't.
                }
            }
        }

        private void CompleteFenceSubmission(FenceSubmissionInfo fsi)
        {
            var fence = fsi.Fence;
            var completedCB = fsi.CommandBuffer;
            fsi.CommandList?.CommandBufferCompleted(completedCB);
            var resetResult = vkResetFences(_device, 1, ref fence);
            CheckResult(resetResult);
            ReturnSubmissionFence(fence);

            lock (_stagingResourcesLock)
            {
                if (_submittedStagingTextures.TryGetValue(completedCB, out var stagingTex))
                {
                    _submittedStagingTextures.Remove(completedCB);
                    _availableStagingTextures.Add(stagingTex);
                }

                if (_submittedStagingBuffers.TryGetValue(completedCB, out var stagingBuffer))
                {
                    _submittedStagingBuffers.Remove(completedCB);
                    if (stagingBuffer.SizeInBytes <= MaxStagingBufferSize)
                        _availableStagingBuffers.Add(stagingBuffer);
                    else
                        stagingBuffer.Dispose();
                }

                if (_submittedSharedCommandPools.TryGetValue(completedCB, out var sharedPool))
                {
                    _submittedSharedCommandPools.Remove(completedCB);

                    lock (_graphicsCommandPoolLock)
                    {
                        if (sharedPool.IsCached)
                            _sharedGraphicsCommandPools.Push(sharedPool);
                        else
                            sharedPool.Destroy();
                    }
                }
            }
        }

        private void ReturnSubmissionFence(Vulkan.VkFence fence)
        {
            _availableSubmissionFences.Enqueue(fence);
        }

        private Vulkan.VkFence GetFreeSubmissionFence()
        {
            if (_availableSubmissionFences.TryDequeue(out var availableFence))
                return availableFence;
            var fenceCI = VkFenceCreateInfo.New();
            var result = vkCreateFence(_device, ref fenceCI, null, out var newFence);
            CheckResult(result);
            return newFence;
        }

        private void SetDebugMarkerName(VkDebugReportObjectTypeEXT type, ulong target, string name)
        {
            Debug.Assert(_setObjectNameDelegate != null);

            var nameInfo = VkDebugMarkerObjectNameInfoEXT.New();
            nameInfo.objectType = type;
            nameInfo.@object = target;

            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            nameInfo.pObjectName = utf8Ptr;
            var result = _setObjectNameDelegate(_device, &nameInfo);
            CheckResult(result);
        }

        private void CreateInstance(bool debug, VulkanDeviceOptions options)
        {
            var availableInstanceLayers = new HashSet<string>(EnumerateInstanceLayers());
            var availableInstanceExtensions = new HashSet<string>(GetInstanceExtensions());

            var instanceCI = VkInstanceCreateInfo.New();
            var applicationInfo = new VkApplicationInfo();
            applicationInfo.apiVersion = new VkVersion(1, 0, 0);
            applicationInfo.applicationVersion = new VkVersion(1, 0, 0);
            applicationInfo.engineVersion = new VkVersion(1, 0, 0);
            applicationInfo.pApplicationName = s_name;
            applicationInfo.pEngineName = s_name;

            instanceCI.pApplicationInfo = &applicationInfo;

            var instanceExtensions = new StackList<IntPtr, Size64Bytes>();
            var instanceLayers = new StackList<IntPtr, Size64Bytes>();

            if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_portability_subset)) _surfaceExtensions.Add(CommonStrings.VK_KHR_portability_subset);

            if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME);
            }
            else if (
#if NET5_0_OR_GREATER
                OperatingSystem.IsAndroid() ||
#endif
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME);

                if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME);

                if (availableInstanceExtensions.Contains(CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME))
                    _surfaceExtensions.Add(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                else // Legacy MoltenVK extensions
                {
                    if (availableInstanceExtensions.Contains(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME);

                    if (availableInstanceExtensions.Contains(CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME)) _surfaceExtensions.Add(CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME);
                }
            }

            foreach (var ext in _surfaceExtensions) instanceExtensions.Add(ext);

            bool hasDeviceProperties2 = availableInstanceExtensions.Contains(CommonStrings.VK_KHR_get_physical_device_properties2);
            if (hasDeviceProperties2) instanceExtensions.Add(CommonStrings.VK_KHR_get_physical_device_properties2);

            string[] requestedInstanceExtensions = options.InstanceExtensions ?? Array.Empty<string>();
            var tempStrings = new List<FixedUtf8String>();

            foreach (string requiredExt in requestedInstanceExtensions)
            {
                if (!availableInstanceExtensions.Contains(requiredExt)) throw new VeldridException($"The required instance extension was not available: {requiredExt}");

                var utf8Str = new FixedUtf8String(requiredExt);
                instanceExtensions.Add(utf8Str);
                tempStrings.Add(utf8Str);
            }

            bool debugReportExtensionAvailable = false;

            if (debug)
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VK_EXT_DEBUG_REPORT_EXTENSION_NAME))
                {
                    debugReportExtensionAvailable = true;
                    instanceExtensions.Add(CommonStrings.VK_EXT_DEBUG_REPORT_EXTENSION_NAME);
                }

                if (availableInstanceLayers.Contains(CommonStrings.StandardValidationLayerName))
                {
                    _standardValidationSupported = true;
                    instanceLayers.Add(CommonStrings.StandardValidationLayerName);
                }

                if (availableInstanceLayers.Contains(CommonStrings.KhronosValidationLayerName))
                {
                    _khronosValidationSupported = true;
                    instanceLayers.Add(CommonStrings.KhronosValidationLayerName);
                }
            }

            instanceCI.enabledExtensionCount = instanceExtensions.Count;
            instanceCI.ppEnabledExtensionNames = (byte**)instanceExtensions.Data;

            instanceCI.enabledLayerCount = instanceLayers.Count;
            if (instanceLayers.Count > 0) instanceCI.ppEnabledLayerNames = (byte**)instanceLayers.Data;

            var result = vkCreateInstance(ref instanceCI, null, out _instance);
            CheckResult(result);

            if (HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME)) CreateMetalSurfaceEXT = GetInstanceProcAddr<vkCreateMetalSurfaceEXT_t>("vkCreateMetalSurfaceEXT");

            if (debug && debugReportExtensionAvailable) EnableDebugCallback();

            if (hasDeviceProperties2)
            {
                _getPhysicalDeviceProperties2 = GetInstanceProcAddr<vkGetPhysicalDeviceProperties2_t>("vkGetPhysicalDeviceProperties2")
                                                ?? GetInstanceProcAddr<vkGetPhysicalDeviceProperties2_t>("vkGetPhysicalDeviceProperties2KHR");
            }

            foreach (var tempStr in tempStrings) tempStr.Dispose();
        }

        private uint DebugCallback(
            uint flags,
            VkDebugReportObjectTypeEXT objectType,
            ulong @object,
            UIntPtr location,
            int messageCode,
            byte* pLayerPrefix,
            byte* pMessage,
            void* pUserData)
        {
            string message = Util.GetString(pMessage);
            var debugReportFlags = (VkDebugReportFlagsEXT)flags;

#if DEBUG
            if (Debugger.IsAttached) Debugger.Break();
#endif

            string fullMessage = $"[{debugReportFlags}] ({objectType}) {message}";

            if (debugReportFlags == VkDebugReportFlagsEXT.ErrorEXT) throw new VeldridException("A Vulkan validation error was encountered: " + fullMessage);

            Console.WriteLine(fullMessage);
            return 0;
        }

        private void CreatePhysicalDevice()
        {
            uint deviceCount = 0;
            vkEnumeratePhysicalDevices(_instance, ref deviceCount, null);
            if (deviceCount == 0) throw new InvalidOperationException("No physical devices exist.");

            var physicalDevices = new VkPhysicalDevice[deviceCount];
            vkEnumeratePhysicalDevices(_instance, ref deviceCount, ref physicalDevices[0]);
            // Just use the first one.
            PhysicalDevice = physicalDevices[0];

            vkGetPhysicalDeviceProperties(PhysicalDevice, out _physicalDeviceProperties);
            fixed (byte* utf8NamePtr = _physicalDeviceProperties.deviceName) _deviceName = Encoding.UTF8.GetString(utf8NamePtr, (int)MaxPhysicalDeviceNameSize).TrimEnd('\0');

            _vendorName = "id:" + _physicalDeviceProperties.vendorID.ToString("x8");
            _apiVersion = GraphicsApiVersion.Unknown;
            DriverInfo = "version:" + _physicalDeviceProperties.driverVersion.ToString("x8");

            vkGetPhysicalDeviceFeatures(PhysicalDevice, out _physicalDeviceFeatures);

            vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out _physicalDeviceMemProperties);
        }

        private void CreateLogicalDevice(VkSurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
        {
            GetQueueFamilyIndices(surface);

            var familyIndices = new HashSet<uint> { GraphicsQueueIndex, PresentQueueIndex };
            var queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[familyIndices.Count];
            uint queueCreateInfosCount = (uint)familyIndices.Count;

            int i = 0;

            foreach (uint index in familyIndices)
            {
                var queueCreateInfo = VkDeviceQueueCreateInfo.New();
                queueCreateInfo.queueFamilyIndex = GraphicsQueueIndex;
                queueCreateInfo.queueCount = 1;
                float priority = 1f;
                queueCreateInfo.pQueuePriorities = &priority;
                queueCreateInfos[i] = queueCreateInfo;
                i += 1;
            }

            var deviceFeatures = _physicalDeviceFeatures;

            var props = GetDeviceExtensionProperties();

            var requiredInstanceExtensions = new HashSet<string>(options.DeviceExtensions ?? Array.Empty<string>());

            bool hasMemReqs2 = false;
            bool hasDedicatedAllocation = false;
            bool hasDriverProperties = false;
            IntPtr[] activeExtensions = new IntPtr[props.Length];
            uint activeExtensionCount = 0;

            fixed (VkExtensionProperties* properties = props)
            {
                for (int property = 0; property < props.Length; property++)
                {
                    string extensionName = Util.GetString(properties[property].extensionName);

                    if (extensionName == "VK_EXT_debug_marker")
                    {
                        activeExtensions[activeExtensionCount++] = CommonStrings.VK_EXT_DEBUG_MARKER_EXTENSION_NAME;
                        requiredInstanceExtensions.Remove(extensionName);
                        _debugMarkerEnabled = true;
                    }
                    else if (extensionName == "VK_KHR_swapchain")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                    }
                    else if (preferStandardClipY && extensionName == "VK_KHR_maintenance1")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        _standardClipYDirection = true;
                    }
                    else if (extensionName == "VK_KHR_get_memory_requirements2")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasMemReqs2 = true;
                    }
                    else if (extensionName == "VK_KHR_dedicated_allocation")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDedicatedAllocation = true;
                    }
                    else if (extensionName == "VK_KHR_driver_properties")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDriverProperties = true;
                    }
                    else if (extensionName == CommonStrings.VK_KHR_portability_subset)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                    }
                    else if (requiredInstanceExtensions.Remove(extensionName)) activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                }
            }

            if (requiredInstanceExtensions.Count != 0)
            {
                string missingList = string.Join(", ", requiredInstanceExtensions);
                throw new VeldridException(
                    $"The following Vulkan device extensions were not available: {missingList}");
            }

            var deviceCreateInfo = VkDeviceCreateInfo.New();
            deviceCreateInfo.queueCreateInfoCount = queueCreateInfosCount;
            deviceCreateInfo.pQueueCreateInfos = queueCreateInfos;

            deviceCreateInfo.pEnabledFeatures = &deviceFeatures;

            var layerNames = new StackList<IntPtr>();
            if (_standardValidationSupported) layerNames.Add(CommonStrings.StandardValidationLayerName);

            if (_khronosValidationSupported) layerNames.Add(CommonStrings.KhronosValidationLayerName);
            deviceCreateInfo.enabledLayerCount = layerNames.Count;
            deviceCreateInfo.ppEnabledLayerNames = (byte**)layerNames.Data;

            fixed (IntPtr* activeExtensionsPtr = activeExtensions)
            {
                deviceCreateInfo.enabledExtensionCount = activeExtensionCount;
                deviceCreateInfo.ppEnabledExtensionNames = (byte**)activeExtensionsPtr;

                var result = vkCreateDevice(PhysicalDevice, ref deviceCreateInfo, null, out _device);
                CheckResult(result);
            }

            vkGetDeviceQueue(_device, GraphicsQueueIndex, 0, out _graphicsQueue);

            if (_debugMarkerEnabled)
            {
                _setObjectNameDelegate = Marshal.GetDelegateForFunctionPointer<vkDebugMarkerSetObjectNameEXT_t>(
                    GetInstanceProcAddr("vkDebugMarkerSetObjectNameEXT"));
                MarkerBegin = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerBeginEXT_t>(
                    GetInstanceProcAddr("vkCmdDebugMarkerBeginEXT"));
                MarkerEnd = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerEndEXT_t>(
                    GetInstanceProcAddr("vkCmdDebugMarkerEndEXT"));
                MarkerInsert = Marshal.GetDelegateForFunctionPointer<vkCmdDebugMarkerInsertEXT_t>(
                    GetInstanceProcAddr("vkCmdDebugMarkerInsertEXT"));
            }

            if (hasDedicatedAllocation && hasMemReqs2)
            {
                GetBufferMemoryRequirements2 = GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2")
                                               ?? GetDeviceProcAddr<vkGetBufferMemoryRequirements2_t>("vkGetBufferMemoryRequirements2KHR");
                GetImageMemoryRequirements2 = GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2")
                                              ?? GetDeviceProcAddr<vkGetImageMemoryRequirements2_t>("vkGetImageMemoryRequirements2KHR");
            }

            if (_getPhysicalDeviceProperties2 != null && hasDriverProperties)
            {
                var deviceProps = VkPhysicalDeviceProperties2KHR.New();
                var driverProps = VkPhysicalDeviceDriverProperties.New();

                deviceProps.pNext = &driverProps;
                _getPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);

                string driverName = Encoding.UTF8.GetString(
                    driverProps.driverName, VkPhysicalDeviceDriverProperties.DriverNameLength).TrimEnd('\0');

                string driverInfo = Encoding.UTF8.GetString(
                    driverProps.driverInfo, VkPhysicalDeviceDriverProperties.DriverInfoLength).TrimEnd('\0');

                var conforming = driverProps.conformanceVersion;
                _apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
                DriverName = driverName;
                DriverInfo = driverInfo;
            }
        }

        private IntPtr GetInstanceProcAddr(string name)
        {
            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];

            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            return vkGetInstanceProcAddr(_instance, utf8Ptr);
        }

        private T GetInstanceProcAddr<T>(string name)
        {
            IntPtr funcPtr = GetInstanceProcAddr(name);
            if (funcPtr != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
            return default;
        }

        private IntPtr GetDeviceProcAddr(string name)
        {
            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];

            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            return vkGetDeviceProcAddr(_device, utf8Ptr);
        }

        private T GetDeviceProcAddr<T>(string name)
        {
            IntPtr funcPtr = GetDeviceProcAddr(name);
            if (funcPtr != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
            return default;
        }

        private void GetQueueFamilyIndices(VkSurfaceKHR surface)
        {
            uint queueFamilyCount = 0;
            vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, null);
            var qfp = new VkQueueFamilyProperties[queueFamilyCount];
            vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, out qfp[0]);

            bool foundGraphics = false;
            bool foundPresent = surface == VkSurfaceKHR.Null;

            for (uint i = 0; i < qfp.Length; i++)
            {
                if ((qfp[i].queueFlags & VkQueueFlags.Graphics) != 0)
                {
                    GraphicsQueueIndex = i;
                    foundGraphics = true;
                }

                if (!foundPresent)
                {
                    vkGetPhysicalDeviceSurfaceSupportKHR(PhysicalDevice, i, surface, out var presentSupported);

                    if (presentSupported)
                    {
                        PresentQueueIndex = i;
                        foundPresent = true;
                    }
                }

                if (foundGraphics && foundPresent) return;
            }
        }

        private void CreateDescriptorPool()
        {
            DescriptorPoolManager = new VkDescriptorPoolManager(this);
        }

        private void CreateGraphicsCommandPool()
        {
            var commandPoolCI = VkCommandPoolCreateInfo.New();
            commandPoolCI.flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
            commandPoolCI.queueFamilyIndex = GraphicsQueueIndex;
            var result = vkCreateCommandPool(_device, ref commandPoolCI, null, out _graphicsCommandPool);
            CheckResult(result);
        }

        private SharedCommandPool GetFreeCommandPool()
        {
            SharedCommandPool sharedPool = null;

            lock (_graphicsCommandPoolLock)
            {
                if (_sharedGraphicsCommandPools.Count > 0)
                    sharedPool = _sharedGraphicsCommandPools.Pop();
            }

            if (sharedPool == null)
                sharedPool = new SharedCommandPool(this, false);

            return sharedPool;
        }

        private IntPtr MapBuffer(VkBuffer buffer, uint numBytes)
        {
            if (buffer.Memory.IsPersistentMapped)
                return (IntPtr)buffer.Memory.BlockMappedPointer;
            void* mappedPtr;
            var result = vkMapMemory(Device, buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, 0, &mappedPtr);
            CheckResult(result);
            return (IntPtr)mappedPtr;
        }

        private void UnmapBuffer(VkBuffer buffer)
        {
            if (!buffer.Memory.IsPersistentMapped) vkUnmapMemory(Device, buffer.Memory.DeviceMemory);
        }

        private VkTexture GetFreeStagingTexture(uint width, uint height, uint depth, PixelFormat format)
        {
            uint totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);

            lock (_stagingResourcesLock)
            {
                for (int i = 0; i < _availableStagingTextures.Count; i++)
                {
                    var tex = _availableStagingTextures[i];

                    if (tex.Memory.Size >= totalSize)
                    {
                        _availableStagingTextures.RemoveAt(i);
                        tex.SetStagingDimensions(width, height, depth, format);
                        return tex;
                    }
                }
            }

            uint texWidth = Math.Max(256, width);
            uint texHeight = Math.Max(256, height);
            var newTex = (VkTexture)ResourceFactory.CreateTexture(TextureDescription.Texture3D(
                texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
            newTex.SetStagingDimensions(width, height, depth, format);

            return newTex;
        }

        private VkBuffer GetFreeStagingBuffer(uint size)
        {
            lock (_stagingResourcesLock)
            {
                for (int i = 0; i < _availableStagingBuffers.Count; i++)
                {
                    var buffer = _availableStagingBuffers[i];

                    if (buffer.SizeInBytes >= size)
                    {
                        _availableStagingBuffers.RemoveAt(i);
                        return buffer;
                    }
                }
            }

            uint newBufferSize = Math.Max(MinStagingBufferSize, size);
            var newBuffer = (VkBuffer)ResourceFactory.CreateBuffer(
                new BufferDescription(newBufferSize, BufferUsage.Staging));
            return newBuffer;
        }

        private protected override void SubmitCommandsCore(CommandList cl, Fence fence)
        {
            SubmitCommandList(cl, 0, null, 0, null, fence);
        }

        private protected override void SwapBuffersCore(Swapchain swapchain)
        {
            var vkSC = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
            var deviceSwapchain = vkSC.DeviceSwapchain;
            var presentInfo = VkPresentInfoKHR.New();
            presentInfo.swapchainCount = 1;
            presentInfo.pSwapchains = &deviceSwapchain;
            uint imageIndex = vkSC.ImageIndex;
            presentInfo.pImageIndices = &imageIndex;

            object presentLock = vkSC.PresentQueueIndex == GraphicsQueueIndex ? _graphicsQueueLock : vkSC;

            lock (presentLock)
            {
                vkQueuePresentKHR(vkSC.PresentQueue, ref presentInfo);

                if (vkSC.AcquireNextImage(_device, VkSemaphore.Null, vkSC.ImageAvailableFence))
                {
                    var fence = vkSC.ImageAvailableFence;
                    vkWaitForFences(_device, 1, ref fence, true, ulong.MaxValue);
                    vkResetFences(_device, 1, ref fence);
                }
            }
        }

        private protected override void WaitForIdleCore()
        {
            lock (_graphicsQueueLock) vkQueueWaitIdle(_graphicsQueue);

            CheckSubmittedFences();
        }

        private protected override void WaitForNextFrameReadyCore()
        {
        }

        private protected override bool GetPixelFormatSupportCore(
            PixelFormat format,
            TextureType type,
            TextureUsage usage,
            out PixelFormatProperties properties)
        {
            var vkFormat = VkFormats.VdToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
            var vkType = VkFormats.VdToVkTextureType(type);
            var tiling = usage == TextureUsage.Staging ? VkImageTiling.Linear : VkImageTiling.Optimal;
            var vkUsage = VkFormats.VdToVkTextureUsage(usage);

            var result = vkGetPhysicalDeviceImageFormatProperties(
                PhysicalDevice,
                vkFormat,
                vkType,
                tiling,
                vkUsage,
                VkImageCreateFlags.None,
                out var vkProps);

            if (result == VkResult.ErrorFormatNotSupported)
            {
                properties = default;
                return false;
            }

            CheckResult(result);

            properties = new PixelFormatProperties(
                vkProps.maxExtent.width,
                vkProps.maxExtent.height,
                vkProps.maxExtent.depth,
                vkProps.maxMipLevels,
                vkProps.maxArrayLayers,
                (uint)vkProps.sampleCounts);
            return true;
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            VkBuffer copySrcVkBuffer = null;
            IntPtr mappedPtr;
            byte* destPtr;
            bool isPersistentMapped = vkBuffer.Memory.IsPersistentMapped;

            if (isPersistentMapped)
            {
                mappedPtr = (IntPtr)vkBuffer.Memory.BlockMappedPointer;
                destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
            }
            else
            {
                copySrcVkBuffer = GetFreeStagingBuffer(sizeInBytes);
                mappedPtr = (IntPtr)copySrcVkBuffer.Memory.BlockMappedPointer;
                destPtr = (byte*)mappedPtr;
            }

            Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);

            if (!isPersistentMapped)
            {
                var pool = GetFreeCommandPool();
                var cb = pool.BeginNewCommandBuffer();

                var copyRegion = new VkBufferCopy
                {
                    dstOffset = bufferOffsetInBytes,
                    size = sizeInBytes
                };
                vkCmdCopyBuffer(cb, copySrcVkBuffer.DeviceBuffer, vkBuffer.DeviceBuffer, 1, ref copyRegion);

                pool.EndAndSubmit(cb);
                lock (_stagingResourcesLock) _submittedStagingBuffers.Add(cb, copySrcVkBuffer);
            }
        }

        private protected override void UpdateTextureCore(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x,
            uint y,
            uint z,
            uint width,
            uint height,
            uint depth,
            uint mipLevel,
            uint arrayLayer)
        {
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;

            if (isStaging)
            {
                var memBlock = vkTex.Memory;
                uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
                var layout = vkTex.GetSubresourceLayout(subresource);
                byte* imageBasePtr = (byte*)memBlock.BlockMappedPointer + layout.offset;

                uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
                uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
                Util.CopyTextureRegion(
                    source.ToPointer(),
                    0, 0, 0,
                    srcRowPitch, srcDepthPitch,
                    imageBasePtr,
                    x, y, z,
                    (uint)layout.rowPitch, (uint)layout.depthPitch,
                    width, height, depth,
                    texture.Format);
            }
            else
            {
                var stagingTex = GetFreeStagingTexture(width, height, depth, texture.Format);
                UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
                var pool = GetFreeCommandPool();
                var cb = pool.BeginNewCommandBuffer();
                VkCommandList.CopyTextureCore_VkCommandBuffer(
                    cb,
                    stagingTex, 0, 0, 0, 0, 0,
                    texture, x, y, z, mipLevel, arrayLayer,
                    width, height, depth, 1);
                lock (_stagingResourcesLock) _submittedStagingTextures.Add(cb, stagingTex);
                pool.EndAndSubmit(cb);
            }
        }

        private class SharedCommandPool
        {
            public bool IsCached { get; }
            private readonly VkGraphicsDevice _gd;
            private readonly VkCommandPool _pool;
            private readonly VkCommandBuffer _cb;

            public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
            {
                _gd = gd;
                IsCached = isCached;

                var commandPoolCI = VkCommandPoolCreateInfo.New();
                commandPoolCI.flags = VkCommandPoolCreateFlags.Transient | VkCommandPoolCreateFlags.ResetCommandBuffer;
                commandPoolCI.queueFamilyIndex = _gd.GraphicsQueueIndex;
                var result = vkCreateCommandPool(_gd.Device, ref commandPoolCI, null, out _pool);
                CheckResult(result);

                var allocateInfo = VkCommandBufferAllocateInfo.New();
                allocateInfo.commandBufferCount = 1;
                allocateInfo.level = VkCommandBufferLevel.Primary;
                allocateInfo.commandPool = _pool;
                result = vkAllocateCommandBuffers(_gd.Device, ref allocateInfo, out _cb);
                CheckResult(result);
            }

            public VkCommandBuffer BeginNewCommandBuffer()
            {
                var beginInfo = VkCommandBufferBeginInfo.New();
                beginInfo.flags = VkCommandBufferUsageFlags.OneTimeSubmit;
                var result = vkBeginCommandBuffer(_cb, ref beginInfo);
                CheckResult(result);

                return _cb;
            }

            public void EndAndSubmit(VkCommandBuffer cb)
            {
                var result = vkEndCommandBuffer(cb);
                CheckResult(result);
                _gd.SubmitCommandBuffer(null, cb, 0, null, 0, null, null);
                lock (_gd._stagingResourcesLock) _gd._submittedSharedCommandPools.Add(cb, this);
            }

            internal void Destroy()
            {
                vkDestroyCommandPool(_gd.Device, _pool, null);
            }
        }

        private struct FenceSubmissionInfo
        {
            public readonly Vulkan.VkFence Fence;
            public readonly VkCommandList CommandList;
            public readonly VkCommandBuffer CommandBuffer;

            public FenceSubmissionInfo(Vulkan.VkFence fence, VkCommandList commandList, VkCommandBuffer commandBuffer)
            {
                Fence = fence;
                CommandList = commandList;
                CommandBuffer = commandBuffer;
            }
        }
    }

    internal unsafe delegate VkResult vkCreateDebugReportCallbackEXT_d(
        VkInstance instance,
        VkDebugReportCallbackCreateInfoEXT* createInfo,
        IntPtr allocatorPtr,
        out VkDebugReportCallbackEXT ret);

    internal unsafe delegate void vkDestroyDebugReportCallbackEXT_d(
        VkInstance instance,
        VkDebugReportCallbackEXT callback,
        VkAllocationCallbacks* pAllocator);

    internal unsafe delegate VkResult vkDebugMarkerSetObjectNameEXT_t(VkDevice device, VkDebugMarkerObjectNameInfoEXT* pNameInfo);

    internal unsafe delegate void vkCmdDebugMarkerBeginEXT_t(VkCommandBuffer commandBuffer, VkDebugMarkerMarkerInfoEXT* pMarkerInfo);

    internal delegate void vkCmdDebugMarkerEndEXT_t(VkCommandBuffer commandBuffer);

    internal unsafe delegate void vkCmdDebugMarkerInsertEXT_t(VkCommandBuffer commandBuffer, VkDebugMarkerMarkerInfoEXT* pMarkerInfo);

    internal unsafe delegate void vkGetBufferMemoryRequirements2_t(VkDevice device, VkBufferMemoryRequirementsInfo2KHR* pInfo, VkMemoryRequirements2KHR* pMemoryRequirements);

    internal unsafe delegate void vkGetImageMemoryRequirements2_t(VkDevice device, VkImageMemoryRequirementsInfo2KHR* pInfo, VkMemoryRequirements2KHR* pMemoryRequirements);

    internal unsafe delegate void vkGetPhysicalDeviceProperties2_t(VkPhysicalDevice physicalDevice, void* properties);

    // VK_EXT_metal_surface

    internal unsafe delegate VkResult vkCreateMetalSurfaceEXT_t(
        VkInstance instance,
        VkMetalSurfaceCreateInfoEXT* pCreateInfo,
        VkAllocationCallbacks* pAllocator,
        VkSurfaceKHR* pSurface);

    internal unsafe struct VkMetalSurfaceCreateInfoEXT
    {
        public const VkStructureType VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT = (VkStructureType)1000217000;

        public VkStructureType sType;
        public void* pNext;
        public uint flags;
        public void* pLayer;
    }

    internal unsafe struct VkPhysicalDeviceDriverProperties
    {
        public const int DriverNameLength = 256;
        public const int DriverInfoLength = 256;
        public const VkStructureType VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES = (VkStructureType)1000196000;

        public VkStructureType sType;
        public void* pNext;
        public VkDriverId driverID;
        public fixed byte driverName[DriverNameLength];
        public fixed byte driverInfo[DriverInfoLength];
        public VkConformanceVersion conformanceVersion;

        public static VkPhysicalDeviceDriverProperties New()
        {
            return new VkPhysicalDeviceDriverProperties { sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES };
        }
    }

    internal enum VkDriverId
    {
    }

    internal struct VkConformanceVersion
    {
        public byte major;
        public byte minor;
        public byte subminor;
        public byte patch;
    }
}
