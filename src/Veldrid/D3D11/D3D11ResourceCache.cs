using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Vortice.Direct3D11;

namespace Veldrid.D3D11
{
    internal class D3D11ResourceCache : IDisposable
    {
        private readonly ID3D11Device device;
        private readonly object @lock = new object();

        private readonly Dictionary<BlendStateDescription, ID3D11BlendState> blendStates
            = new Dictionary<BlendStateDescription, ID3D11BlendState>();

        private readonly Dictionary<DepthStencilStateDescription, ID3D11DepthStencilState> depthStencilStates
            = new Dictionary<DepthStencilStateDescription, ID3D11DepthStencilState>();

        private readonly Dictionary<D3D11RasterizerStateCacheKey, ID3D11RasterizerState> rasterizerStates
            = new Dictionary<D3D11RasterizerStateCacheKey, ID3D11RasterizerState>();

        private readonly Dictionary<InputLayoutCacheKey, ID3D11InputLayout> inputLayouts
            = new Dictionary<InputLayoutCacheKey, ID3D11InputLayout>();

        public D3D11ResourceCache(ID3D11Device device)
        {
            this.device = device;
        }

        #region Disposal

        public void Dispose()
        {
            foreach (var kvp in blendStates) kvp.Value.Dispose();

            foreach (var kvp in depthStencilStates) kvp.Value.Dispose();

            foreach (var kvp in rasterizerStates) kvp.Value.Dispose();

            foreach (var kvp in inputLayouts) kvp.Value.Dispose();
        }

        #endregion

        public void GetPipelineResources(
            ref BlendStateDescription blendDesc,
            ref DepthStencilStateDescription dssDesc,
            ref RasterizerStateDescription rasterDesc,
            bool multisample,
            VertexLayoutDescription[] vertexLayouts,
            byte[] vsBytecode,
            out ID3D11BlendState blendState,
            out ID3D11DepthStencilState depthState,
            out ID3D11RasterizerState rasterState,
            out ID3D11InputLayout inputLayout)
        {
            lock (@lock)
            {
                blendState = getBlendState(ref blendDesc);
                depthState = getDepthStencilState(ref dssDesc);
                rasterState = getRasterizerState(ref rasterDesc, multisample);
                inputLayout = getInputLayout(vertexLayouts, vsBytecode);
            }
        }

        private ID3D11BlendState getBlendState(ref BlendStateDescription description)
        {
            Debug.Assert(Monitor.IsEntered(@lock));

            if (!blendStates.TryGetValue(description, out var blendState))
            {
                blendState = createNewBlendState(ref description);
                var key = description;
                key.AttachmentStates = (BlendAttachmentDescription[])key.AttachmentStates.Clone();
                blendStates.Add(key, blendState);
            }

            return blendState;
        }

        private ID3D11BlendState createNewBlendState(ref BlendStateDescription description)
        {
            var attachmentStates = description.AttachmentStates;
            var d3dBlendStateDesc = new BlendDescription();

            for (int i = 0; i < attachmentStates.Length; i++)
            {
                var state = attachmentStates[i];
                d3dBlendStateDesc.RenderTarget[i].BlendEnable = state.BlendEnabled;
                d3dBlendStateDesc.RenderTarget[i].RenderTargetWriteMask = D3D11Formats.VdToD3D11ColorWriteEnable(state.ColorWriteMask.GetOrDefault());
                d3dBlendStateDesc.RenderTarget[i].SourceBlend = D3D11Formats.VdToD3D11Blend(state.SourceColorFactor);
                d3dBlendStateDesc.RenderTarget[i].DestinationBlend = D3D11Formats.VdToD3D11Blend(state.DestinationColorFactor);
                d3dBlendStateDesc.RenderTarget[i].BlendOperation = D3D11Formats.VdToD3D11BlendOperation(state.ColorFunction);
                d3dBlendStateDesc.RenderTarget[i].SourceBlendAlpha = D3D11Formats.VdToD3D11Blend(state.SourceAlphaFactor);
                d3dBlendStateDesc.RenderTarget[i].DestinationBlendAlpha = D3D11Formats.VdToD3D11Blend(state.DestinationAlphaFactor);
                d3dBlendStateDesc.RenderTarget[i].BlendOperationAlpha = D3D11Formats.VdToD3D11BlendOperation(state.AlphaFunction);
            }

            d3dBlendStateDesc.AlphaToCoverageEnable = description.AlphaToCoverageEnabled;
            d3dBlendStateDesc.IndependentBlendEnable = true;

            return device.CreateBlendState(d3dBlendStateDesc);
        }

