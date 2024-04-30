using System;
using System.Collections.Generic;
using System.Diagnostics;
using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal unsafe class MTLCommandList : CommandList
    {
        public MTLCommandBuffer CommandBuffer => _cb;

        public override bool IsDisposed => _disposed;

        public override string Name { get; set; }
        private readonly MTLGraphicsDevice _gd;

        private readonly List<MTLBuffer> _availableStagingBuffers = new List<MTLBuffer>();
        private readonly Dictionary<MTLCommandBuffer, List<MTLBuffer>> _submittedStagingBuffers = new Dictionary<MTLCommandBuffer, List<MTLBuffer>>();
        private readonly object _submittedCommandsLock = new object();
        private readonly Dictionary<MTLCommandBuffer, MTLFence> _completionFences = new Dictionary<MTLCommandBuffer, MTLFence>();

        private readonly Dictionary<UIntPtr, DeviceBufferRange> _boundVertexBuffers = new Dictionary<UIntPtr, DeviceBufferRange>();
        private readonly Dictionary<UIntPtr, DeviceBufferRange> _boundFragmentBuffers = new Dictionary<UIntPtr, DeviceBufferRange>();
        private readonly Dictionary<UIntPtr, DeviceBufferRange> _boundComputeBuffers = new Dictionary<UIntPtr, DeviceBufferRange>();

        private readonly Dictionary<UIntPtr, MetalBindings.MTLTexture> _boundVertexTextures = new Dictionary<UIntPtr, MetalBindings.MTLTexture>();
        private readonly Dictionary<UIntPtr, MetalBindings.MTLTexture> _boundFragmentTextures = new Dictionary<UIntPtr, MetalBindings.MTLTexture>();
        private readonly Dictionary<UIntPtr, MetalBindings.MTLTexture> _boundComputeTextures = new Dictionary<UIntPtr, MetalBindings.MTLTexture>();

        private readonly Dictionary<UIntPtr, MTLSamplerState> _boundVertexSamplers = new Dictionary<UIntPtr, MTLSamplerState>();
        private readonly Dictionary<UIntPtr, MTLSamplerState> _boundFragmentSamplers = new Dictionary<UIntPtr, MTLSamplerState>();
        private readonly Dictionary<UIntPtr, MTLSamplerState> _boundComputeSamplers = new Dictionary<UIntPtr, MTLSamplerState>();

        private bool RenderEncoderActive => !_rce.IsNull;
        private bool BlitEncoderActive => !_bce.IsNull;
        private bool ComputeEncoderActive => !_cce.IsNull;
        private MTLCommandBuffer _cb;
        private MTLFramebuffer _mtlFramebuffer;
        private uint _viewportCount;
        private bool _currentFramebufferEverActive;
        private MTLRenderCommandEncoder _rce;
        private MTLBlitCommandEncoder _bce;
        private MTLComputeCommandEncoder _cce;
        private RgbaFloat?[] _clearColors = Array.Empty<RgbaFloat?>();
        private (float depth, byte stencil)? _clearDepth;
        private MTLBuffer _indexBuffer;
        private uint _ibOffset;
        private MTLIndexType _indexType;
        private MTLPipeline _lastGraphicsPipeline;
        private new MTLPipeline _graphicsPipeline;
        private MTLPipeline _lastComputePipeline;
        private new MTLPipeline _computePipeline;
        private MTLViewport[] _viewports = Array.Empty<MTLViewport>();
        private bool _viewportsChanged;
        private MTLScissorRect[] _activeScissorRects = Array.Empty<MTLScissorRect>();
        private MTLScissorRect[] _scissorRects = Array.Empty<MTLScissorRect>();
        private uint _graphicsResourceSetCount;
        private BoundResourceSetInfo[] _graphicsResourceSets;
        private bool[] _graphicsResourceSetsActive;
        private uint _computeResourceSetCount;
        private BoundResourceSetInfo[] _computeResourceSets;
        private bool[] _computeResourceSetsActive;
        private uint _vertexBufferCount;
        private uint _nonVertexBufferCount;
        private MTLBuffer[] _vertexBuffers;
        private bool[] _vertexBuffersActive;
        private uint[] _vbOffsets;
        private bool[] _vbOffsetsActive;
        private bool _disposed;

        public MTLCommandList(ref CommandListDescription description, MTLGraphicsDevice gd)
            : base(ref description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
        {
            _gd = gd;
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                EnsureNoRenderPass();

                lock (_submittedStagingBuffers)
                {
                    foreach (var buffer in _availableStagingBuffers) buffer.Dispose();

                    foreach (var kvp in _submittedStagingBuffers)
                    {
                        foreach (var buffer in kvp.Value) buffer.Dispose();
                    }

                    _submittedStagingBuffers.Clear();
                }

                if (_cb.NativePtr != IntPtr.Zero) ObjectiveCRuntime.release(_cb.NativePtr);
            }
        }

        #endregion

        public MTLCommandBuffer Commit()
        {
            _cb.commit();
            var ret = _cb;
            _cb = default;
            return ret;
        }

        public override void Begin()
        {
            if (_cb.NativePtr != IntPtr.Zero) ObjectiveCRuntime.release(_cb.NativePtr);

            using (NSAutoreleasePool.Begin())
            {
                _cb = _gd.CommandQueue.commandBuffer();
                ObjectiveCRuntime.retain(_cb.NativePtr);
            }

            ClearCachedState();
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            PreComputeCommand();
            _cce.dispatchThreadGroups(
                new MTLSize(groupCountX, groupCountY, groupCountZ),
                _computePipeline.ThreadsPerThreadgroup);
        }

        public override void End()
        {
            EnsureNoBlitEncoder();
            EnsureNoComputeEncoder();

            if (!_currentFramebufferEverActive && _mtlFramebuffer != null) BeginCurrentRenderPass();
            EnsureNoRenderPass();
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            _scissorRects[index] = new MTLScissorRect(x, y, width, height);
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            _viewportsChanged = true;
            _viewports[index] = new MTLViewport(
                viewport.X,
                viewport.Y,
                viewport.Width,
                viewport.Height,
                viewport.MinDepth,
                viewport.MaxDepth);
        }

        public void SetCompletionFence(MTLCommandBuffer cb, MTLFence fence)
        {
            lock (_submittedCommandsLock)
            {
                Debug.Assert(!_completionFences.ContainsKey(cb));
                _completionFences[cb] = fence;
            }
        }

        public void OnCompleted(MTLCommandBuffer cb)
        {
            lock (_submittedCommandsLock)
            {
                if (_completionFences.TryGetValue(cb, out var completionFence))
                {
                    completionFence.Set();
                    _completionFences.Remove(cb);
                }

                if (_submittedStagingBuffers.TryGetValue(cb, out var bufferList))
                {
                    _availableStagingBuffers.AddRange(bufferList);
                    _submittedStagingBuffers.Remove(cb);
                }
            }
        }

        protected override void CopyBufferCore(
            DeviceBuffer source,
            uint sourceOffset,
            DeviceBuffer destination,
            uint destinationOffset,
            uint sizeInBytes)
        {
            var mtlSrc = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(source);
            var mtlDst = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(destination);

            if (sourceOffset % 4 != 0 || destinationOffset % 4 != 0 || sizeInBytes % 4 != 0)
            {
                // Unaligned copy -- use special compute shader.
                EnsureComputeEncoder();
                _cce.setComputePipelineState(_gd.GetUnalignedBufferCopyPipeline());
                _cce.setBuffer(mtlSrc.DeviceBuffer, UIntPtr.Zero, 0);
                _cce.setBuffer(mtlDst.DeviceBuffer, UIntPtr.Zero, 1);

                MTLUnalignedBufferCopyInfo copyInfo;
                copyInfo.SourceOffset = sourceOffset;
                copyInfo.DestinationOffset = destinationOffset;
                copyInfo.CopySize = sizeInBytes;

                _cce.setBytes(&copyInfo, (UIntPtr)sizeof(MTLUnalignedBufferCopyInfo), 2);
                _cce.dispatchThreadGroups(new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            }
            else
            {
                EnsureBlitEncoder();
                _bce.copy(
                    mtlSrc.DeviceBuffer, sourceOffset,
                    mtlDst.DeviceBuffer, destinationOffset,
                    sizeInBytes);
            }
        }

        protected override void CopyTextureCore(
            Texture source, uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
            Texture destination, uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
            uint width, uint height, uint depth, uint layerCount)
        {
            EnsureBlitEncoder();
            var srcMTLTexture = Util.AssertSubtype<Texture, MTLTexture>(source);
            var dstMTLTexture = Util.AssertSubtype<Texture, MTLTexture>(destination);

            bool srcIsStaging = (source.Usage & TextureUsage.Staging) != 0;
            bool dstIsStaging = (destination.Usage & TextureUsage.Staging) != 0;

            if (srcIsStaging && !dstIsStaging)
            {
                // Staging -> Normal
                var srcBuffer = srcMTLTexture.StagingBuffer;
                var dstTexture = dstMTLTexture.DeviceTexture;

                Util.GetMipDimensions(srcMTLTexture, srcMipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);

                for (uint layer = 0; layer < layerCount; layer++)
                {
                    uint blockSize = FormatHelpers.IsCompressedFormat(srcMTLTexture.Format) ? 4u : 1u;
                    uint compressedSrcX = srcX / blockSize;
                    uint compressedSrcY = srcY / blockSize;
                    uint blockSizeInBytes = blockSize == 1
                        ? FormatSizeHelpers.GetSizeInBytes(srcMTLTexture.Format)
                        : FormatHelpers.GetBlockSizeInBytes(srcMTLTexture.Format);

                    ulong srcSubresourceBase = Util.ComputeSubresourceOffset(
                        srcMTLTexture,
                        srcMipLevel,
                        layer + srcBaseArrayLayer);
                    srcMTLTexture.GetSubresourceLayout(
                        srcMipLevel,
                        srcBaseArrayLayer + layer,
                        out uint srcRowPitch,
                        out uint srcDepthPitch);
                    ulong sourceOffset = srcSubresourceBase
                                         + srcDepthPitch * srcZ
                                         + srcRowPitch * compressedSrcY
                                         + blockSizeInBytes * compressedSrcX;

                    uint copyWidth = width > mipWidth && width <= blockSize
                        ? mipWidth
                        : width;

                    uint copyHeight = height > mipHeight && height <= blockSize
                        ? mipHeight
                        : height;

                    var sourceSize = new MTLSize(copyWidth, copyHeight, depth);
                    if (dstMTLTexture.Type != TextureType.Texture3D) srcDepthPitch = 0;
                    _bce.copyFromBuffer(
                        srcBuffer,
                        (UIntPtr)sourceOffset,
                        srcRowPitch,
                        srcDepthPitch,
                        sourceSize,
                        dstTexture,
                        dstBaseArrayLayer + layer,
                        dstMipLevel,
                        new MTLOrigin(dstX, dstY, dstZ),
                        _gd.MetalFeatures.IsMacOS);
                }
            }
            else if (srcIsStaging && dstIsStaging)
            {
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    // Staging -> Staging
                    ulong srcSubresourceBase = Util.ComputeSubresourceOffset(
                        srcMTLTexture,
                        srcMipLevel,
                        layer + srcBaseArrayLayer);
                    srcMTLTexture.GetSubresourceLayout(
                        srcMipLevel,
                        srcBaseArrayLayer + layer,
                        out uint srcRowPitch,
                        out uint srcDepthPitch);

                    ulong dstSubresourceBase = Util.ComputeSubresourceOffset(
                        dstMTLTexture,
                        dstMipLevel,
                        layer + dstBaseArrayLayer);
                    dstMTLTexture.GetSubresourceLayout(
                        dstMipLevel,
                        dstBaseArrayLayer + layer,
                        out uint dstRowPitch,
                        out uint dstDepthPitch);

                    uint blockSize = FormatHelpers.IsCompressedFormat(dstMTLTexture.Format) ? 4u : 1u;

                    if (blockSize == 1)
                    {
                        uint pixelSize = FormatSizeHelpers.GetSizeInBytes(dstMTLTexture.Format);
                        uint copySize = width * pixelSize;

                        for (uint zz = 0; zz < depth; zz++)
                        {
                            for (uint yy = 0; yy < height; yy++)
                            {
                                ulong srcRowOffset = srcSubresourceBase
                                                     + srcDepthPitch * (zz + srcZ)
                                                     + srcRowPitch * (yy + srcY)
                                                     + pixelSize * srcX;
                                ulong dstRowOffset = dstSubresourceBase
                                                     + dstDepthPitch * (zz + dstZ)
                                                     + dstRowPitch * (yy + dstY)
                                                     + pixelSize * dstX;
                                _bce.copy(
                                    srcMTLTexture.StagingBuffer,
                                    (UIntPtr)srcRowOffset,
                                    dstMTLTexture.StagingBuffer,
                                    (UIntPtr)dstRowOffset,
                                    copySize);
                            }
                        }
                    }
                    else // blockSize != 1
                    {
                        uint paddedWidth = Math.Max(blockSize, width);
                        uint paddedHeight = Math.Max(blockSize, height);
                        uint numRows = FormatHelpers.GetNumRows(paddedHeight, srcMTLTexture.Format);
                        uint rowPitch = FormatHelpers.GetRowPitch(paddedWidth, srcMTLTexture.Format);

                        uint compressedSrcX = srcX / 4;
                        uint compressedSrcY = srcY / 4;
                        uint compressedDstX = dstX / 4;
                        uint compressedDstY = dstY / 4;
                        uint blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(srcMTLTexture.Format);

                        for (uint zz = 0; zz < depth; zz++)
                        {
                            for (uint row = 0; row < numRows; row++)
                            {
                                ulong srcRowOffset = srcSubresourceBase
                                                     + srcDepthPitch * (zz + srcZ)
                                                     + srcRowPitch * (row + compressedSrcY)
                                                     + blockSizeInBytes * compressedSrcX;
                                ulong dstRowOffset = dstSubresourceBase
                                                     + dstDepthPitch * (zz + dstZ)
                                                     + dstRowPitch * (row + compressedDstY)
                                                     + blockSizeInBytes * compressedDstX;
                                _bce.copy(
                                    srcMTLTexture.StagingBuffer,
                                    (UIntPtr)srcRowOffset,
                                    dstMTLTexture.StagingBuffer,
                                    (UIntPtr)dstRowOffset,
                                    rowPitch);
                            }
                        }
                    }
                }
            }
            else if (!srcIsStaging && dstIsStaging)
            {
                // Normal -> Staging
                var srcOrigin = new MTLOrigin(srcX, srcY, srcZ);
                var srcSize = new MTLSize(width, height, depth);

                for (uint layer = 0; layer < layerCount; layer++)
                {
                    dstMTLTexture.GetSubresourceLayout(
                        dstMipLevel,
                        dstBaseArrayLayer + layer,
                        out uint dstBytesPerRow,
                        out uint dstBytesPerImage);

                    Util.GetMipDimensions(srcMTLTexture, dstMipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
                    uint blockSize = FormatHelpers.IsCompressedFormat(srcMTLTexture.Format) ? 4u : 1u;
                    uint bufferRowLength = Math.Max(mipWidth, blockSize);
                    uint bufferImageHeight = Math.Max(mipHeight, blockSize);
                    uint compressedDstX = dstX / blockSize;
                    uint compressedDstY = dstY / blockSize;
                    uint blockSizeInBytes = blockSize == 1
                        ? FormatSizeHelpers.GetSizeInBytes(srcMTLTexture.Format)
                        : FormatHelpers.GetBlockSizeInBytes(srcMTLTexture.Format);
                    uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcMTLTexture.Format);
                    uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcMTLTexture.Format);

                    ulong dstOffset = Util.ComputeSubresourceOffset(dstMTLTexture, dstMipLevel, dstBaseArrayLayer + layer)
                                      + dstZ * depthPitch
                                      + compressedDstY * rowPitch
                                      + compressedDstX * blockSizeInBytes;

                    _bce.copyTextureToBuffer(
                        srcMTLTexture.DeviceTexture,
                        srcBaseArrayLayer + layer,
                        srcMipLevel,
                        srcOrigin,
                        srcSize,
                        dstMTLTexture.StagingBuffer,
                        (UIntPtr)dstOffset,
                        dstBytesPerRow,
                        dstBytesPerImage);
                }
            }
            else
            {
                // Normal -> Normal
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    _bce.copyFromTexture(
                        srcMTLTexture.DeviceTexture,
                        srcBaseArrayLayer + layer,
                        srcMipLevel,
                        new MTLOrigin(srcX, srcY, srcZ),
                        new MTLSize(width, height, depth),
                        dstMTLTexture.DeviceTexture,
                        dstBaseArrayLayer + layer,
                        dstMipLevel,
                        new MTLOrigin(dstX, dstY, dstZ),
                        _gd.MetalFeatures.IsMacOS);
                }
            }
        }

        protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
        {
            var mtlBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(indirectBuffer);
            PreComputeCommand();
            _cce.dispatchThreadgroupsWithIndirectBuffer(
                mtlBuffer.DeviceBuffer,
                offset,
                _computePipeline.ThreadsPerThreadgroup);
        }

        protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            if (PreDrawCommand())
            {
                var mtlBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(indirectBuffer);

                for (uint i = 0; i < drawCount; i++)
                {
                    uint currentOffset = i * stride + offset;
                    _rce.drawIndexedPrimitives(
                        _graphicsPipeline.PrimitiveType,
                        _indexType,
                        _indexBuffer.DeviceBuffer,
                        _ibOffset,
                        mtlBuffer.DeviceBuffer,
                        currentOffset);
                }
            }
        }

        protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            if (PreDrawCommand())
            {
                var mtlBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(indirectBuffer);

                for (uint i = 0; i < drawCount; i++)
                {
                    uint currentOffset = i * stride + offset;
                    _rce.drawPrimitives(_graphicsPipeline.PrimitiveType, mtlBuffer.DeviceBuffer, currentOffset);
                }
            }
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            // TODO: This approach destroys the contents of the source Texture (according to the docs).
            EnsureNoBlitEncoder();
            EnsureNoRenderPass();

            var mtlSrc = Util.AssertSubtype<Texture, MTLTexture>(source);
            var mtlDst = Util.AssertSubtype<Texture, MTLTexture>(destination);

            var rpDesc = MTLRenderPassDescriptor.New();
            var colorAttachment = rpDesc.colorAttachments[0];
            colorAttachment.texture = mtlSrc.DeviceTexture;
            colorAttachment.loadAction = MTLLoadAction.Load;
            colorAttachment.storeAction = MTLStoreAction.MultisampleResolve;
            colorAttachment.resolveTexture = mtlDst.DeviceTexture;

            using (NSAutoreleasePool.Begin())
            {
                var encoder = _cb.renderCommandEncoderWithDescriptor(rpDesc);
                encoder.endEncoding();
            }

            ObjectiveCRuntime.release(rpDesc.NativePtr);
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet set, uint dynamicOffsetCount, ref uint dynamicOffsets)
        {
            if (!_computeResourceSets[slot].Equals(set, dynamicOffsetCount, ref dynamicOffsets))
            {
                _computeResourceSets[slot].Offsets.Dispose();
                _computeResourceSets[slot] = new BoundResourceSetInfo(set, dynamicOffsetCount, ref dynamicOffsets);
                _computeResourceSetsActive[slot] = false;
            }
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            if (!_currentFramebufferEverActive && _mtlFramebuffer != null)
            {
                // This ensures that any submitted clear values will be used even if nothing has been drawn.
                if (EnsureRenderPass()) EndCurrentRenderPass();
            }

            EnsureNoRenderPass();
            _mtlFramebuffer = Util.AssertSubtype<Framebuffer, MTLFramebuffer>(fb);
            _viewportCount = Math.Max(1u, (uint)fb.ColorTargets.Count);
            Util.EnsureArrayMinimumSize(ref _viewports, _viewportCount);
            Util.ClearArray(_viewports);
            Util.EnsureArrayMinimumSize(ref _scissorRects, _viewportCount);
            Util.ClearArray(_scissorRects);
            Util.EnsureArrayMinimumSize(ref _activeScissorRects, _viewportCount);
            Util.ClearArray(_activeScissorRects);
            Util.EnsureArrayMinimumSize(ref _clearColors, (uint)fb.ColorTargets.Count);
            Util.ClearArray(_clearColors);
            _currentFramebufferEverActive = false;
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetCount, ref uint dynamicOffsets)
        {
            if (!_graphicsResourceSets[slot].Equals(rs, dynamicOffsetCount, ref dynamicOffsets))
            {
                _graphicsResourceSets[slot].Offsets.Dispose();
                _graphicsResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetCount, ref dynamicOffsets);
                _graphicsResourceSetsActive[slot] = false;
            }
        }

        private bool PreDrawCommand()
        {
            if (EnsureRenderPass())
            {
                if (_viewportsChanged)
                {
                    FlushViewports();
                    _viewportsChanged = false;
                }

                if (_graphicsPipeline.ScissorTestEnabled)
                    FlushScissorRects();

                Debug.Assert(_graphicsPipeline != null);

                if (_graphicsPipeline.RenderPipelineState.NativePtr != _lastGraphicsPipeline?.RenderPipelineState.NativePtr)
                    _rce.setRenderPipelineState(_graphicsPipeline.RenderPipelineState);

                if (_graphicsPipeline.CullMode != _lastGraphicsPipeline?.CullMode)
                    _rce.setCullMode(_graphicsPipeline.CullMode);

                if (_graphicsPipeline.FrontFace != _lastGraphicsPipeline?.FrontFace)
                    _rce.setFrontFacing(_graphicsPipeline.FrontFace);

                if (_graphicsPipeline.FillMode != _lastGraphicsPipeline?.FillMode)
                    _rce.setTriangleFillMode(_graphicsPipeline.FillMode);

                var blendColor = _graphicsPipeline.BlendColor;
                if (blendColor != _lastGraphicsPipeline?.BlendColor)
                    _rce.setBlendColor(blendColor.R, blendColor.G, blendColor.B, blendColor.A);

                if (_framebuffer.DepthTarget != null)
                {
                    if (_graphicsPipeline.DepthStencilState.NativePtr != _lastGraphicsPipeline?.DepthStencilState.NativePtr)
                        _rce.setDepthStencilState(_graphicsPipeline.DepthStencilState);

                    if (_graphicsPipeline.DepthClipMode != _lastGraphicsPipeline?.DepthClipMode)
                        _rce.setDepthClipMode(_graphicsPipeline.DepthClipMode);

                    if (_graphicsPipeline.StencilReference != _lastGraphicsPipeline?.StencilReference)
                        _rce.setStencilReferenceValue(_graphicsPipeline.StencilReference);
                }

                _lastGraphicsPipeline = _graphicsPipeline;

                for (uint i = 0; i < _graphicsResourceSetCount; i++)
                {
                    if (!_graphicsResourceSetsActive[i])
                    {
                        ActivateGraphicsResourceSet(i, _graphicsResourceSets[i]);
                        _graphicsResourceSetsActive[i] = true;
                    }
                }

                for (uint i = 0; i < _vertexBufferCount; i++)
                {
                    if (!_vertexBuffersActive[i])
                    {
                        UIntPtr index = _graphicsPipeline.ResourceBindingModel == ResourceBindingModel.Improved
                            ? _nonVertexBufferCount + i
                            : i;
                        _rce.setVertexBuffer(
                            _vertexBuffers[i].DeviceBuffer,
                            _vbOffsets[i],
                            index);

                        _vertexBuffersActive[i] = true;
                        _vbOffsetsActive[i] = true;
                    }

                    if (!_vbOffsetsActive[i])
                    {
                        UIntPtr index = _graphicsPipeline.ResourceBindingModel == ResourceBindingModel.Improved
                            ? _nonVertexBufferCount + i
                            : i;

                        _rce.setVertexBufferOffset(
                            _vbOffsets[i],
                            index);

                        _vbOffsetsActive[i] = true;
                    }
                }

                return true;
            }

            return false;
        }

        private void FlushViewports()
        {
            if (_gd.MetalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v3))
                fixed (MTLViewport* viewportsPtr = &_viewports[0])
                    _rce.setViewports(viewportsPtr, _viewportCount);
            else
                _rce.setViewport(_viewports[0]);
        }

        private void FlushScissorRects()
        {
            if (_gd.MetalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v3))
            {
                bool scissorRectsChanged = false;

                for (int i = 0; i < _scissorRects.Length; i++)
                {
                    scissorRectsChanged |= !_scissorRects[i].Equals(_activeScissorRects[i]);
                    _activeScissorRects[i] = _scissorRects[i];
                }

                if (scissorRectsChanged)
                {
                    fixed (MTLScissorRect* scissorRectsPtr = _scissorRects)
                        _rce.setScissorRects(scissorRectsPtr, _viewportCount);
                }
            }
            else
            {
                if (!_scissorRects[0].Equals(_activeScissorRects[0]))
                    _rce.setScissorRect(_scissorRects[0]);

                _activeScissorRects[0] = _scissorRects[0];
            }
        }

        private void PreComputeCommand()
        {
            EnsureComputeEncoder();

            if (_computePipeline.ComputePipelineState.NativePtr != _lastComputePipeline?.ComputePipelineState.NativePtr)
                _cce.setComputePipelineState(_computePipeline.ComputePipelineState);

            _lastComputePipeline = _computePipeline;

            for (uint i = 0; i < _computeResourceSetCount; i++)
            {
                if (!_computeResourceSetsActive[i])
                {
                    ActivateComputeResourceSet(i, _computeResourceSets[i]);
                    _computeResourceSetsActive[i] = true;
                }
            }
        }

        private MTLBuffer GetFreeStagingBuffer(uint sizeInBytes)
        {
            lock (_submittedCommandsLock)
            {
                foreach (var buffer in _availableStagingBuffers)
                {
                    if (buffer.SizeInBytes >= sizeInBytes)
                    {
                        _availableStagingBuffers.Remove(buffer);
                        return buffer;
                    }
                }
            }

            var staging = _gd.ResourceFactory.CreateBuffer(
                new BufferDescription(sizeInBytes, BufferUsage.Staging));

            return Util.AssertSubtype<DeviceBuffer, MTLBuffer>(staging);
        }

        private void ActivateGraphicsResourceSet(uint slot, BoundResourceSetInfo brsi)
        {
            Debug.Assert(RenderEncoderActive);
            var mtlRS = Util.AssertSubtype<ResourceSet, MTLResourceSet>(brsi.Set);
            var layout = mtlRS.Layout;
            uint dynamicOffsetIndex = 0;

            for (int i = 0; i < mtlRS.Resources.Length; i++)
            {
                var bindingInfo = layout.GetBindingInfo(i);
                var resource = mtlRS.Resources[i];
                uint bufferOffset = 0;

                if (bindingInfo.DynamicBuffer)
                {
                    bufferOffset = brsi.Offsets.Get(dynamicOffsetIndex);
                    dynamicOffsetIndex += 1;
                }

                switch (bindingInfo.Kind)
                {
                    case ResourceKind.UniformBuffer:
                    {
                        var range = Util.GetBufferRange(resource, bufferOffset);
                        BindBuffer(range, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;
                    }

                    case ResourceKind.TextureReadOnly:
                        var texView = Util.GetTextureView(_gd, resource);
                        var mtlTexView = Util.AssertSubtype<TextureView, MTLTextureView>(texView);
                        BindTexture(mtlTexView, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;

                    case ResourceKind.TextureReadWrite:
                        var texViewRW = Util.GetTextureView(_gd, resource);
                        var mtlTexViewRW = Util.AssertSubtype<TextureView, MTLTextureView>(texViewRW);
                        BindTexture(mtlTexViewRW, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;

                    case ResourceKind.Sampler:
                        var mtlSampler = Util.AssertSubtype<BindableResource, MTLSampler>(resource);
                        BindSampler(mtlSampler, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;

                    case ResourceKind.StructuredBufferReadOnly:
                    {
                        var range = Util.GetBufferRange(resource, bufferOffset);
                        BindBuffer(range, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;
                    }

                    case ResourceKind.StructuredBufferReadWrite:
                    {
                        var range = Util.GetBufferRange(resource, bufferOffset);
                        BindBuffer(range, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;
                    }

                    default:
                        throw Illegal.Value<ResourceKind>();
                }
            }
        }

        private void ActivateComputeResourceSet(uint slot, BoundResourceSetInfo brsi)
        {
            Debug.Assert(ComputeEncoderActive);
            var mtlRS = Util.AssertSubtype<ResourceSet, MTLResourceSet>(brsi.Set);
            var layout = mtlRS.Layout;
            uint dynamicOffsetIndex = 0;

            for (int i = 0; i < mtlRS.Resources.Length; i++)
            {
                var bindingInfo = layout.GetBindingInfo(i);
                var resource = mtlRS.Resources[i];
                uint bufferOffset = 0;

                if (bindingInfo.DynamicBuffer)
                {
                    bufferOffset = brsi.Offsets.Get(dynamicOffsetIndex);
                    dynamicOffsetIndex += 1;
                }

                switch (bindingInfo.Kind)
                {
                    case ResourceKind.UniformBuffer:
                    {
                        var range = Util.GetBufferRange(resource, bufferOffset);
                        BindBuffer(range, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;
                    }

                    case ResourceKind.TextureReadOnly:
                        var texView = Util.GetTextureView(_gd, resource);
                        var mtlTexView = Util.AssertSubtype<TextureView, MTLTextureView>(texView);
                        BindTexture(mtlTexView, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;

                    case ResourceKind.TextureReadWrite:
                        var texViewRW = Util.GetTextureView(_gd, resource);
                        var mtlTexViewRW = Util.AssertSubtype<TextureView, MTLTextureView>(texViewRW);
                        BindTexture(mtlTexViewRW, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;

                    case ResourceKind.Sampler:
                        var mtlSampler = Util.AssertSubtype<BindableResource, MTLSampler>(resource);
                        BindSampler(mtlSampler, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;

                    case ResourceKind.StructuredBufferReadOnly:
                    {
                        var range = Util.GetBufferRange(resource, bufferOffset);
                        BindBuffer(range, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;
                    }

                    case ResourceKind.StructuredBufferReadWrite:
                    {
                        var range = Util.GetBufferRange(resource, bufferOffset);
                        BindBuffer(range, slot, bindingInfo.Slot, bindingInfo.Stages);
                        break;
                    }

                    default:
                        throw Illegal.Value<ResourceKind>();
                }
            }
        }

        private void BindBuffer(DeviceBufferRange range, uint set, uint slot, ShaderStages stages)
        {
            var mtlBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(range.Buffer);
            uint baseBuffer = GetBufferBase(set, stages != ShaderStages.Compute);

            if (stages == ShaderStages.Compute)
            {
                UIntPtr index = slot + baseBuffer;

                if (!_boundComputeBuffers.TryGetValue(index, out var boundBuffer) || !range.Equals(boundBuffer))
                {
                    _cce.setBuffer(mtlBuffer.DeviceBuffer, range.Offset, slot + baseBuffer);
                    _boundComputeBuffers[index] = range;
                }
            }
            else
            {
                if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
                {
                    UIntPtr index = _graphicsPipeline.ResourceBindingModel == ResourceBindingModel.Improved
                        ? slot + baseBuffer
                        : slot + _vertexBufferCount + baseBuffer;

                    if (!_boundVertexBuffers.TryGetValue(index, out var boundBuffer) || boundBuffer.Buffer != range.Buffer)
                    {
                        _rce.setVertexBuffer(mtlBuffer.DeviceBuffer, range.Offset, index);
                        _boundVertexBuffers[index] = range;
                    }
                    else if (!range.Equals(boundBuffer))
                    {
                        _rce.setVertexBufferOffset(range.Offset, index);
                        _boundVertexBuffers[index] = range;
                    }
                }

                if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
                {
                    UIntPtr index = slot + baseBuffer;

                    if (!_boundFragmentBuffers.TryGetValue(index, out var boundBuffer) || boundBuffer.Buffer != range.Buffer)
                    {
                        _rce.setFragmentBuffer(mtlBuffer.DeviceBuffer, range.Offset, slot + baseBuffer);
                        _boundFragmentBuffers[index] = range;
                    }
                    else if (!range.Equals(boundBuffer))
                    {
                        _rce.setFragmentBufferOffset(range.Offset, slot + baseBuffer);
                        _boundFragmentBuffers[index] = range;
                    }
                }
            }
        }

        private void BindTexture(MTLTextureView mtlTexView, uint set, uint slot, ShaderStages stages)
        {
            uint baseTexture = GetTextureBase(set, stages != ShaderStages.Compute);
            UIntPtr index = slot + baseTexture;

            if (stages == ShaderStages.Compute && (!_boundComputeTextures.TryGetValue(index, out var computeTexture) || computeTexture.NativePtr != mtlTexView.TargetDeviceTexture.NativePtr))
            {
                _cce.setTexture(mtlTexView.TargetDeviceTexture, index);
                _boundComputeTextures[index] = mtlTexView.TargetDeviceTexture;
            }

            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex
                && (!_boundVertexTextures.TryGetValue(index, out var vertexTexture) || vertexTexture.NativePtr != mtlTexView.TargetDeviceTexture.NativePtr))
            {
                _rce.setVertexTexture(mtlTexView.TargetDeviceTexture, index);
                _boundVertexTextures[index] = mtlTexView.TargetDeviceTexture;
            }

            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment
                && (!_boundFragmentTextures.TryGetValue(index, out var fragmentTexture) || fragmentTexture.NativePtr != mtlTexView.TargetDeviceTexture.NativePtr))
            {
                _rce.setFragmentTexture(mtlTexView.TargetDeviceTexture, index);
                _boundFragmentTextures[index] = mtlTexView.TargetDeviceTexture;
            }
        }

        private void BindSampler(MTLSampler mtlSampler, uint set, uint slot, ShaderStages stages)
        {
            uint baseSampler = GetSamplerBase(set, stages != ShaderStages.Compute);
            UIntPtr index = slot + baseSampler;

            if (stages == ShaderStages.Compute && (!_boundComputeSamplers.TryGetValue(index, out var computeSampler) || computeSampler.NativePtr != mtlSampler.DeviceSampler.NativePtr))
            {
                _cce.setSamplerState(mtlSampler.DeviceSampler, index);
                _boundComputeSamplers[index] = mtlSampler.DeviceSampler;
            }

            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex
                && (!_boundVertexSamplers.TryGetValue(index, out var vertexSampler) || vertexSampler.NativePtr != mtlSampler.DeviceSampler.NativePtr))
            {
                _rce.setVertexSamplerState(mtlSampler.DeviceSampler, index);
                _boundVertexSamplers[index] = mtlSampler.DeviceSampler;
            }

            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment
                && (!_boundFragmentSamplers.TryGetValue(index, out var fragmentSampler) || fragmentSampler.NativePtr != mtlSampler.DeviceSampler.NativePtr))
            {
                _rce.setFragmentSamplerState(mtlSampler.DeviceSampler, index);
                _boundFragmentSamplers[index] = mtlSampler.DeviceSampler;
            }
        }

        private uint GetBufferBase(uint set, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            uint ret = 0;

            for (int i = 0; i < set; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].BufferCount;
            }

            return ret;
        }

        private uint GetTextureBase(uint set, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            uint ret = 0;

            for (int i = 0; i < set; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].TextureCount;
            }

            return ret;
        }

        private uint GetSamplerBase(uint set, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            uint ret = 0;

            for (int i = 0; i < set; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].SamplerCount;
            }

            return ret;
        }

        private bool EnsureRenderPass()
        {
            Debug.Assert(_mtlFramebuffer != null);
            EnsureNoBlitEncoder();
            EnsureNoComputeEncoder();
            return RenderEncoderActive || BeginCurrentRenderPass();
        }

        private bool BeginCurrentRenderPass()
        {
            if (_mtlFramebuffer is MTLSwapchainFramebuffer swapchainFramebuffer && !swapchainFramebuffer.EnsureDrawableAvailable())
                return false;

            var rpDesc = _mtlFramebuffer.CreateRenderPassDescriptor();

            for (uint i = 0; i < _clearColors.Length; i++)
            {
                if (_clearColors[i] != null)
                {
                    var attachment = rpDesc.colorAttachments[0];
                    attachment.loadAction = MTLLoadAction.Clear;
                    var c = _clearColors[i].Value;
                    attachment.clearColor = new MTLClearColor(c.R, c.G, c.B, c.A);
                    _clearColors[i] = null;
                }
            }

            if (_clearDepth != null)
            {
                var depthAttachment = rpDesc.depthAttachment;
                depthAttachment.loadAction = MTLLoadAction.Clear;
                depthAttachment.clearDepth = _clearDepth.Value.depth;

                if (FormatHelpers.IsStencilFormat(_mtlFramebuffer.DepthTarget.Value.Target.Format))
                {
                    var stencilAttachment = rpDesc.stencilAttachment;
                    stencilAttachment.loadAction = MTLLoadAction.Clear;
                    stencilAttachment.clearStencil = _clearDepth.Value.stencil;
                }

                _clearDepth = null;
            }

            using (NSAutoreleasePool.Begin())
            {
                _rce = _cb.renderCommandEncoderWithDescriptor(rpDesc);
                ObjectiveCRuntime.retain(_rce.NativePtr);
            }

            ObjectiveCRuntime.release(rpDesc.NativePtr);
            _currentFramebufferEverActive = true;

            return true;
        }

        private void EnsureNoRenderPass()
        {
            if (RenderEncoderActive) EndCurrentRenderPass();

            Debug.Assert(!RenderEncoderActive);
        }

        private void EndCurrentRenderPass()
        {
            _rce.endEncoding();
            ObjectiveCRuntime.release(_rce.NativePtr);
            _rce = default;

            _lastGraphicsPipeline = null;
            _boundVertexBuffers.Clear();
            _boundVertexTextures.Clear();
            _boundVertexSamplers.Clear();
            _boundFragmentBuffers.Clear();
            _boundFragmentTextures.Clear();
            _boundFragmentSamplers.Clear();
            Util.ClearArray(_graphicsResourceSetsActive);
            Util.ClearArray(_vertexBuffersActive);
            Util.ClearArray(_vbOffsetsActive);

            Util.ClearArray(_activeScissorRects);

            _viewportsChanged = true;
        }

        private void EnsureBlitEncoder()
        {
            if (!BlitEncoderActive)
            {
                EnsureNoRenderPass();
                EnsureNoComputeEncoder();

                using (NSAutoreleasePool.Begin())
                {
                    _bce = _cb.blitCommandEncoder();
                    ObjectiveCRuntime.retain(_bce.NativePtr);
                }
            }

            Debug.Assert(BlitEncoderActive);
            Debug.Assert(!RenderEncoderActive);
            Debug.Assert(!ComputeEncoderActive);
        }

        private void EnsureNoBlitEncoder()
        {
            if (BlitEncoderActive)
            {
                _bce.endEncoding();
                ObjectiveCRuntime.release(_bce.NativePtr);
                _bce = default;
            }

            Debug.Assert(!BlitEncoderActive);
        }

        private void EnsureComputeEncoder()
        {
            if (!ComputeEncoderActive)
            {
                EnsureNoBlitEncoder();
                EnsureNoRenderPass();

                using (NSAutoreleasePool.Begin())
                {
                    _cce = _cb.computeCommandEncoder();
                    ObjectiveCRuntime.retain(_cce.NativePtr);
                }
            }

            Debug.Assert(ComputeEncoderActive);
            Debug.Assert(!RenderEncoderActive);
            Debug.Assert(!BlitEncoderActive);
        }

        private void EnsureNoComputeEncoder()
        {
            if (ComputeEncoderActive)
            {
                _cce.endEncoding();
                ObjectiveCRuntime.release(_cce.NativePtr);
                _cce = default;

                _boundComputeBuffers.Clear();
                _boundComputeTextures.Clear();
                _boundComputeSamplers.Clear();
                _lastComputePipeline = null;

                Util.ClearArray(_computeResourceSetsActive);
            }

            Debug.Assert(!ComputeEncoderActive);
        }

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
        {
            EnsureNoRenderPass();
            _clearColors[index] = clearColor;
        }

        private protected override void ClearDepthStencilCore(float depth, byte stencil)
        {
            EnsureNoRenderPass();
            _clearDepth = (depth, stencil);
        }

        private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            if (PreDrawCommand())
            {
                if (instanceStart == 0)
                {
                    _rce.drawPrimitives(
                        _graphicsPipeline.PrimitiveType,
                        vertexStart,
                        vertexCount,
                        instanceCount);
                }
                else
                {
                    _rce.drawPrimitives(
                        _graphicsPipeline.PrimitiveType,
                        vertexStart,
                        vertexCount,
                        instanceCount,
                        instanceStart);
                }
            }
        }

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            if (PreDrawCommand())
            {
                uint indexSize = _indexType == MTLIndexType.UInt16 ? 2u : 4u;
                uint indexBufferOffset = indexSize * indexStart + _ibOffset;

                if (vertexOffset == 0 && instanceStart == 0)
                {
                    _rce.drawIndexedPrimitives(
                        _graphicsPipeline.PrimitiveType,
                        indexCount,
                        _indexType,
                        _indexBuffer.DeviceBuffer,
                        indexBufferOffset,
                        instanceCount);
                }
                else
                {
                    _rce.drawIndexedPrimitives(
                        _graphicsPipeline.PrimitiveType,
                        indexCount,
                        _indexType,
                        _indexBuffer.DeviceBuffer,
                        indexBufferOffset,
                        instanceCount,
                        vertexOffset,
                        instanceStart);
                }
            }
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            if (pipeline.IsComputePipeline && _computePipeline != pipeline)
            {
                _computePipeline = Util.AssertSubtype<Pipeline, MTLPipeline>(pipeline);
                _computeResourceSetCount = (uint)_computePipeline.ResourceLayouts.Length;
                Util.EnsureArrayMinimumSize(ref _computeResourceSets, _computeResourceSetCount);
                Util.EnsureArrayMinimumSize(ref _computeResourceSetsActive, _computeResourceSetCount);
                Util.ClearArray(_computeResourceSetsActive);
            }
            else if (!pipeline.IsComputePipeline && _graphicsPipeline != pipeline)
            {
                _graphicsPipeline = Util.AssertSubtype<Pipeline, MTLPipeline>(pipeline);
                _graphicsResourceSetCount = (uint)_graphicsPipeline.ResourceLayouts.Length;
                Util.EnsureArrayMinimumSize(ref _graphicsResourceSets, _graphicsResourceSetCount);
                Util.EnsureArrayMinimumSize(ref _graphicsResourceSetsActive, _graphicsResourceSetCount);
                Util.ClearArray(_graphicsResourceSetsActive);

                _nonVertexBufferCount = _graphicsPipeline.NonVertexBufferCount;

                _vertexBufferCount = _graphicsPipeline.VertexBufferCount;
                Util.EnsureArrayMinimumSize(ref _vertexBuffers, _vertexBufferCount);
                Util.EnsureArrayMinimumSize(ref _vbOffsets, _vertexBufferCount);
                Util.EnsureArrayMinimumSize(ref _vertexBuffersActive, _vertexBufferCount);
                Util.EnsureArrayMinimumSize(ref _vbOffsetsActive, _vertexBufferCount);
                Util.ClearArray(_vertexBuffersActive);
                Util.ClearArray(_vbOffsetsActive);
            }
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            bool useComputeCopy = bufferOffsetInBytes % 4 != 0
                                  || (sizeInBytes % 4 != 0 && bufferOffsetInBytes != 0 && sizeInBytes != buffer.SizeInBytes);

            var dstMTLBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(buffer);
            var staging = GetFreeStagingBuffer(sizeInBytes);

            _gd.UpdateBuffer(staging, 0, source, sizeInBytes);

            if (useComputeCopy)
                CopyBufferCore(staging, 0, buffer, bufferOffsetInBytes, sizeInBytes);
            else
            {
                Debug.Assert(bufferOffsetInBytes % 4 == 0);
                uint sizeRoundFactor = (4 - sizeInBytes % 4) % 4;
                EnsureBlitEncoder();
                _bce.copy(
                    staging.DeviceBuffer, UIntPtr.Zero,
                    dstMTLBuffer.DeviceBuffer, bufferOffsetInBytes,
                    sizeInBytes + sizeRoundFactor);
            }

            lock (_submittedCommandsLock)
            {
                if (!_submittedStagingBuffers.TryGetValue(_cb, out var bufferList)) _submittedStagingBuffers[_cb] = bufferList = new List<MTLBuffer>();

                bufferList.Add(staging);
            }
        }

        private protected override void GenerateMipmapsCore(Texture texture)
        {
            Debug.Assert(texture.MipLevels > 1);
            EnsureBlitEncoder();
            var mtlTex = Util.AssertSubtype<Texture, MTLTexture>(texture);
            _bce.generateMipmapsForTexture(mtlTex.DeviceTexture);
        }

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            _indexBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(buffer);
            _ibOffset = offset;
            _indexType = MTLFormats.VdToMTLIndexFormat(format);
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            Util.EnsureArrayMinimumSize(ref _vertexBuffers, index + 1);
            Util.EnsureArrayMinimumSize(ref _vbOffsets, index + 1);
            Util.EnsureArrayMinimumSize(ref _vertexBuffersActive, index + 1);
            Util.EnsureArrayMinimumSize(ref _vbOffsetsActive, index + 1);

            if (_vertexBuffers[index] != buffer)
            {
                var mtlBuffer = Util.AssertSubtype<DeviceBuffer, MTLBuffer>(buffer);
                _vertexBuffers[index] = mtlBuffer;
                _vertexBuffersActive[index] = false;
            }

            if (_vbOffsets[index] != offset)
            {
                _vbOffsets[index] = offset;
                _vbOffsetsActive[index] = false;
            }
        }

        private protected override void PushDebugGroupCore(string name)
        {
            var nsName = NSString.New(name);
            if (!_bce.IsNull)
                _bce.pushDebugGroup(nsName);
            else if (!_cce.IsNull)
                _cce.pushDebugGroup(nsName);
            else if (!_rce.IsNull) _rce.pushDebugGroup(nsName);

            ObjectiveCRuntime.release(nsName);
        }

        private protected override void PopDebugGroupCore()
        {
            if (!_bce.IsNull)
                _bce.popDebugGroup();
            else if (!_cce.IsNull)
                _cce.popDebugGroup();
            else if (!_rce.IsNull) _rce.popDebugGroup();
        }

        private protected override void InsertDebugMarkerCore(string name)
        {
            var nsName = NSString.New(name);
            if (!_bce.IsNull)
                _bce.insertDebugSignpost(nsName);
            else if (!_cce.IsNull)
                _cce.insertDebugSignpost(nsName);
            else if (!_rce.IsNull) _rce.insertDebugSignpost(nsName);

            ObjectiveCRuntime.release(nsName);
        }
    }
}
