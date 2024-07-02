using Xunit;

namespace Veldrid.Tests
{
    public abstract class DisposalTestBase<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
    {
        [Fact]
        public void DisposeBuffer()
        {
            DeviceBuffer b = RF.CreateBuffer(new BufferDescription(256, BufferUsage.VertexBuffer));
            b.Dispose();
            Assert.True(b.IsDisposed);
        }

        [Fact]
        public void DisposeTexture()
        {
            Texture t = RF.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32G32B32A32Float, TextureUsage.Sampled));
            TextureView tv = RF.CreateTextureView(t);
            GD.WaitForIdle(); // Required currently by Vulkan backend.
            tv.Dispose();
            Assert.True(tv.IsDisposed);
            Assert.False(t.IsDisposed);
            t.Dispose();
            Assert.True(t.IsDisposed);
        }

        [Fact]
        public void DisposeFramebuffer()
        {
            Texture t = RF.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32G32B32A32Float, TextureUsage.RenderTarget));
            Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, t));
            GD.WaitForIdle(); // Required currently by Vulkan backend.
            fb.Dispose();
            Assert.True(fb.IsDisposed);
            Assert.False(t.IsDisposed);
            t.Dispose();
            Assert.True(t.IsDisposed);
        }

        [Fact]
        public void DisposeCommandList()
        {
            CommandList cl = RF.CreateCommandList();
            cl.Dispose();
            Assert.True(cl.IsDisposed);
        }

        [Fact]
        public void DisposeSampler()
        {
            Sampler s = RF.CreateSampler(SamplerDescription.POINT);
            s.Dispose();
            Assert.True(s.IsDisposed);
        }

        [Fact]
        public void DisposePipeline()
        {
            Shader[] shaders = TestShaders.LoadVertexFragment(RF, "UIntVertexAttribs");
            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("ColorUInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                shaders);

            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SINGLE_OVERRIDE_BLEND,
                DepthStencilStateDescription.DISABLED,
                RasterizerStateDescription.DEFAULT,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.R32G32B32A32Float)));
            Pipeline pipeline = RF.CreateGraphicsPipeline(ref gpd);
            pipeline.Dispose();
            Assert.True(pipeline.IsDisposed);
            Assert.False(shaders[0].IsDisposed);
            Assert.False(shaders[1].IsDisposed);
            Assert.False(layout.IsDisposed);
            layout.Dispose();
            Assert.True(layout.IsDisposed);
            Assert.False(shaders[0].IsDisposed);
            Assert.False(shaders[1].IsDisposed);
            shaders[0].Dispose();
            Assert.True(shaders[0].IsDisposed);
            shaders[1].Dispose();
            Assert.True(shaders[1].IsDisposed);
        }

        [Fact]
        public void DisposeResourceSet()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer ub0 = RF.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));
            DeviceBuffer ub1 = RF.CreateBuffer(new BufferDescription(256, BufferUsage.UniformBuffer));

            ResourceSet rs = RF.CreateResourceSet(new ResourceSetDescription(layout, ub0, ub1));
            rs.Dispose();
            Assert.True(rs.IsDisposed);
            Assert.False(ub0.IsDisposed);
            Assert.False(ub1.IsDisposed);
            Assert.False(layout.IsDisposed);
            layout.Dispose();
            Assert.True(layout.IsDisposed);
            Assert.False(ub0.IsDisposed);
            Assert.False(ub1.IsDisposed);
            ub0.Dispose();
            Assert.True(ub0.IsDisposed);
            ub1.Dispose();
            Assert.True(ub1.IsDisposed);
        }
    }

#if TESTVULKAN
    [Trait("Backend", "Vulkan")]
    public class VulkanDisposalTests : DisposalTestBase<VulkanDeviceCreator> { }
#endif
#if TESTD3D11
    [Trait("Backend", "D3D11")]
    public class D3D11DisposalTests : DisposalTestBase<D3D11DeviceCreator> { }
#endif
#if TESTMETAL
    [Trait("Backend", "Metal")]
    public class MetalDisposalTests : DisposalTestBase<MetalDeviceCreator> { }
#endif
#if TESTOPENGL
    [Trait("Backend", "OpenGL")]
    public class OpenGLDisposalTests : DisposalTestBase<OpenGLDeviceCreator> { }
#endif
#if TESTOPENGLES
    [Trait("Backend", "OpenGLES")]
    public class OpenGLESDisposalTests : DisposalTestBase<OpenGLESDeviceCreator> { }
#endif
}