        private ID3D11DepthStencilState getDepthStencilState(ref DepthStencilStateDescription description)
        {
            Debug.Assert(Monitor.IsEntered(@lock));

            if (!depthStencilStates.TryGetValue(description, out var dss))
            {
                dss = createNewDepthStencilState(ref description);
                var key = description;
                depthStencilStates.Add(key, dss);
            }

            return dss;
        }

        private ID3D11DepthStencilState createNewDepthStencilState(ref DepthStencilStateDescription description)
        {
            var dssDesc = new DepthStencilDescription
            {
                DepthFunc = D3D11Formats.VdToD3D11ComparisonFunc(description.DepthComparison),
                DepthEnable = description.DepthTestEnabled,
                DepthWriteMask = description.DepthWriteEnabled ? DepthWriteMask.All : DepthWriteMask.Zero,
                StencilEnable = description.StencilTestEnabled,
                FrontFace = toD3D11StencilOpDesc(description.StencilFront),
                BackFace = toD3D11StencilOpDesc(description.StencilBack),
                StencilReadMask = description.StencilReadMask,
                StencilWriteMask = description.StencilWriteMask
            };

            return device.CreateDepthStencilState(dssDesc);
        }

        private DepthStencilOperationDescription toD3D11StencilOpDesc(StencilBehaviorDescription sbd)
        {
            return new DepthStencilOperationDescription
            {
                StencilFunc = D3D11Formats.VdToD3D11ComparisonFunc(sbd.Comparison),
                StencilPassOp = D3D11Formats.VdToD3D11StencilOperation(sbd.Pass),
                StencilFailOp = D3D11Formats.VdToD3D11StencilOperation(sbd.Fail),
                StencilDepthFailOp = D3D11Formats.VdToD3D11StencilOperation(sbd.DepthFail)
            };
        }

        private ID3D11RasterizerState getRasterizerState(ref RasterizerStateDescription description, bool multisample)
        {
            Debug.Assert(Monitor.IsEntered(@lock));
            var key = new D3D11RasterizerStateCacheKey(description, multisample);

            if (!rasterizerStates.TryGetValue(key, out var rasterizerState))
            {
                rasterizerState = createNewRasterizerState(ref key);
                rasterizerStates.Add(key, rasterizerState);
            }

            return rasterizerState;
        }

        private ID3D11RasterizerState createNewRasterizerState(ref D3D11RasterizerStateCacheKey key)
        {
            var rssDesc = new RasterizerDescription
            {
                CullMode = D3D11Formats.VdToD3D11CullMode(key.VeldridDescription.CullMode),
                FillMode = D3D11Formats.VdToD3D11FillMode(key.VeldridDescription.FillMode),
                DepthClipEnable = key.VeldridDescription.DepthClipEnabled,
                ScissorEnable = key.VeldridDescription.ScissorTestEnabled,
                FrontCounterClockwise = key.VeldridDescription.FrontFace == FrontFace.CounterClockwise,
                MultisampleEnable = key.Multisampled
            };

            return device.CreateRasterizerState(rssDesc);
        }

        private ID3D11InputLayout getInputLayout(VertexLayoutDescription[] vertexLayouts, byte[] vsBytecode)
        {
            Debug.Assert(Monitor.IsEntered(@lock));

            if (vsBytecode == null || vertexLayouts == null || vertexLayouts.Length == 0) return null;

            var tempKey = InputLayoutCacheKey.CreateTempKey(vertexLayouts);

            if (!inputLayouts.TryGetValue(tempKey, out var inputLayout))
            {
                inputLayout = createNewInputLayout(vertexLayouts, vsBytecode);
                var permanentKey = InputLayoutCacheKey.CreatePermanentKey(vertexLayouts);
                inputLayouts.Add(permanentKey, inputLayout);
            }

            return inputLayout;
        }

