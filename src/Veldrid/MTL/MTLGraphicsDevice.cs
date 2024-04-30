using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Veldrid.MetalBindings;
using NativeLibrary = NativeLibraryLoader.NativeLibrary;

namespace Veldrid.MTL
{
    internal unsafe class MTLGraphicsDevice : GraphicsDevice
    {
        public MTLDevice Device => _device;
        public MTLCommandQueue CommandQueue => _commandQueue;
        public MTLFeatureSupport MetalFeatures { get; }
        public ResourceBindingModel ResourceBindingModel { get; }
        public bool PreferMemorylessDepthTargets { get; }

        public override string DeviceName => _deviceName;

        public override string VendorName => "Apple";

        public override GraphicsApiVersion ApiVersion => _apiVersion;

        public override GraphicsBackend BackendType => GraphicsBackend.Metal;

        public override bool IsUvOriginTopLeft => true;

        public override bool IsDepthRangeZeroToOne => true;

        public override bool IsClipSpaceYInverted => false;

        public override ResourceFactory ResourceFactory { get; }

        public override Swapchain MainSwapchain => _mainSwapchain;

        public override GraphicsDeviceFeatures Features { get; }
        private static readonly Lazy<bool> s_isSupported = new Lazy<bool>(GetIsSupported);

        private static readonly Dictionary<IntPtr, MTLGraphicsDevice> s_aotRegisteredBlocks
            = new Dictionary<IntPtr, MTLGraphicsDevice>();

        private readonly MTLDevice _device;
        private readonly string _deviceName;
        private readonly GraphicsApiVersion _apiVersion;
        private readonly MTLCommandQueue _commandQueue;
        private readonly MTLSwapchain _mainSwapchain;
        private readonly bool[] _supportedSampleCounts;

        private readonly object _submittedCommandsLock = new object();
        private readonly Dictionary<MTLCommandBuffer, MTLCommandList> _submittedCLs = new Dictionary<MTLCommandBuffer, MTLCommandList>();

        private readonly object _resetEventsLock = new object();
        private readonly List<ManualResetEvent[]> _resetEvents = new List<ManualResetEvent[]>();

        private const string UnalignedBufferCopyPipelineMacOSName = "MTL_UnalignedBufferCopy_macOS";
        private const string UnalignedBufferCopyPipelineiOSName = "MTL_UnalignedBufferCopy_iOS";
        private readonly object _unalignedBufferCopyPipelineLock = new object();
        private readonly NativeLibrary _libSystem;
        private readonly IntPtr _concreteGlobalBlock;
        private readonly IntPtr _completionHandlerFuncPtr;
        private readonly IntPtr _completionBlockDescriptor;
        private readonly IntPtr _completionBlockLiteral;

        private readonly IMTLDisplayLink _displayLink;
        private readonly AutoResetEvent _nextFrameReadyEvent;
        private readonly EventWaitHandle _frameEndedEvent = new EventWaitHandle(true, EventResetMode.ManualReset);
        private readonly BackendInfoMetal _metalInfo;
        private MTLCommandBuffer _latestSubmittedCB;
        private MTLShader _unalignedBufferCopyShader;
        private MTLComputePipelineState _unalignedBufferCopyPipeline;
        private readonly MTLCommandBufferHandler _completionHandler;

        public MTLGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? swapchainDesc)
            : this(options, swapchainDesc, new MetalDeviceOptions())
        {
        }

        public MTLGraphicsDevice(
            GraphicsDeviceOptions options,
            SwapchainDescription? swapchainDesc,
            MetalDeviceOptions metalOptions)
        {
            _device = MTLDevice.MTLCreateSystemDefaultDevice();
            _deviceName = _device.name;
            MetalFeatures = new MTLFeatureSupport(_device);

            int major = (int)MetalFeatures.MaxFeatureSet / 10000;
            int minor = (int)MetalFeatures.MaxFeatureSet % 10000;
            _apiVersion = new GraphicsApiVersion(major, minor, 0, 0);

            Features = new GraphicsDeviceFeatures(
                true,
                false,
                false,
                MetalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v3),
                false,
                MetalFeatures.IsDrawBaseVertexInstanceSupported(),
                MetalFeatures.IsDrawBaseVertexInstanceSupported(),
                true,
                true,
                true,
                true,
                true,
                true, // TODO: Should be macOS 10.11+ and iOS 11.0+.
                true,
                true,
                true,
                true,
                true,
                false);
            ResourceBindingModel = options.ResourceBindingModel;
            PreferMemorylessDepthTargets = metalOptions.PreferMemorylessDepthTargets;

