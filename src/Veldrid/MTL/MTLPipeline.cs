using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal class MTLPipeline : Pipeline
    {
        public MTLRenderPipelineState RenderPipelineState { get; }
        public MTLComputePipelineState ComputePipelineState { get; }
        public MTLPrimitiveType PrimitiveType { get; }
        public new MTLResourceLayout[] ResourceLayouts { get; }
        public ResourceBindingModel ResourceBindingModel { get; }
        public uint VertexBufferCount { get; }
        public uint NonVertexBufferCount { get; }
        public MTLCullMode CullMode { get; }
        public MTLWinding FrontFace { get; }
        public MTLTriangleFillMode FillMode { get; }
        public MTLDepthStencilState DepthStencilState { get; }
        public MTLDepthClipMode DepthClipMode { get; }
        public override bool IsComputePipeline { get; }
        public bool ScissorTestEnabled { get; }
        public MTLSize ThreadsPerThreadgroup { get; } = new MTLSize(1, 1, 1);
        public bool HasStencil { get; }
        public uint StencilReference { get; }
        public RgbaFloat BlendColor { get; }
        public override bool IsDisposed => _disposed;
        public override string Name { get; set; }

        private static readonly Dictionary<RenderPipelineStateLookup, MTLRenderPipelineState> render_pipeline_states = new Dictionary<RenderPipelineStateLookup, MTLRenderPipelineState>();
        private static readonly Dictionary<ComputePipelineStateLookup, MTLComputePipelineState> compute_pipeline_states = new Dictionary<ComputePipelineStateLookup, MTLComputePipelineState>();
        private static readonly Dictionary<DepthStencilStateDescription, MTLDepthStencilState> depth_stencil_states = new Dictionary<DepthStencilStateDescription, MTLDepthStencilState>();
        private bool _disposed;
        private List<MTLFunction> _specializedFunctions;

        public MTLPipeline(ref GraphicsPipelineDescription description, MTLGraphicsDevice gd)
            : base(ref description)
        {
            PrimitiveType = MTLFormats.VdToMTLPrimitiveTopology(description.PrimitiveTopology);
            ResourceLayouts = new MTLResourceLayout[description.ResourceLayouts.Length];
            NonVertexBufferCount = 0;

            for (int i = 0; i < ResourceLayouts.Length; i++)
            {
                ResourceLayouts[i] = Util.AssertSubtype<ResourceLayout, MTLResourceLayout>(description.ResourceLayouts[i]);
                NonVertexBufferCount += ResourceLayouts[i].BufferCount;
            }

            ResourceBindingModel = description.ResourceBindingModel ?? gd.ResourceBindingModel;

            CullMode = MTLFormats.VdToMTLCullMode(description.RasterizerState.CullMode);
            FrontFace = MTLFormats.VdVoMTLFrontFace(description.RasterizerState.FrontFace);
            FillMode = MTLFormats.VdToMTLFillMode(description.RasterizerState.FillMode);
            ScissorTestEnabled = description.RasterizerState.ScissorTestEnabled;

            var stateLookup = new RenderPipelineStateLookup { Shaders = description.ShaderSet, BlendState = description.BlendState, Outputs = description.Outputs };

            if (!render_pipeline_states.TryGetValue(stateLookup, out var renderPipelineState))
            {
                var mtlDesc = MTLRenderPipelineDescriptor.New();

                foreach (var shader in description.ShaderSet.Shaders)
                {
                    var mtlShader = Util.AssertSubtype<Shader, MTLShader>(shader);
                    MTLFunction specializedFunction;

                    if (mtlShader.HasFunctionConstants)
                    {
                        // Need to create specialized MTLFunction.
                        var constantValues = CreateConstantValues(description.ShaderSet.Specializations);
                        specializedFunction = mtlShader.Library.newFunctionWithNameConstantValues(mtlShader.EntryPoint, constantValues);
                        AddSpecializedFunction(specializedFunction);
                        ObjectiveCRuntime.release(constantValues.NativePtr);

                        Debug.Assert(specializedFunction.NativePtr != IntPtr.Zero, "Failed to create specialized MTLFunction");
                    }
                    else
                        specializedFunction = mtlShader.Function;

                    if (shader.Stage == ShaderStages.Vertex)
                        mtlDesc.vertexFunction = specializedFunction;
                    else if (shader.Stage == ShaderStages.Fragment) mtlDesc.fragmentFunction = specializedFunction;
                }

                // Vertex layouts
                var vdVertexLayouts = description.ShaderSet.VertexLayouts;
                var vertexDescriptor = mtlDesc.vertexDescriptor;

                for (uint i = 0; i < vdVertexLayouts.Length; i++)
                {
                    uint layoutIndex = ResourceBindingModel == ResourceBindingModel.Improved
                        ? NonVertexBufferCount + i
                        : i;
                    var mtlLayout = vertexDescriptor.layouts[layoutIndex];
                    mtlLayout.stride = vdVertexLayouts[i].Stride;
                    uint stepRate = vdVertexLayouts[i].InstanceStepRate;
                    mtlLayout.stepFunction = stepRate == 0 ? MTLVertexStepFunction.PerVertex : MTLVertexStepFunction.PerInstance;
                    mtlLayout.stepRate = Math.Max(1, stepRate);
                }

                uint element = 0;

                for (uint i = 0; i < vdVertexLayouts.Length; i++)
                {
                    uint offset = 0;
                    var vdDesc = vdVertexLayouts[i];

                    for (uint j = 0; j < vdDesc.Elements.Length; j++)
                    {
                        var elementDesc = vdDesc.Elements[j];
                        var mtlAttribute = vertexDescriptor.attributes[element];
                        mtlAttribute.bufferIndex = ResourceBindingModel == ResourceBindingModel.Improved
                            ? NonVertexBufferCount + i
                            : i;
                        mtlAttribute.format = MTLFormats.VdToMTLVertexFormat(elementDesc.Format);
                        mtlAttribute.offset = elementDesc.Offset != 0 ? elementDesc.Offset : (UIntPtr)offset;
                        offset += FormatSizeHelpers.GetSizeInBytes(elementDesc.Format);
                        element += 1;
                    }
                }

                VertexBufferCount = (uint)vdVertexLayouts.Length;

                // Outputs
                var outputs = description.Outputs;
                var blendStateDesc = description.BlendState;
                BlendColor = blendStateDesc.BlendFactor;

                if (outputs.SampleCount != TextureSampleCount.Count1) mtlDesc.sampleCount = FormatHelpers.GetSampleCountUInt32(outputs.SampleCount);

                if (outputs.DepthAttachment != null)
                {
                    var depthFormat = outputs.DepthAttachment.Value.Format;
                    var mtlDepthFormat = MTLFormats.VdToMTLPixelFormat(depthFormat, true);
                    mtlDesc.depthAttachmentPixelFormat = mtlDepthFormat;

                    if (FormatHelpers.IsStencilFormat(depthFormat))
                    {
                        HasStencil = true;
                        mtlDesc.stencilAttachmentPixelFormat = mtlDepthFormat;
                    }
                }

                for (uint i = 0; i < outputs.ColorAttachments.Length; i++)
                {
                    var attachmentBlendDesc = blendStateDesc.AttachmentStates[i];
                    var colorDesc = mtlDesc.colorAttachments[i];
                    colorDesc.pixelFormat = MTLFormats.VdToMTLPixelFormat(outputs.ColorAttachments[i].Format, false);
                    colorDesc.blendingEnabled = attachmentBlendDesc.BlendEnabled;
                    colorDesc.writeMask = MTLFormats.VdToMTLColorWriteMask(attachmentBlendDesc.ColorWriteMask.GetOrDefault());
                    colorDesc.alphaBlendOperation = MTLFormats.VdToMTLBlendOp(attachmentBlendDesc.AlphaFunction);
                    colorDesc.sourceAlphaBlendFactor = MTLFormats.VdToMTLBlendFactor(attachmentBlendDesc.SourceAlphaFactor);
                    colorDesc.destinationAlphaBlendFactor = MTLFormats.VdToMTLBlendFactor(attachmentBlendDesc.DestinationAlphaFactor);

                    colorDesc.rgbBlendOperation = MTLFormats.VdToMTLBlendOp(attachmentBlendDesc.ColorFunction);
                    colorDesc.sourceRGBBlendFactor = MTLFormats.VdToMTLBlendFactor(attachmentBlendDesc.SourceColorFactor);
                    colorDesc.destinationRGBBlendFactor = MTLFormats.VdToMTLBlendFactor(attachmentBlendDesc.DestinationColorFactor);
                }

                mtlDesc.alphaToCoverageEnabled = blendStateDesc.AlphaToCoverageEnabled;

                renderPipelineState = gd.Device.newRenderPipelineStateWithDescriptor(mtlDesc);
                ObjectiveCRuntime.release(mtlDesc.NativePtr);
            }

            RenderPipelineState = renderPipelineState;

            if (description.Outputs.DepthAttachment != null)
            {
                if (!depth_stencil_states.TryGetValue(description.DepthStencilState, out var depthStencilState))
                {
                    var depthDescriptor = MTLUtil.AllocInit<MTLDepthStencilDescriptor>(
                        nameof(MTLDepthStencilDescriptor));
                    depthDescriptor.depthCompareFunction = MTLFormats.VdToMTLCompareFunction(
                        description.DepthStencilState.DepthComparison);
                    depthDescriptor.depthWriteEnabled = description.DepthStencilState.DepthWriteEnabled;

                    bool stencilEnabled = description.DepthStencilState.StencilTestEnabled;

                    if (stencilEnabled)
                    {
                        StencilReference = description.DepthStencilState.StencilReference;

                        var vdFrontDesc = description.DepthStencilState.StencilFront;
                        var front = MTLUtil.AllocInit<MTLStencilDescriptor>(nameof(MTLStencilDescriptor));
                        front.readMask = stencilEnabled ? description.DepthStencilState.StencilReadMask : 0u;
                        front.writeMask = stencilEnabled ? description.DepthStencilState.StencilWriteMask : 0u;
                        front.depthFailureOperation = MTLFormats.VdToMTLStencilOperation(vdFrontDesc.DepthFail);
                        front.stencilFailureOperation = MTLFormats.VdToMTLStencilOperation(vdFrontDesc.Fail);
                        front.depthStencilPassOperation = MTLFormats.VdToMTLStencilOperation(vdFrontDesc.Pass);
                        front.stencilCompareFunction = MTLFormats.VdToMTLCompareFunction(vdFrontDesc.Comparison);
                        depthDescriptor.frontFaceStencil = front;

                        var vdBackDesc = description.DepthStencilState.StencilBack;
                        var back = MTLUtil.AllocInit<MTLStencilDescriptor>(nameof(MTLStencilDescriptor));
                        back.readMask = stencilEnabled ? description.DepthStencilState.StencilReadMask : 0u;
                        back.writeMask = stencilEnabled ? description.DepthStencilState.StencilWriteMask : 0u;
                        back.depthFailureOperation = MTLFormats.VdToMTLStencilOperation(vdBackDesc.DepthFail);
                        back.stencilFailureOperation = MTLFormats.VdToMTLStencilOperation(vdBackDesc.Fail);
                        back.depthStencilPassOperation = MTLFormats.VdToMTLStencilOperation(vdBackDesc.Pass);
                        back.stencilCompareFunction = MTLFormats.VdToMTLCompareFunction(vdBackDesc.Comparison);
                        depthDescriptor.backFaceStencil = back;

                        ObjectiveCRuntime.release(front.NativePtr);
                        ObjectiveCRuntime.release(back.NativePtr);
                    }

                    depthStencilState = gd.Device.newDepthStencilStateWithDescriptor(depthDescriptor);
                    ObjectiveCRuntime.release(depthDescriptor.NativePtr);
                }

                DepthStencilState = depthStencilState;
            }

            DepthClipMode = description.DepthStencilState.DepthTestEnabled ? MTLDepthClipMode.Clip : MTLDepthClipMode.Clamp;
        }

        public MTLPipeline(ref ComputePipelineDescription description, MTLGraphicsDevice gd)
            : base(ref description)
        {
            IsComputePipeline = true;
            ResourceLayouts = new MTLResourceLayout[description.ResourceLayouts.Length];

            for (int i = 0; i < ResourceLayouts.Length; i++) ResourceLayouts[i] = Util.AssertSubtype<ResourceLayout, MTLResourceLayout>(description.ResourceLayouts[i]);

            ThreadsPerThreadgroup = new MTLSize(
                description.ThreadGroupSizeX,
                description.ThreadGroupSizeY,
                description.ThreadGroupSizeZ);

            var stateLookup = new ComputePipelineStateLookup
                { ComputeShader = description.ComputeShader, ResourceLayouts = description.ResourceLayouts, Specializations = description.Specializations };

            if (!compute_pipeline_states.TryGetValue(stateLookup, out var computePipelineState))
            {
                var mtlDesc = MTLUtil.AllocInit<MTLComputePipelineDescriptor>(
                    nameof(MTLComputePipelineDescriptor));
                var mtlShader = Util.AssertSubtype<Shader, MTLShader>(description.ComputeShader);
                MTLFunction specializedFunction;

                if (mtlShader.HasFunctionConstants)
                {
                    // Need to create specialized MTLFunction.
                    var constantValues = CreateConstantValues(description.Specializations);
                    specializedFunction = mtlShader.Library.newFunctionWithNameConstantValues(mtlShader.EntryPoint, constantValues);
                    AddSpecializedFunction(specializedFunction);
                    ObjectiveCRuntime.release(constantValues.NativePtr);

                    Debug.Assert(specializedFunction.NativePtr != IntPtr.Zero, "Failed to create specialized MTLFunction");
                }
                else
                    specializedFunction = mtlShader.Function;

                mtlDesc.computeFunction = specializedFunction;
                var buffers = mtlDesc.buffers;
                uint bufferIndex = 0;

                foreach (var layout in ResourceLayouts)
                {
                    foreach (var rle in layout.Description.Elements)
                    {
                        var kind = rle.Kind;

                        if (kind == ResourceKind.UniformBuffer
                            || kind == ResourceKind.StructuredBufferReadOnly)
                        {
                            var bufferDesc = buffers[bufferIndex];
                            bufferDesc.mutability = MTLMutability.Immutable;
                            bufferIndex += 1;
                        }
                        else if (kind == ResourceKind.StructuredBufferReadWrite)
                        {
                            var bufferDesc = buffers[bufferIndex];
                            bufferDesc.mutability = MTLMutability.Mutable;
                            bufferIndex += 1;
                        }
                    }
                }

                computePipelineState = gd.Device.newComputePipelineStateWithDescriptor(mtlDesc);
                ObjectiveCRuntime.release(mtlDesc.NativePtr);
            }

            ComputePipelineState = computePipelineState;
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposed)
            {
                if (RenderPipelineState.NativePtr != IntPtr.Zero)
                {
                    render_pipeline_states.Remove(render_pipeline_states.Single(kvp => kvp.Value.NativePtr == RenderPipelineState.NativePtr).Key);
                    ObjectiveCRuntime.release(RenderPipelineState.NativePtr);
                }

                if (DepthStencilState.NativePtr != IntPtr.Zero)
                {
                    depth_stencil_states.Remove(depth_stencil_states.Single(kvp => kvp.Value.NativePtr == DepthStencilState.NativePtr).Key);
                    ObjectiveCRuntime.release(DepthStencilState.NativePtr);
                }

                if (ComputePipelineState.NativePtr != IntPtr.Zero)
                {
                    compute_pipeline_states.Remove(compute_pipeline_states.Single(kvp => kvp.Value.NativePtr == ComputePipelineState.NativePtr).Key);
                    ObjectiveCRuntime.release(ComputePipelineState.NativePtr);
                }

                if (_specializedFunctions != null)
                {
                    foreach (var function in _specializedFunctions) ObjectiveCRuntime.release(function.NativePtr);

                    _specializedFunctions.Clear();
                }

                _disposed = true;
            }
        }

        #endregion

        private unsafe MTLFunctionConstantValues CreateConstantValues(SpecializationConstant[] specializations)
        {
            var ret = MTLFunctionConstantValues.New();

            if (specializations != null)
            {
                foreach (var sc in specializations)
                {
                    var mtlType = MTLFormats.VdVoMTLShaderConstantType(sc.Type);
                    ret.setConstantValuetypeatIndex(&sc.Data, mtlType, sc.ID);
                }
            }

            return ret;
        }

        private void AddSpecializedFunction(MTLFunction function)
        {
            if (_specializedFunctions == null) _specializedFunctions = new List<MTLFunction>();

            _specializedFunctions.Add(function);
        }

        private struct RenderPipelineStateLookup : IEquatable<RenderPipelineStateLookup>
        {
            public ShaderSetDescription Shaders;
            public OutputDescription Outputs;
            public BlendStateDescription BlendState;

            public bool Equals(RenderPipelineStateLookup other)
            {
                return Shaders.Equals(other.Shaders) &&
                       Outputs.Equals(other.Outputs) &&
                       BlendState.Equals(other.BlendState);
            }

            public override bool Equals(object obj)
            {
                return obj is RenderPipelineStateLookup other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Shaders, Outputs, BlendState);
            }
        }

        private struct ComputePipelineStateLookup : IEquatable<ComputePipelineStateLookup>
        {
            public Shader ComputeShader;
            public ResourceLayout[] ResourceLayouts;
            public SpecializationConstant[] Specializations;

            public bool Equals(ComputePipelineStateLookup other)
            {
                return ComputeShader == other.ComputeShader &&
                       Util.ArrayEquals(ResourceLayouts, other.ResourceLayouts) &&
                       Util.ArrayEqualsEquatable(Specializations, other.Specializations);
            }

            public override bool Equals(object obj)
            {
                return obj is ComputePipelineStateLookup other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ComputeShader, HashHelper.Array(ResourceLayouts), HashHelper.Array(Specializations));
            }
        }
    }
}
