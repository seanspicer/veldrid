using System;
using Xunit;

namespace Veldrid.Tests
{

    public abstract class VertexLayoutTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
    {
        [Theory]
        [InlineData(0, 0, 0, 0, -1, true)]
        [InlineData(0, 12, 28, 36, -1, true)]
        [InlineData(0, 16, 32, 48, -1, true)]
        [InlineData(0, 16, 32, 48, 64, true)]
        [InlineData(0, 16, 32, 48, 128, true)]
        [InlineData(0, 16, 32, 48, 49, false)]
        [InlineData(0, 12, 12, 12, -1, false)]
        [InlineData(0, 12, 0, 36, -1, false)]
        [InlineData(0, 12, 28, 35, -1, false)]
        public void ExplicitOffsets(uint firstOffset, uint secondOffset, uint thirdOffset, uint fourthOffset, int stride, bool succeeds)
        {
            Texture outTex = RF.CreateTexture(
                TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32G32B32A32Float, TextureUsage.RenderTarget));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, outTex));

            VertexLayoutDescription vertexLayoutDesc = new VertexLayoutDescription(
                new VertexElementDescription("AV3", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3, firstOffset),
                new VertexElementDescription("BV4", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, secondOffset),
                new VertexElementDescription("CV2", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2, thirdOffset),
                new VertexElementDescription("DV4", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4, fourthOffset));

            if (stride > 0)
            {
                vertexLayoutDesc.Stride = (uint)stride;
            }

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    vertexLayoutDesc
                },
                TestShaders.LoadVertexFragment(RF, "VertexLayoutTestShader"));

            try
            {
                RF.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    BlendStateDescription.SINGLE_OVERRIDE_BLEND,
                    DepthStencilStateDescription.DISABLED,
                    RasterizerStateDescription.DEFAULT,
                    PrimitiveTopology.TriangleList,
                    shaderSet,
                    Array.Empty<ResourceLayout>(),
                    fb.OutputDescription));
            }
            catch when (!succeeds) { }
        }
    }

#if TESTOPENGL
    [Trait("Backend", "OpenGL")]
    public class OpenGLVertexLayoutTests : VertexLayoutTests<OpenGLDeviceCreator> { }
#endif
#if TESTOPENGLES
    [Trait("Backend", "OpenGLES")]
    public class OpenGLESVertexLayoutTests : VertexLayoutTests<OpenGLESDeviceCreator> { }
#endif
#if TESTVULKAN
    [Trait("Backend", "Vulkan")]
    public class VulkanVertexLayoutTests : VertexLayoutTests<VulkanDeviceCreatorWithMainSwapchain> { }
#endif
#if TESTD3D11
    [Trait("Backend", "D3D11")]
    public class D3D11VertexLayoutTests : VertexLayoutTests<D3D11DeviceCreator> { }
#endif
#if TESTMETAL
    [Trait("Backend", "Metal")]
    public class MetalVertexLayoutTests : RenderTests<MetalDeviceCreator> { }
#endif
}