            if (MetalFeatures.IsMacOS)
            {
                _libSystem = new NativeLibrary("libSystem.dylib");
                _concreteGlobalBlock = _libSystem.LoadFunction("_NSConcreteGlobalBlock");
                _completionHandler = OnCommandBufferCompleted;
                _displayLink = new MTLCVDisplayLink();
            }
            else
            {
                _concreteGlobalBlock = IntPtr.Zero;
                _completionHandler = OnCommandBufferCompleted_Static;
            }

            if (_displayLink != null)
            {
                _nextFrameReadyEvent = new AutoResetEvent(true);
                _displayLink.Callback += OnDisplayLinkCallback;
            }

            _completionHandlerFuncPtr = Marshal.GetFunctionPointerForDelegate(_completionHandler);
            _completionBlockDescriptor = Marshal.AllocHGlobal(Unsafe.SizeOf<BlockDescriptor>());
            var descriptorPtr = (BlockDescriptor*)_completionBlockDescriptor;
            descriptorPtr->reserved = 0;
            descriptorPtr->Block_size = (ulong)Unsafe.SizeOf<BlockDescriptor>();

            _completionBlockLiteral = Marshal.AllocHGlobal(Unsafe.SizeOf<BlockLiteral>());
            var blockPtr = (BlockLiteral*)_completionBlockLiteral;
            blockPtr->isa = _concreteGlobalBlock;
            blockPtr->flags = (1 << 28) | (1 << 29);
            blockPtr->invoke = _completionHandlerFuncPtr;
            blockPtr->descriptor = descriptorPtr;

            if (!MetalFeatures.IsMacOS)
                lock (s_aotRegisteredBlocks)
                    s_aotRegisteredBlocks.Add(_completionBlockLiteral, this);

            ResourceFactory = new MTLResourceFactory(this);
            _commandQueue = _device.newCommandQueue();

            var allSampleCounts = (TextureSampleCount[])Enum.GetValues(typeof(TextureSampleCount));
            _supportedSampleCounts = new bool[allSampleCounts.Length];

            for (int i = 0; i < allSampleCounts.Length; i++)
            {
                var count = allSampleCounts[i];
                uint uintValue = FormatHelpers.GetSampleCountUInt32(count);
                if (_device.supportsTextureSampleCount(uintValue)) _supportedSampleCounts[i] = true;
            }

            if (swapchainDesc != null)
            {
                var desc = swapchainDesc.Value;
                _mainSwapchain = new MTLSwapchain(this, ref desc);
            }

            _metalInfo = new BackendInfoMetal(this);

