using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vortice;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Veldrid.D3D11
{
    internal class D3D11CommandList : CommandList
    {
        public ID3D11CommandList DeviceCommandList => _commandList;

        public override bool IsDisposed => _disposed;

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                DeviceContext.DebugName = value;
            }
        }

        internal ID3D11DeviceContext DeviceContext { get; }

        private readonly D3D11GraphicsDevice _gd;
        private readonly ID3D11DeviceContext1 _context1;
        private readonly ID3DUserDefinedAnnotation _uda;

        // Cached resources
        private const int MaxCachedUniformBuffers = 15;
        private readonly D3D11BufferRange[] _vertexBoundUniformBuffers = new D3D11BufferRange[MaxCachedUniformBuffers];
        private readonly D3D11BufferRange[] _fragmentBoundUniformBuffers = new D3D11BufferRange[MaxCachedUniformBuffers];
        private const int MaxCachedTextureViews = 16;
        private readonly D3D11TextureView[] _vertexBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
        private readonly D3D11TextureView[] _fragmentBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
        private const int MaxCachedSamplers = 4;
        private readonly D3D11Sampler[] _vertexBoundSamplers = new D3D11Sampler[MaxCachedSamplers];
        private readonly D3D11Sampler[] _fragmentBoundSamplers = new D3D11Sampler[MaxCachedSamplers];

        private readonly Dictionary<Texture, List<BoundTextureInfo>> _boundSRVs = new Dictionary<Texture, List<BoundTextureInfo>>();
        private readonly Dictionary<Texture, List<BoundTextureInfo>> _boundUAVs = new Dictionary<Texture, List<BoundTextureInfo>>();
        private readonly List<List<BoundTextureInfo>> _boundTextureInfoPool = new List<List<BoundTextureInfo>>(20);

        private const int MaxUAVs = 8;
        private readonly List<(DeviceBuffer, int)> _boundComputeUAVBuffers = new List<(DeviceBuffer, int)>(MaxUAVs);
        private readonly List<(DeviceBuffer, int)> _boundOMUAVBuffers = new List<(DeviceBuffer, int)>(MaxUAVs);

        private readonly List<D3D11Buffer> _availableStagingBuffers = new List<D3D11Buffer>();
        private readonly List<D3D11Buffer> _submittedStagingBuffers = new List<D3D11Buffer>();

        private readonly List<D3D11Swapchain> _referencedSwapchains = new List<D3D11Swapchain>();

        private D3D11Framebuffer D3D11Framebuffer => Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(_framebuffer);
        private bool _begun;
        private bool _disposed;
        private ID3D11CommandList _commandList;

        private Viewport[] _viewports = new Viewport[0];
        private RawRect[] _scissors = new RawRect[0];
        private bool _viewportsChanged;
        private bool _scissorRectsChanged;

        private uint _numVertexBindings;
        private ID3D11Buffer[] _vertexBindings = new ID3D11Buffer[1];
        private int[] _vertexStrides = new int[1];
        private int[] _vertexOffsets = new int[1];

        // Cached pipeline State
        private DeviceBuffer _ib;
        private uint _ibOffset;
        private ID3D11BlendState _blendState;
        private Color4 _blendFactor;
        private ID3D11DepthStencilState _depthStencilState;
        private uint _stencilReference;
        private ID3D11RasterizerState _rasterizerState;
        private Vortice.Direct3D.PrimitiveTopology _primitiveTopology;
        private ID3D11InputLayout _inputLayout;
        private ID3D11VertexShader _vertexShader;
        private ID3D11GeometryShader _geometryShader;
        private ID3D11HullShader _hullShader;
        private ID3D11DomainShader _domainShader;
        private ID3D11PixelShader _pixelShader;

        private new D3D11Pipeline _graphicsPipeline;

        private BoundResourceSetInfo[] _graphicsResourceSets = new BoundResourceSetInfo[1];

        // Resource sets are invalidated when a new resource set is bound with an incompatible SRV or UAV.
        private bool[] _invalidatedGraphicsResourceSets = new bool[1];

        private new D3D11Pipeline _computePipeline;

        private BoundResourceSetInfo[] _computeResourceSets = new BoundResourceSetInfo[1];

        // Resource sets are invalidated when a new resource set is bound with an incompatible SRV or UAV.
        private bool[] _invalidatedComputeResourceSets = new bool[1];
        private string _name;
        private bool _vertexBindingsChanged;
        private readonly ID3D11Buffer[] _cbOut = new ID3D11Buffer[1];
        private readonly int[] _firstConstRef = new int[1];
        private readonly int[] _numConstsRef = new int[1];

        public D3D11CommandList(D3D11GraphicsDevice gd, ref CommandListDescription description)
            : base(ref description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
        {
            _gd = gd;
            DeviceContext = gd.Device.CreateDeferredContext();
            _context1 = DeviceContext.QueryInterfaceOrNull<ID3D11DeviceContext1>();
            _uda = DeviceContext.QueryInterfaceOrNull<ID3DUserDefinedAnnotation>();
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposed)
            {
                _uda?.Dispose();
                DeviceCommandList?.Dispose();
                _context1?.Dispose();
                DeviceContext.Dispose();

                foreach (var boundGraphicsSet in _graphicsResourceSets) boundGraphicsSet.Offsets.Dispose();

                foreach (var boundComputeSet in _computeResourceSets) boundComputeSet.Offsets.Dispose();

                foreach (var buffer in _availableStagingBuffers) buffer.Dispose();
                _availableStagingBuffers.Clear();

                _disposed = true;
            }
        }

        #endregion

        public override void Begin()
        {
            _commandList?.Dispose();
            _commandList = null;
            ClearState();
            _begun = true;
        }

        public override void End()
        {
            if (_commandList != null) throw new VeldridException("Invalid use of End().");

            DeviceContext.FinishCommandList(false, out _commandList).CheckError();
            _commandList.DebugName = _name;
            ResetManagedState();
            _begun = false;
        }

        public void Reset()
        {
            if (_commandList != null)
            {
                _commandList.Dispose();
                _commandList = null;
            }
            else if (_begun)
            {
                DeviceContext.ClearState();
                DeviceContext.FinishCommandList(false, out _commandList);
                _commandList.Dispose();
                _commandList = null;
            }

            ResetManagedState();
            _begun = false;
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            PreDispatchCommand();

            DeviceContext.Dispatch((int)groupCountX, (int)groupCountY, (int)groupCountZ);
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            _scissorRectsChanged = true;
            Util.EnsureArrayMinimumSize(ref _scissors, index + 1);
            _scissors[index] = new RawRect((int)x, (int)y, (int)(x + width), (int)(y + height));
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            _viewportsChanged = true;
            Util.EnsureArrayMinimumSize(ref _viewports, index + 1);
            _viewports[index] = viewport;
        }

        internal void OnCompleted()
        {
            _commandList.Dispose();
            _commandList = null;

            foreach (var sc in _referencedSwapchains) sc.RemoveCommandListReference(this);
            _referencedSwapchains.Clear();

            foreach (var buffer in _submittedStagingBuffers) _availableStagingBuffers.Add(buffer);

            _submittedStagingBuffers.Clear();
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            if (_graphicsResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets)) return;

            _graphicsResourceSets[slot].Offsets.Dispose();
            _graphicsResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
            ActivateResourceSet(slot, _graphicsResourceSets[slot], true);
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet set, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            if (_computeResourceSets[slot].Equals(set, dynamicOffsetsCount, ref dynamicOffsets)) return;

            _computeResourceSets[slot].Offsets.Dispose();
            _computeResourceSets[slot] = new BoundResourceSetInfo(set, dynamicOffsetsCount, ref dynamicOffsets);
            ActivateResourceSet(slot, _computeResourceSets[slot], false);
        }

        protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            PreDrawCommand();

            var d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(indirectBuffer);
            int currentOffset = (int)offset;

            for (uint i = 0; i < drawCount; i++)
            {
                DeviceContext.DrawInstancedIndirect(d3d11Buffer.Buffer, currentOffset);
                currentOffset += (int)stride;
            }
        }

        protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            PreDrawCommand();

            var d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(indirectBuffer);
            int currentOffset = (int)offset;

            for (uint i = 0; i < drawCount; i++)
            {
                DeviceContext.DrawIndexedInstancedIndirect(d3d11Buffer.Buffer, currentOffset);
                currentOffset += (int)stride;
            }
        }

        protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
        {
            PreDispatchCommand();
            var d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(indirectBuffer);
            DeviceContext.DispatchIndirect(d3d11Buffer.Buffer, (int)offset);
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            var d3d11Source = Util.AssertSubtype<Texture, D3D11Texture>(source);
            var d3d11Destination = Util.AssertSubtype<Texture, D3D11Texture>(destination);
            DeviceContext.ResolveSubresource(
                d3d11Destination.DeviceTexture,
                0,
                d3d11Source.DeviceTexture,
                0,
                d3d11Destination.DxgiFormat);
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            var d3dFB = Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(fb);

            if (d3dFB.Swapchain != null)
            {
                d3dFB.Swapchain.AddCommandListReference(this);
                _referencedSwapchains.Add(d3dFB.Swapchain);
            }

            for (int i = 0; i < fb.ColorTargets.Count; i++) UnbindSRVTexture(fb.ColorTargets[i].Target);

            DeviceContext.OMSetRenderTargets(d3dFB.RenderTargetViews, d3dFB.DepthStencilView);
        }

        protected override void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
        {
            var srcD3D11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(source);
            var dstD3D11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(destination);

            var region = new Box((int)sourceOffset, 0, 0, (int)(sourceOffset + sizeInBytes), 1, 1);

            DeviceContext.CopySubresourceRegion(dstD3D11Buffer.Buffer, 0, (int)destinationOffset, 0, 0, srcD3D11Buffer.Buffer, 0, region);
        }

        protected override void CopyTextureCore(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            var srcD3D11Texture = Util.AssertSubtype<Texture, D3D11Texture>(source);
            var dstD3D11Texture = Util.AssertSubtype<Texture, D3D11Texture>(destination);

            uint blockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
            uint clampedWidth = Math.Max(blockSize, width);
            uint clampedHeight = Math.Max(blockSize, height);

            Box? region = null;

            if (srcX != 0 || srcY != 0 || srcZ != 0
                || clampedWidth != source.Width || clampedHeight != source.Height || depth != source.Depth)
            {
                region = new Box(
                    (int)srcX,
                    (int)srcY,
                    (int)srcZ,
                    (int)(srcX + clampedWidth),
                    (int)(srcY + clampedHeight),
                    (int)(srcZ + depth));
            }

            for (uint i = 0; i < layerCount; i++)
            {
                int srcSubresource = D3D11Util.ComputeSubresource(srcMipLevel, source.MipLevels, srcBaseArrayLayer + i);
                int dstSubresource = D3D11Util.ComputeSubresource(dstMipLevel, destination.MipLevels, dstBaseArrayLayer + i);

                DeviceContext.CopySubresourceRegion(
                    dstD3D11Texture.DeviceTexture,
                    dstSubresource,
                    (int)dstX,
                    (int)dstY,
                    (int)dstZ,
                    srcD3D11Texture.DeviceTexture,
                    srcSubresource,
                    region);
            }
        }

        private void ClearState()
        {
            ClearCachedState();
            DeviceContext.ClearState();
            ResetManagedState();
        }

        private void ResetManagedState()
        {
            _numVertexBindings = 0;
            Util.ClearArray(_vertexBindings);
            Util.ClearArray(_vertexStrides);
            Util.ClearArray(_vertexOffsets);

            _framebuffer = null;

            Util.ClearArray(_viewports);
            Util.ClearArray(_scissors);
            _viewportsChanged = false;
            _scissorRectsChanged = false;

            _ib = null;
            _graphicsPipeline = null;
            _blendState = null;
            _depthStencilState = null;
            _rasterizerState = null;
            _primitiveTopology = Vortice.Direct3D.PrimitiveTopology.Undefined;
            _inputLayout = null;
            _vertexShader = null;
            _geometryShader = null;
            _hullShader = null;
            _domainShader = null;
            _pixelShader = null;

            ClearSets(_graphicsResourceSets);

            Util.ClearArray(_vertexBoundUniformBuffers);
            Util.ClearArray(_vertexBoundTextureViews);
            Util.ClearArray(_vertexBoundSamplers);

            Util.ClearArray(_fragmentBoundUniformBuffers);
            Util.ClearArray(_fragmentBoundTextureViews);
            Util.ClearArray(_fragmentBoundSamplers);

            _computePipeline = null;
            ClearSets(_computeResourceSets);

            foreach (var kvp in _boundSRVs)
            {
                var list = kvp.Value;
                list.Clear();
                PoolBoundTextureList(list);
            }

            _boundSRVs.Clear();

            foreach (var kvp in _boundUAVs)
            {
                var list = kvp.Value;
                list.Clear();
                PoolBoundTextureList(list);
            }

            _boundUAVs.Clear();
        }

        private void ClearSets(BoundResourceSetInfo[] boundSets)
        {
            foreach (var boundSetInfo in boundSets) boundSetInfo.Offsets.Dispose();
            Util.ClearArray(boundSets);
        }

        private void ActivateResourceSet(uint slot, BoundResourceSetInfo brsi, bool graphics)
        {
            var d3d11RS = Util.AssertSubtype<ResourceSet, D3D11ResourceSet>(brsi.Set);

            int cbBase = GetConstantBufferBase(slot, graphics);
            int uaBase = GetUnorderedAccessBase(slot, graphics);
            int textureBase = GetTextureBase(slot, graphics);
            int samplerBase = GetSamplerBase(slot, graphics);

            var layout = d3d11RS.Layout;
            var resources = d3d11RS.Resources;
            uint dynamicOffsetIndex = 0;

            for (int i = 0; i < resources.Length; i++)
            {
                var resource = resources[i];
                uint bufferOffset = 0;

                if (layout.IsDynamicBuffer(i))
                {
                    bufferOffset = brsi.Offsets.Get(dynamicOffsetIndex);
                    dynamicOffsetIndex += 1;
                }

                var rbi = layout.GetDeviceSlotIndex(i);

                switch (rbi.Kind)
                {
                    case ResourceKind.UniformBuffer:
                    {
                        var range = GetBufferRange(resource, bufferOffset);
                        BindUniformBuffer(range, cbBase + rbi.Slot, rbi.Stages);
                        break;
                    }

                    case ResourceKind.StructuredBufferReadOnly:
                    {
                        var range = GetBufferRange(resource, bufferOffset);
                        BindStorageBufferView(range, textureBase + rbi.Slot, rbi.Stages);
                        break;
                    }

                    case ResourceKind.StructuredBufferReadWrite:
                    {
                        var range = GetBufferRange(resource, bufferOffset);
                        var uav = range.Buffer.GetUnorderedAccessView(range.Offset, range.Size);
                        BindUnorderedAccessView(null, range.Buffer, uav, uaBase + rbi.Slot, rbi.Stages, slot);
                        break;
                    }

                    case ResourceKind.TextureReadOnly:
                        var texView = Util.GetTextureView(_gd, resource);
                        var d3d11TexView = Util.AssertSubtype<TextureView, D3D11TextureView>(texView);
                        UnbindUAVTexture(d3d11TexView.Target);
                        BindTextureView(d3d11TexView, textureBase + rbi.Slot, rbi.Stages, slot);
                        break;

                    case ResourceKind.TextureReadWrite:
                        var rwTexView = Util.GetTextureView(_gd, resource);
                        var d3d11RWTexView = Util.AssertSubtype<TextureView, D3D11TextureView>(rwTexView);
                        UnbindSRVTexture(d3d11RWTexView.Target);
                        BindUnorderedAccessView(d3d11RWTexView.Target, null, d3d11RWTexView.UnorderedAccessView, uaBase + rbi.Slot, rbi.Stages, slot);
                        break;

                    case ResourceKind.Sampler:
                        var sampler = Util.AssertSubtype<BindableResource, D3D11Sampler>(resource);
                        BindSampler(sampler, samplerBase + rbi.Slot, rbi.Stages);
                        break;

                    default: throw Illegal.Value<ResourceKind>();
                }
            }
        }

        private D3D11BufferRange GetBufferRange(BindableResource resource, uint additionalOffset)
        {
            if (resource is D3D11Buffer d3d11Buff)
                return new D3D11BufferRange(d3d11Buff, additionalOffset, d3d11Buff.SizeInBytes);

            if (resource is DeviceBufferRange range)
            {
                return new D3D11BufferRange(
                    Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(range.Buffer),
                    range.Offset + additionalOffset,
                    range.SizeInBytes);
            }

            throw new VeldridException($"Unexpected resource type used in a buffer type slot: {resource.GetType().Name}");
        }

        private void UnbindSRVTexture(Texture target)
        {
            if (_boundSRVs.TryGetValue(target, out var btis))
            {
                foreach (var bti in btis)
                {
                    BindTextureView(null, bti.Slot, bti.Stages, 0);

                    if ((bti.Stages & ShaderStages.Compute) == ShaderStages.Compute)
                        _invalidatedComputeResourceSets[bti.ResourceSet] = true;
                    else
                        _invalidatedGraphicsResourceSets[bti.ResourceSet] = true;
                }

                bool result = _boundSRVs.Remove(target);
                Debug.Assert(result);

                btis.Clear();
                PoolBoundTextureList(btis);
            }
        }

        private void PoolBoundTextureList(List<BoundTextureInfo> btis)
        {
            _boundTextureInfoPool.Add(btis);
        }

        private void UnbindUAVTexture(Texture target)
        {
            if (_boundUAVs.TryGetValue(target, out var btis))
            {
                foreach (var bti in btis)
                {
                    BindUnorderedAccessView(null, null, null, bti.Slot, bti.Stages, bti.ResourceSet);
                    if ((bti.Stages & ShaderStages.Compute) == ShaderStages.Compute)
                        _invalidatedComputeResourceSets[bti.ResourceSet] = true;
                    else
                        _invalidatedGraphicsResourceSets[bti.ResourceSet] = true;
                }

                bool result = _boundUAVs.Remove(target);
                Debug.Assert(result);

                btis.Clear();
                PoolBoundTextureList(btis);
            }
        }

        private int GetConstantBufferBase(uint slot, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;

            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].UniformBufferCount;
            }

            return ret;
        }

        private int GetUnorderedAccessBase(uint slot, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;

            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].StorageBufferCount;
            }

            return ret;
        }

        private int GetTextureBase(uint slot, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;

            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].TextureCount;
            }

            return ret;
        }

        private int GetSamplerBase(uint slot, bool graphics)
        {
            var layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;

            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].SamplerCount;
            }

            return ret;
        }

        private void PreDrawCommand()
        {
            FlushViewports();
            FlushScissorRects();
            FlushVertexBindings();

            int graphicsResourceCount = _graphicsPipeline.ResourceLayouts.Length;

            for (uint i = 0; i < graphicsResourceCount; i++)
            {
                if (_invalidatedGraphicsResourceSets[i])
                {
                    _invalidatedGraphicsResourceSets[i] = false;
                    ActivateResourceSet(i, _graphicsResourceSets[i], true);
                }
            }
        }

        private void PreDispatchCommand()
        {
            int computeResourceCount = _computePipeline.ResourceLayouts.Length;

            for (uint i = 0; i < computeResourceCount; i++)
            {
                if (_invalidatedComputeResourceSets[i])
                {
                    _invalidatedComputeResourceSets[i] = false;
                    ActivateResourceSet(i, _computeResourceSets[i], false);
                }
            }
        }

        private void FlushViewports()
        {
            if (_viewportsChanged)
            {
                _viewportsChanged = false;
                DeviceContext.RSSetViewports(_viewports);
            }
        }

        private void FlushScissorRects()
        {
            if (_scissorRectsChanged)
            {
                _scissorRectsChanged = false;

                if (_scissors.Length > 0)
                {
                    // Because this array is resized using Util.EnsureMinimumArraySize, this might set more scissor rectangles
                    // than are actually needed, but this is okay -- extras are essentially ignored and should be harmless.
                    DeviceContext.RSSetScissorRects(_scissors);
                }
            }
        }

        private void FlushVertexBindings()
        {
            if (_vertexBindingsChanged)
            {
                DeviceContext.IASetVertexBuffers(
                    0, (int)_numVertexBindings,
                    _vertexBindings,
                    _vertexStrides,
                    _vertexOffsets);

                _vertexBindingsChanged = false;
            }
        }

        private void BindTextureView(D3D11TextureView texView, int slot, ShaderStages stages, uint resourceSet)
        {
            var srv = texView?.ShaderResourceView ?? null;

            if (srv != null)
            {
                if (!_boundSRVs.TryGetValue(texView.Target, out var list))
                {
                    list = GetNewOrCachedBoundTextureInfoList();
                    _boundSRVs.Add(texView.Target, list);
                }

                list.Add(new BoundTextureInfo { Slot = slot, Stages = stages, ResourceSet = resourceSet });
            }

            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                bool bind = false;

                if (slot < MaxCachedUniformBuffers)
                {
                    if (_vertexBoundTextureViews[slot] != texView)
                    {
                        _vertexBoundTextureViews[slot] = texView;
                        bind = true;
                    }
                }
                else
                    bind = true;

                if (bind) DeviceContext.VSSetShaderResource(slot, srv);
            }

            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry) DeviceContext.GSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl) DeviceContext.HSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation) DeviceContext.DSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                bool bind = false;

                if (slot < MaxCachedUniformBuffers)
                {
                    if (_fragmentBoundTextureViews[slot] != texView)
                    {
                        _fragmentBoundTextureViews[slot] = texView;
                        bind = true;
                    }
                }
                else
                    bind = true;

                if (bind) DeviceContext.PSSetShaderResource(slot, srv);
            }

            if ((stages & ShaderStages.Compute) == ShaderStages.Compute) DeviceContext.CSSetShaderResource(slot, srv);
        }

        private List<BoundTextureInfo> GetNewOrCachedBoundTextureInfoList()
        {
            if (_boundTextureInfoPool.Count > 0)
            {
                int index = _boundTextureInfoPool.Count - 1;
                var ret = _boundTextureInfoPool[index];
                _boundTextureInfoPool.RemoveAt(index);
                return ret;
            }

            return new List<BoundTextureInfo>();
        }

        private void BindStorageBufferView(D3D11BufferRange range, int slot, ShaderStages stages)
        {
            bool compute = (stages & ShaderStages.Compute) != 0;
            UnbindUAVBuffer(range.Buffer);

            var srv = range.Buffer.GetShaderResourceView(range.Offset, range.Size);

            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex) DeviceContext.VSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry) DeviceContext.GSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl) DeviceContext.HSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation) DeviceContext.DSSetShaderResource(slot, srv);

            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment) DeviceContext.PSSetShaderResource(slot, srv);

            if (compute) DeviceContext.CSSetShaderResource(slot, srv);
        }

        private void BindUniformBuffer(D3D11BufferRange range, int slot, ShaderStages stages)
        {
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                bool bind = false;

                if (slot < MaxCachedUniformBuffers)
                {
                    if (!_vertexBoundUniformBuffers[slot].Equals(range))
                    {
                        _vertexBoundUniformBuffers[slot] = range;
                        bind = true;
                    }
                }
                else
                    bind = true;

                if (bind)
                {
                    if (range.IsFullRange)
                        DeviceContext.VSSetConstantBuffer(slot, range.Buffer.Buffer);
                    else
                    {
                        PackRangeParams(range);
                        if (!_gd.SupportsCommandLists) DeviceContext.VSUnsetConstantBuffer(slot);
                        _context1.VSSetConstantBuffers1(slot, 1, _cbOut, _firstConstRef, _numConstsRef);
                    }
                }
            }

            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            {
                if (range.IsFullRange)
                    DeviceContext.GSSetConstantBuffer(slot, range.Buffer.Buffer);
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists) DeviceContext.GSUnsetConstantBuffer(slot);
                    _context1.GSSetConstantBuffers1(slot, 1, _cbOut, _firstConstRef, _numConstsRef);
                }
            }

            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            {
                if (range.IsFullRange)
                    DeviceContext.HSSetConstantBuffer(slot, range.Buffer.Buffer);
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists) DeviceContext.HSUnsetConstantBuffer(slot);
                    _context1.HSSetConstantBuffers1(slot, 1, _cbOut, _firstConstRef, _numConstsRef);
                }
            }

            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            {
                if (range.IsFullRange)
                    DeviceContext.DSSetConstantBuffer(slot, range.Buffer.Buffer);
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists) DeviceContext.DSUnsetConstantBuffer(slot);
                    _context1.DSSetConstantBuffers1(slot, 1, _cbOut, _firstConstRef, _numConstsRef);
                }
            }

            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                bool bind = false;

                if (slot < MaxCachedUniformBuffers)
                {
                    if (!_fragmentBoundUniformBuffers[slot].Equals(range))
                    {
                        _fragmentBoundUniformBuffers[slot] = range;
                        bind = true;
                    }
                }
                else
                    bind = true;

                if (bind)
                {
                    if (range.IsFullRange)
                        DeviceContext.PSSetConstantBuffer(slot, range.Buffer.Buffer);
                    else
                    {
                        PackRangeParams(range);
                        if (!_gd.SupportsCommandLists) DeviceContext.PSUnsetConstantBuffer(slot);
                        _context1.PSSetConstantBuffers1(slot, 1, _cbOut, _firstConstRef, _numConstsRef);
                    }
                }
            }

            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
            {
                if (range.IsFullRange)
                    DeviceContext.CSSetConstantBuffer(slot, range.Buffer.Buffer);
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists) DeviceContext.CSSetConstantBuffer(slot, null);
                    _context1.CSSetConstantBuffers1(slot, 1, _cbOut, _firstConstRef, _numConstsRef);
                }
            }
        }

        private void PackRangeParams(D3D11BufferRange range)
        {
            _cbOut[0] = range.Buffer.Buffer;
            _firstConstRef[0] = (int)range.Offset / 16;
            uint roundedSize = range.Size < 256 ? 256u : range.Size;
            _numConstsRef[0] = (int)roundedSize / 16;
        }

        private void BindUnorderedAccessView(
            Texture texture,
            DeviceBuffer buffer,
            ID3D11UnorderedAccessView uav,
            int slot,
            ShaderStages stages,
            uint resourceSet)
        {
            bool compute = stages == ShaderStages.Compute;
            Debug.Assert(compute || (stages & ShaderStages.Compute) == 0);
            Debug.Assert(texture == null || buffer == null);

            if (texture != null && uav != null)
            {
                if (!_boundUAVs.TryGetValue(texture, out var list))
                {
                    list = GetNewOrCachedBoundTextureInfoList();
                    _boundUAVs.Add(texture, list);
                }

                list.Add(new BoundTextureInfo { Slot = slot, Stages = stages, ResourceSet = resourceSet });
            }

            int baseSlot = 0;
            if (!compute && _fragmentBoundSamplers != null) baseSlot = _framebuffer.ColorTargets.Count;
            int actualSlot = baseSlot + slot;

            if (buffer != null) TrackBoundUAVBuffer(buffer, actualSlot, compute);

            if (compute)
                DeviceContext.CSSetUnorderedAccessView(actualSlot, uav);
            else
                DeviceContext.OMSetUnorderedAccessView(actualSlot, uav);
        }

        private void TrackBoundUAVBuffer(DeviceBuffer buffer, int slot, bool compute)
        {
            var list = compute ? _boundComputeUAVBuffers : _boundOMUAVBuffers;
            list.Add((buffer, slot));
        }

        private void UnbindUAVBuffer(DeviceBuffer buffer)
        {
            UnbindUAVBufferIndividual(buffer, false);
            UnbindUAVBufferIndividual(buffer, true);
        }

        private void UnbindUAVBufferIndividual(DeviceBuffer buffer, bool compute)
        {
            var list = compute ? _boundComputeUAVBuffers : _boundOMUAVBuffers;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Item1 == buffer)
                {
                    int slot = list[i].Item2;
                    if (compute)
                        DeviceContext.CSUnsetUnorderedAccessView(slot);
                    else
                        DeviceContext.OMUnsetUnorderedAccessView(slot);

                    list.RemoveAt(i);
                    i -= 1;
                }
            }
        }

        private void BindSampler(D3D11Sampler sampler, int slot, ShaderStages stages)
        {
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                bool bind = false;

                if (slot < MaxCachedSamplers)
                {
                    if (_vertexBoundSamplers[slot] != sampler)
                    {
                        _vertexBoundSamplers[slot] = sampler;
                        bind = true;
                    }
                }
                else
                    bind = true;

                if (bind) DeviceContext.VSSetSampler(slot, sampler.DeviceSampler);
            }

            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry) DeviceContext.GSSetSampler(slot, sampler.DeviceSampler);

            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl) DeviceContext.HSSetSampler(slot, sampler.DeviceSampler);

            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation) DeviceContext.DSSetSampler(slot, sampler.DeviceSampler);

            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                bool bind = false;

                if (slot < MaxCachedSamplers)
                {
                    if (_fragmentBoundSamplers[slot] != sampler)
                    {
                        _fragmentBoundSamplers[slot] = sampler;
                        bind = true;
                    }
                }
                else
                    bind = true;

                if (bind) DeviceContext.PSSetSampler(slot, sampler.DeviceSampler);
            }

            if ((stages & ShaderStages.Compute) == ShaderStages.Compute) DeviceContext.CSSetSampler(slot, sampler.DeviceSampler);
        }

        private unsafe void UpdateSubresource_Workaround(
            ID3D11Resource resource,
            int subresource,
            Box region,
            IntPtr data)
        {
            bool needWorkaround = !_gd.SupportsCommandLists;
            var pAdjustedSrcData = data.ToPointer();

            if (needWorkaround)
            {
                Debug.Assert(region.Top == 0 && region.Front == 0);
                pAdjustedSrcData = (byte*)data - region.Left;
            }

            DeviceContext.UpdateSubresource(resource, subresource, region, (IntPtr)pAdjustedSrcData, 0, 0);
        }

        private D3D11Buffer GetFreeStagingBuffer(uint sizeInBytes)
        {
            foreach (var buffer in _availableStagingBuffers)
            {
                if (buffer.SizeInBytes >= sizeInBytes)
                {
                    _availableStagingBuffers.Remove(buffer);
                    return buffer;
                }
            }

            var staging = _gd.ResourceFactory.CreateBuffer(
                new BufferDescription(sizeInBytes, BufferUsage.Staging));

            return Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(staging);
        }

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            if (_ib != buffer || _ibOffset != offset)
            {
                _ib = buffer;
                _ibOffset = offset;
                var d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);
                UnbindUAVBuffer(buffer);
                DeviceContext.IASetIndexBuffer(d3d11Buffer.Buffer, D3D11Formats.ToDxgiFormat(format), (int)offset);
            }
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            if (!pipeline.IsComputePipeline && _graphicsPipeline != pipeline)
            {
                var d3dPipeline = Util.AssertSubtype<Pipeline, D3D11Pipeline>(pipeline);
                _graphicsPipeline = d3dPipeline;
                ClearSets(_graphicsResourceSets); // Invalidate resource set bindings -- they may be invalid.
                Util.ClearArray(_invalidatedGraphicsResourceSets);

                var blendState = d3dPipeline.BlendState;
                var blendFactor = d3dPipeline.BlendFactor;

                if (_blendState != blendState || _blendFactor != blendFactor)
                {
                    _blendState = blendState;
                    _blendFactor = blendFactor;
                    DeviceContext.OMSetBlendState(blendState, blendFactor);
                }

                var depthStencilState = d3dPipeline.DepthStencilState;
                uint stencilReference = d3dPipeline.StencilReference;

                if (_depthStencilState != depthStencilState || _stencilReference != stencilReference)
                {
                    _depthStencilState = depthStencilState;
                    _stencilReference = stencilReference;
                    DeviceContext.OMSetDepthStencilState(depthStencilState, (int)stencilReference);
                }

                var rasterizerState = d3dPipeline.RasterizerState;

                if (_rasterizerState != rasterizerState)
                {
                    _rasterizerState = rasterizerState;
                    DeviceContext.RSSetState(rasterizerState);
                }

                var primitiveTopology = d3dPipeline.PrimitiveTopology;

                if (_primitiveTopology != primitiveTopology)
                {
                    _primitiveTopology = primitiveTopology;
                    DeviceContext.IASetPrimitiveTopology(primitiveTopology);
                }

                var inputLayout = d3dPipeline.InputLayout;

                if (_inputLayout != inputLayout)
                {
                    _inputLayout = inputLayout;
                    DeviceContext.IASetInputLayout(inputLayout);
                }

                var vertexShader = d3dPipeline.VertexShader;

                if (_vertexShader != vertexShader)
                {
                    _vertexShader = vertexShader;
                    DeviceContext.VSSetShader(vertexShader);
                }

                var geometryShader = d3dPipeline.GeometryShader;

                if (_geometryShader != geometryShader)
                {
                    _geometryShader = geometryShader;
                    DeviceContext.GSSetShader(geometryShader);
                }

                var hullShader = d3dPipeline.HullShader;

                if (_hullShader != hullShader)
                {
                    _hullShader = hullShader;
                    DeviceContext.HSSetShader(hullShader);
                }

                var domainShader = d3dPipeline.DomainShader;

                if (_domainShader != domainShader)
                {
                    _domainShader = domainShader;
                    DeviceContext.DSSetShader(domainShader);
                }

                var pixelShader = d3dPipeline.PixelShader;

                if (_pixelShader != pixelShader)
                {
                    _pixelShader = pixelShader;
                    DeviceContext.PSSetShader(pixelShader);
                }

                if (!Util.ArrayEqualsEquatable(_vertexStrides, d3dPipeline.VertexStrides))
                {
                    _vertexBindingsChanged = true;

                    if (d3dPipeline.VertexStrides != null)
                    {
                        Util.EnsureArrayMinimumSize(ref _vertexStrides, (uint)d3dPipeline.VertexStrides.Length);
                        d3dPipeline.VertexStrides.CopyTo(_vertexStrides, 0);
                    }
                }

                Util.EnsureArrayMinimumSize(ref _vertexStrides, 1);
                Util.EnsureArrayMinimumSize(ref _vertexBindings, (uint)_vertexStrides.Length);
                Util.EnsureArrayMinimumSize(ref _vertexOffsets, (uint)_vertexStrides.Length);

                Util.EnsureArrayMinimumSize(ref _graphicsResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
                Util.EnsureArrayMinimumSize(ref _invalidatedGraphicsResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
            }
            else if (pipeline.IsComputePipeline && _computePipeline != pipeline)
            {
                var d3dPipeline = Util.AssertSubtype<Pipeline, D3D11Pipeline>(pipeline);
                _computePipeline = d3dPipeline;
                ClearSets(_computeResourceSets); // Invalidate resource set bindings -- they may be invalid.
                Util.ClearArray(_invalidatedComputeResourceSets);

                var computeShader = d3dPipeline.ComputeShader;
                DeviceContext.CSSetShader(computeShader);
                Util.EnsureArrayMinimumSize(ref _computeResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
                Util.EnsureArrayMinimumSize(ref _invalidatedComputeResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
            }
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            var d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);

            if (_vertexBindings[index] != d3d11Buffer.Buffer || _vertexOffsets[index] != offset)
            {
                _vertexBindingsChanged = true;
                UnbindUAVBuffer(buffer);
                _vertexBindings[index] = d3d11Buffer.Buffer;
                _vertexOffsets[index] = (int)offset;
                _numVertexBindings = Math.Max(index + 1, _numVertexBindings);
            }
        }

        private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            PreDrawCommand();

            if (instanceCount == 1 && instanceStart == 0)
                DeviceContext.Draw((int)vertexCount, (int)vertexStart);
            else
                DeviceContext.DrawInstanced((int)vertexCount, (int)instanceCount, (int)vertexStart, (int)instanceStart);
        }

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            PreDrawCommand();

            Debug.Assert(_ib != null);
            if (instanceCount == 1 && instanceStart == 0)
                DeviceContext.DrawIndexed((int)indexCount, (int)indexStart, vertexOffset);
            else
                DeviceContext.DrawIndexedInstanced((int)indexCount, (int)instanceCount, (int)indexStart, vertexOffset, (int)instanceStart);
        }

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
        {
            DeviceContext.ClearRenderTargetView(D3D11Framebuffer.RenderTargetViews[index], new Color4(clearColor.R, clearColor.G, clearColor.B, clearColor.A));
        }

        private protected override void ClearDepthStencilCore(float depth, byte stencil)
        {
            DeviceContext.ClearDepthStencilView(D3D11Framebuffer.DepthStencilView, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, depth, stencil);
        }

        private protected override unsafe void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            var d3dBuffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);
            if (sizeInBytes == 0) return;

            bool isDynamic = (buffer.Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;
            bool isStaging = (buffer.Usage & BufferUsage.Staging) == BufferUsage.Staging;
            bool isUniformBuffer = (buffer.Usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer;
            bool useMap = isDynamic;
            bool updateFullBuffer = bufferOffsetInBytes == 0 && sizeInBytes == buffer.SizeInBytes;
            bool useUpdateSubresource = !isDynamic && !isStaging && (!isUniformBuffer || updateFullBuffer);

            if (useUpdateSubresource)
            {
                Box? subregion = new Box((int)bufferOffsetInBytes, 0, 0, (int)(sizeInBytes + bufferOffsetInBytes), 1, 1);
                if (isUniformBuffer) subregion = null;

                if (bufferOffsetInBytes == 0)
                    DeviceContext.UpdateSubresource(d3dBuffer.Buffer, 0, subregion, source, 0, 0);
                else
                    UpdateSubresource_Workaround(d3dBuffer.Buffer, 0, subregion.Value, source);
            }
            else if (useMap && updateFullBuffer) // Can only update full buffer with WriteDiscard.
            {
                var msb = DeviceContext.Map(
                    d3dBuffer.Buffer,
                    0,
                    D3D11Formats.VdToD3D11MapMode(isDynamic, MapMode.Write));
                if (sizeInBytes < 1024)
                    Unsafe.CopyBlock(msb.DataPointer.ToPointer(), source.ToPointer(), sizeInBytes);
                else
                    Buffer.MemoryCopy(source.ToPointer(), msb.DataPointer.ToPointer(), buffer.SizeInBytes, sizeInBytes);
                DeviceContext.Unmap(d3dBuffer.Buffer, 0);
            }
            else
            {
                var staging = GetFreeStagingBuffer(sizeInBytes);
                _gd.UpdateBuffer(staging, 0, source, sizeInBytes);
                CopyBuffer(staging, 0, buffer, bufferOffsetInBytes, sizeInBytes);
                _submittedStagingBuffers.Add(staging);
            }
        }

        private protected override void GenerateMipmapsCore(Texture texture)
        {
            var fullTexView = texture.GetFullTextureView(_gd);
            var d3d11View = Util.AssertSubtype<TextureView, D3D11TextureView>(fullTexView);
            var srv = d3d11View.ShaderResourceView;
            DeviceContext.GenerateMips(srv);
        }

        private protected override void PushDebugGroupCore(string name)
        {
            _uda?.BeginEvent(name);
        }

        private protected override void PopDebugGroupCore()
        {
            _uda?.EndEvent();
        }

        private protected override void InsertDebugMarkerCore(string name)
        {
            _uda?.SetMarker(name);
        }

        private struct BoundTextureInfo
        {
            public int Slot;
            public ShaderStages Stages;
            public uint ResourceSet;
        }

        private struct D3D11BufferRange : IEquatable<D3D11BufferRange>
        {
            public readonly D3D11Buffer Buffer;
            public readonly uint Offset;
            public readonly uint Size;

            public bool IsFullRange => Offset == 0 && Size == Buffer.SizeInBytes;

            public D3D11BufferRange(D3D11Buffer buffer, uint offset, uint size)
            {
                Buffer = buffer;
                Offset = offset;
                Size = size;
            }

            public bool Equals(D3D11BufferRange other)
            {
                return Buffer == other.Buffer && Offset.Equals(other.Offset) && Size.Equals(other.Size);
            }
        }
    }
}