        private ID3D11InputLayout createNewInputLayout(VertexLayoutDescription[] vertexLayouts, byte[] vsBytecode)
        {
            int totalCount = 0;
            for (int i = 0; i < vertexLayouts.Length; i++) totalCount += vertexLayouts[i].Elements.Length;

            int element = 0; // Total element index across slots.
            var elements = new InputElementDescription[totalCount];
            var si = new SemanticIndices();

            for (int slot = 0; slot < vertexLayouts.Length; slot++)
            {
                var elementDescs = vertexLayouts[slot].Elements;
                uint stepRate = vertexLayouts[slot].InstanceStepRate;
                int currentOffset = 0;

                for (int i = 0; i < elementDescs.Length; i++)
                {
                    var desc = elementDescs[i];
                    elements[element] = new InputElementDescription(
                        getSemanticString(desc.Semantic),
                        SemanticIndices.GetAndIncrement(ref si, desc.Semantic),
                        D3D11Formats.ToDxgiFormat(desc.Format),
                        desc.Offset != 0 ? (int)desc.Offset : currentOffset,
                        slot,
                        stepRate == 0 ? InputClassification.PerVertexData : InputClassification.PerInstanceData,
                        (int)stepRate);

                    currentOffset += (int)FormatSizeHelpers.GetSizeInBytes(desc.Format);
                    element += 1;
                }
            }

            return device.CreateInputLayout(elements, vsBytecode);
        }

        private string getSemanticString(VertexElementSemantic semantic)
        {
            switch (semantic)
            {
                case VertexElementSemantic.Position:
                    return "POSITION";

                case VertexElementSemantic.Normal:
                    return "NORMAL";

                case VertexElementSemantic.TextureCoordinate:
                    return "TEXCOORD";

                case VertexElementSemantic.Color:
                    return "COLOR";

                default:
                    throw Illegal.Value<VertexElementSemantic>();
            }
        }

        private struct SemanticIndices
        {
            private int position;
            private int texCoord;
            private int normal;
            private int color;

            public static int GetAndIncrement(ref SemanticIndices si, VertexElementSemantic type)
            {
                switch (type)
                {
                    case VertexElementSemantic.Position:
                        return si.position++;

                    case VertexElementSemantic.TextureCoordinate:
                        return si.texCoord++;

                    case VertexElementSemantic.Normal:
                        return si.normal++;

                    case VertexElementSemantic.Color:
                        return si.color++;

                    default:
                        throw Illegal.Value<VertexElementSemantic>();
                }
            }
        }

        private struct InputLayoutCacheKey : IEquatable<InputLayoutCacheKey>
        {
            public VertexLayoutDescription[] VertexLayouts;

            public static InputLayoutCacheKey CreateTempKey(VertexLayoutDescription[] original)
            {
                return new InputLayoutCacheKey { VertexLayouts = original };
            }

            public static InputLayoutCacheKey CreatePermanentKey(VertexLayoutDescription[] original)
            {
                var vertexLayouts = new VertexLayoutDescription[original.Length];

                for (int i = 0; i < original.Length; i++)
                {
                    vertexLayouts[i].Stride = original[i].Stride;
                    vertexLayouts[i].InstanceStepRate = original[i].InstanceStepRate;
                    vertexLayouts[i].Elements = (VertexElementDescription[])original[i].Elements.Clone();
                }

                return new InputLayoutCacheKey { VertexLayouts = vertexLayouts };
            }

            public bool Equals(InputLayoutCacheKey other)
            {
                return Util.ArrayEqualsEquatable(VertexLayouts, other.VertexLayouts);
            }

            public override int GetHashCode()
            {
                return HashHelper.Array(VertexLayouts);
            }
        }

        private struct D3D11RasterizerStateCacheKey : IEquatable<D3D11RasterizerStateCacheKey>
        {
            public RasterizerStateDescription VeldridDescription;
            public readonly bool Multisampled;

            public D3D11RasterizerStateCacheKey(RasterizerStateDescription veldridDescription, bool multisampled)
            {
                VeldridDescription = veldridDescription;
                Multisampled = multisampled;
            }

            public bool Equals(D3D11RasterizerStateCacheKey other)
            {
                return VeldridDescription.Equals(other.VeldridDescription)
                       && Multisampled.Equals(other.Multisampled);
            }

            public override int GetHashCode()
            {
                return HashHelper.Combine(VeldridDescription.GetHashCode(), Multisampled.GetHashCode());
            }
        }
    }
}