            PostDeviceCreated();
        }

        public override void UpdateActiveDisplay(int x, int y, int w, int h)
        {
            if (_displayLink != null) _displayLink.UpdateActiveDisplay(x, y, w, h);
        }

        public override double GetActualRefreshPeriod()
        {
            if (_displayLink != null) return _displayLink.GetActualOutputVideoRefreshPeriod();

            return -1.0f;
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            for (int i = _supportedSampleCounts.Length - 1; i >= 0; i--)
                if (_supportedSampleCounts[i])
                    return (TextureSampleCount)i;

            return TextureSampleCount.Count1;
        }

        public override bool GetMetalInfo(out BackendInfoMetal info)
        {
            info = _metalInfo;
            return true;
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            return Util.AssertSubtype<Fence, MTLFence>(fence).Wait(nanosecondTimeout);
        }

        public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
        {
            int msTimeout;
            if (nanosecondTimeout == ulong.MaxValue)
                msTimeout = -1;
            else
                msTimeout = (int)Math.Min(nanosecondTimeout / 1_000_000, int.MaxValue);

            var events = GetResetEventArray(fences.Length);
            for (int i = 0; i < fences.Length; i++) events[i] = Util.AssertSubtype<Fence, MTLFence>(fences[i]).ResetEvent;
            bool result;

            if (waitAll)
                result = WaitHandle.WaitAll(events, msTimeout);
            else
            {
                int index = WaitHandle.WaitAny(events, msTimeout);
                result = index != WaitHandle.WaitTimeout;
            }

            ReturnResetEventArray(events);

            return result;
        }

        public override void ResetFence(Fence fence)
        {
            Util.AssertSubtype<Fence, MTLFence>(fence).Reset();
        }

        internal static bool IsSupported()
        {
            return s_isSupported.Value;
        }

        internal MTLComputePipelineState GetUnalignedBufferCopyPipeline()
        {
            lock (_unalignedBufferCopyPipelineLock)
            {
                if (_unalignedBufferCopyPipeline.IsNull)
                {
                    var descriptor = MTLUtil.AllocInit<MTLComputePipelineDescriptor>(
                        nameof(MTLComputePipelineDescriptor));
                    var buffer0 = descriptor.buffers[0];
                    buffer0.mutability = MTLMutability.Mutable;
                    var buffer1 = descriptor.buffers[1];
                    buffer0.mutability = MTLMutability.Mutable;

                    Debug.Assert(_unalignedBufferCopyShader == null);
                    string name = MetalFeatures.IsMacOS ? UnalignedBufferCopyPipelineMacOSName : UnalignedBufferCopyPipelineiOSName;

                    using (var resourceStream = typeof(MTLGraphicsDevice).Assembly.GetManifestResourceStream(name))
                    {
                        byte[] data = new byte[resourceStream.Length];

                        using (var ms = new MemoryStream(data))
                        {
                            resourceStream.CopyTo(ms);
                            var shaderDesc = new ShaderDescription(ShaderStages.Compute, data, "copy_bytes");
                            _unalignedBufferCopyShader = new MTLShader(ref shaderDesc, this);
                        }
                    }

                    descriptor.computeFunction = _unalignedBufferCopyShader.Function;
                    _unalignedBufferCopyPipeline = _device.newComputePipelineStateWithDescriptor(descriptor);
                    ObjectiveCRuntime.release(descriptor.NativePtr);
                }

                return _unalignedBufferCopyPipeline;
            }
        }

        internal override uint GetUniformBufferMinOffsetAlignmentCore()
        {
            return MetalFeatures.IsMacOS ? 16u : 256u;
        }

        internal override uint GetStructuredBufferMinOffsetAlignmentCore()
        {
            return 16u;
        }

        protected override MappedResource MapCore(MappableResource resource, MapMode mode, uint subresource)
        {
            if (resource is MTLBuffer buffer)
                return MapBuffer(buffer, mode);
            var texture = Util.AssertSubtype<MappableResource, MTLTexture>(resource);
            return MapTexture(texture, mode, subresource);
        }

        protected override void PlatformDispose()
        {
            WaitForIdle();

            if (!_unalignedBufferCopyPipeline.IsNull)
            {
                _unalignedBufferCopyShader.Dispose();
                ObjectiveCRuntime.release(_unalignedBufferCopyPipeline.NativePtr);
            }

            _mainSwapchain?.Dispose();
            ObjectiveCRuntime.release(_commandQueue.NativePtr);
            ObjectiveCRuntime.release(_device.NativePtr);

            lock (s_aotRegisteredBlocks) s_aotRegisteredBlocks.Remove(_completionBlockLiteral);

            _libSystem?.Dispose();
            Marshal.FreeHGlobal(_completionBlockDescriptor);
            Marshal.FreeHGlobal(_completionBlockLiteral);

            _displayLink?.Dispose();
        }

        protected override void UnmapCore(MappableResource resource, uint subresource)
        {
        }

        // Xamarin AOT requires native callbacks be static.
        [MonoPInvokeCallback(typeof(MTLCommandBufferHandler))]
        private static void OnCommandBufferCompleted_Static(IntPtr block, MTLCommandBuffer cb)
        {
            lock (s_aotRegisteredBlocks)
                if (s_aotRegisteredBlocks.TryGetValue(block, out var gd))
                    gd.OnCommandBufferCompleted(block, cb);
        }

        private static bool GetIsSupported()
        {
            bool result = false;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (RuntimeInformation.OSDescription.Contains("Darwin"))
                    {
                        var allDevices = MTLDevice.MTLCopyAllDevices();
                        result |= (ulong)allDevices.count > 0;
                        ObjectiveCRuntime.release(allDevices.NativePtr);
                    }
                    else
                    {
                        var defaultDevice = MTLDevice.MTLCreateSystemDefaultDevice();

                        if (defaultDevice.NativePtr != IntPtr.Zero)
                        {
                            result = true;
                            ObjectiveCRuntime.release(defaultDevice.NativePtr);
                        }
                    }
                }
            }
            catch
            {
                result = false;
            }

            return result;
        }

        private void OnCommandBufferCompleted(IntPtr block, MTLCommandBuffer cb)
        {
            lock (_submittedCommandsLock)
            {
                var cl = _submittedCLs[cb];
                _submittedCLs.Remove(cb);
                cl.OnCompleted(cb);

                if (_latestSubmittedCB.NativePtr == cb.NativePtr) _latestSubmittedCB = default;
            }

            ObjectiveCRuntime.release(cb.NativePtr);
        }

        private void OnDisplayLinkCallback()
        {
            _nextFrameReadyEvent.Set();
            _frameEndedEvent.WaitOne();
        }

        private MappedResource MapBuffer(MTLBuffer buffer, MapMode mode)
        {
            return new MappedResource(
                buffer,
                mode,
                (IntPtr)buffer.Pointer,
                buffer.SizeInBytes,
                0,
                buffer.SizeInBytes,
                buffer.SizeInBytes);
        }

        private MappedResource MapTexture(MTLTexture texture, MapMode mode, uint subresource)
        {
            Debug.Assert(!texture.StagingBuffer.IsNull);
            var data = texture.StagingBufferPointer;
            Util.GetMipLevelAndArrayLayer(texture, subresource, out uint mipLevel, out uint arrayLayer);
            Util.GetMipDimensions(texture, mipLevel, out uint width, out uint height, out uint depth);
            uint subresourceSize = texture.GetSubresourceSize(mipLevel, arrayLayer);
            texture.GetSubresourceLayout(mipLevel, arrayLayer, out uint rowPitch, out uint depthPitch);
            ulong offset = Util.ComputeSubresourceOffset(texture, mipLevel, arrayLayer);
            byte* offsetPtr = (byte*)data + offset;
            return new MappedResource(texture, mode, (IntPtr)offsetPtr, subresourceSize, subresource, rowPitch, depthPitch);
        }

        private ManualResetEvent[] GetResetEventArray(int length)
        {
            lock (_resetEventsLock)
            {
                for (int i = _resetEvents.Count - 1; i > 0; i--)
                {
                    var array = _resetEvents[i];

                    if (array.Length == length)
                    {
                        _resetEvents.RemoveAt(i);
                        return array;
                    }
                }
            }

            var newArray = new ManualResetEvent[length];
            return newArray;
        }

        private void ReturnResetEventArray(ManualResetEvent[] array)
        {
            lock (_resetEventsLock) _resetEvents.Add(array);
        }

        private protected override void SubmitCommandsCore(CommandList commandList, Fence fence)
        {
            var mtlCL = Util.AssertSubtype<CommandList, MTLCommandList>(commandList);

            mtlCL.CommandBuffer.addCompletedHandler(_completionBlockLiteral);

            lock (_submittedCommandsLock)
            {
                if (fence != null) mtlCL.SetCompletionFence(mtlCL.CommandBuffer, Util.AssertSubtype<Fence, MTLFence>(fence));

                _submittedCLs.Add(mtlCL.CommandBuffer, mtlCL);
                _latestSubmittedCB = mtlCL.Commit();
            }
        }

        private protected override void WaitForNextFrameReadyCore()
        {
            _frameEndedEvent.Reset();
            _nextFrameReadyEvent?.WaitOne(TimeSpan.FromSeconds(1)); // Should never time out.

            // in iOS, if one frame takes longer than the next V-Sync request, the next frame will be processed immediately rather than being delayed to a subsequent V-Sync request,
            // therefore we will request the next drawable here as a method of waiting until we're ready to draw the next frame.
            if (!MetalFeatures.IsMacOS)
            {
                var mtlSwapchainFramebuffer = Util.AssertSubtype<Framebuffer, MTLSwapchainFramebuffer>(_mainSwapchain.Framebuffer);
                mtlSwapchainFramebuffer.EnsureDrawableAvailable();
            }
        }

        private protected override bool GetPixelFormatSupportCore(
            PixelFormat format,
            TextureType type,
            TextureUsage usage,
            out PixelFormatProperties properties)
        {
            if (!MTLFormats.IsFormatSupported(format, usage, MetalFeatures))
            {
                properties = default;
                return false;
            }

            uint sampleCounts = 0;

            for (int i = 0; i < _supportedSampleCounts.Length; i++)
                if (_supportedSampleCounts[i])
                    sampleCounts |= (uint)(1 << i);

            var maxFeatureSet = MetalFeatures.MaxFeatureSet;
            uint maxArrayLayer = MTLFormats.GetMaxTextureVolume(maxFeatureSet);
            uint maxWidth;
            uint maxHeight;
            uint maxDepth;

            if (type == TextureType.Texture1D)
            {
                maxWidth = MTLFormats.GetMaxTexture1DWidth(maxFeatureSet);
                maxHeight = 1;
                maxDepth = 1;
            }
            else if (type == TextureType.Texture2D)
            {
                uint maxDimensions;
                if ((usage & TextureUsage.Cubemap) != 0)
                    maxDimensions = MTLFormats.GetMaxTextureCubeDimensions(maxFeatureSet);
                else
                    maxDimensions = MTLFormats.GetMaxTexture2DDimensions(maxFeatureSet);

                maxWidth = maxDimensions;
                maxHeight = maxDimensions;
                maxDepth = 1;
            }
            else if (type == TextureType.Texture3D)
            {
                maxWidth = maxArrayLayer;
                maxHeight = maxArrayLayer;
                maxDepth = maxArrayLayer;
                maxArrayLayer = 1;
            }
            else
                throw Illegal.Value<TextureType>();

            properties = new PixelFormatProperties(
                maxWidth,
                maxHeight,
                maxDepth,
                uint.MaxValue,
                maxArrayLayer,
                sampleCounts);
            return true;
        }

        private protected override void SwapBuffersCore(Swapchain swapchain)
        {
            var mtlSC = Util.AssertSubtype<Swapchain, MTLSwapchain>(swapchain);
            IntPtr currentDrawablePtr = mtlSC.CurrentDrawable.NativePtr;

            if (currentDrawablePtr != IntPtr.Zero)
            {
                using (NSAutoreleasePool.Begin())
                {
                    var submitCB = _commandQueue.commandBuffer();
                    submitCB.presentDrawable(currentDrawablePtr);
                    submitCB.commit();
                }

                mtlSC.InvalidateDrawable();
            }

            _frameEndedEvent.Set();
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            var mtlBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(buffer);
            var destPtr = mtlBuffer.Pointer;
            byte* destOffsetPtr = (byte*)destPtr + bufferOffsetInBytes;

            if (destPtr == null)
                throw new VeldridException("Attempting to write to a MTLBuffer that is inaccessible from a CPU.");

            Unsafe.CopyBlock(destOffsetPtr, source.ToPointer(), sizeInBytes);
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
            var mtlTex = Util.AssertSubtype<Texture, MTLTexture>(texture);

            if (mtlTex.StagingBuffer.IsNull)
            {
                var stagingTex = ResourceFactory.CreateTexture(new TextureDescription(
                    width, height, depth, 1, 1, texture.Format, TextureUsage.Staging, texture.Type));
                UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
                var cl = ResourceFactory.CreateCommandList();
                cl.Begin();
                cl.CopyTexture(
                    stagingTex, 0, 0, 0, 0, 0,
                    texture, x, y, z, mipLevel, arrayLayer,
                    width, height, depth, 1);
                cl.End();
                SubmitCommands(cl);

                cl.Dispose();
                stagingTex.Dispose();
            }
            else
            {
                mtlTex.GetSubresourceLayout(mipLevel, arrayLayer, out uint dstRowPitch, out uint dstDepthPitch);
                ulong dstOffset = Util.ComputeSubresourceOffset(mtlTex, mipLevel, arrayLayer);
                uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
                uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
                Util.CopyTextureRegion(
                    source.ToPointer(),
                    0, 0, 0,
                    srcRowPitch, srcDepthPitch,
                    (byte*)mtlTex.StagingBufferPointer + dstOffset,
                    x, y, z,
                    dstRowPitch, dstDepthPitch,
                    width, height, depth,
                    texture.Format);
            }
        }

        private protected override void WaitForIdleCore()
        {
            var lastCB = default(MTLCommandBuffer);

            lock (_submittedCommandsLock)
            {
                lastCB = _latestSubmittedCB;
                ObjectiveCRuntime.retain(lastCB.NativePtr);
            }

            if (lastCB.NativePtr != IntPtr.Zero && lastCB.status != MTLCommandBufferStatus.Completed) lastCB.waitUntilCompleted();

            ObjectiveCRuntime.release(lastCB.NativePtr);
        }
    }

    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type t) { }
    }
}
