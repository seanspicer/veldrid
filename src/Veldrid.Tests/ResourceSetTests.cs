
using Xunit;

namespace Veldrid.Tests
{
    public abstract class ResourceSetTests<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
    {
        [Fact]
        public void ResourceSetBufferInsteadOfTextureViewFails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TV0", ResourceKind.TextureReadOnly, ShaderStages.Vertex)));

            DeviceBuffer ub = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            Assert.Throws<VeldridException>(() =>
            {
                ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout,
                    ub));
            });
        }

        [Fact]
        public void ResourceSetIncorrectTextureUsageFails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("TV0", ResourceKind.TextureReadWrite, ShaderStages.Vertex)));

            Texture t = RF.CreateTexture(TextureDescription.Texture2D(64, 64, 1, 1, PixelFormat.R8G8B8A8UNorm, TextureUsage.Sampled));
            TextureView tv = RF.CreateTextureView(t);

            Assert.Throws<VeldridException>(() =>
            {
                ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, tv));
            });
        }

        [Fact]
        public void ResourceSetIncorrectBufferUsageFails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("RWB0", ResourceKind.StructuredBufferReadWrite, ShaderStages.Vertex)));

            DeviceBuffer readOnlyBuffer = RF.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));

            Assert.Throws<VeldridException>(() =>
            {
                ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, readOnlyBuffer));
            });
        }

        [Fact]
        public void ResourceSetTooFewOrTooManyElementsFails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UB1", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("UB2", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer ub = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            Assert.Throws<VeldridException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub));
            });

            Assert.Throws<VeldridException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub, ub));
            });

            Assert.Throws<VeldridException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub, ub, ub, ub));
            });

            Assert.Throws<VeldridException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout, ub, ub, ub, ub, ub));
            });
        }

        [Fact]
        public void ResourceSetInvalidUniformOffsetFails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));

            Assert.Throws<VeldridException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout,
                    new DeviceBufferRange(buffer, GD.UniformBufferMinOffsetAlignment - 1, 256)));
            });

            Assert.Throws<VeldridException>(() =>
            {
                RF.CreateResourceSet(new ResourceSetDescription(layout,
                    new DeviceBufferRange(buffer, GD.UniformBufferMinOffsetAlignment + 1, 256)));
            });
        }

        [Fact]
        public void ResourceSetNoPipelineBoundFails()
        {
            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("UB0", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
            DeviceBuffer ub = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));


            ResourceSet rs = RF.CreateResourceSet(new ResourceSetDescription(layout, ub));

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            Assert.Throws<VeldridException>(() => cl.SetGraphicsResourceSet(0, rs));
            cl.End();
        }

        [Fact]
        public void ResourceSetInvalidSlotFails()
        {
            DeviceBuffer infoBuffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("ColorUInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(RF, "UIntVertexAttribs"));

            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SINGLE_OVERRIDE_BLEND,
                DepthStencilStateDescription.DISABLED,
                RasterizerStateDescription.DEFAULT,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.B8G8R8A8UNorm)));

            Pipeline pipeline = RF.CreateGraphicsPipeline(ref gpd);

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(pipeline);
            Assert.Throws<VeldridException>(() => cl.SetGraphicsResourceSet(1, set));
            Assert.Throws<VeldridException>(() => cl.SetGraphicsResourceSet(2, set));
            Assert.Throws<VeldridException>(() => cl.SetGraphicsResourceSet(3, set));
            cl.End();
        }

        [Fact]
        public void ResourceSetIncompatibleSetFails()
        {
            DeviceBuffer infoBuffer = RF.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
            DeviceBuffer orthoBuffer = RF.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            ShaderSetDescription shaderSet = new ShaderSetDescription(
                new VertexLayoutDescription[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("ColorUInt", VertexElementSemantic.TextureCoordinate, VertexElementFormat.UInt4))
                },
                TestShaders.LoadVertexFragment(RF, "UIntVertexAttribs"));

            ResourceLayout layout = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Ortho", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            ResourceLayout layout2 = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

            ResourceLayout layout3 = RF.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("InfoBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            Texture tex = RF.CreateTexture(TextureDescription.Texture2D(16, 16, 1, 1, PixelFormat.R32G32B32A32Float, TextureUsage.Sampled));
            TextureView texView = RF.CreateTextureView(tex);

            ResourceSet set = RF.CreateResourceSet(new ResourceSetDescription(layout, infoBuffer, orthoBuffer));
            ResourceSet set2 = RF.CreateResourceSet(new ResourceSetDescription(layout2, infoBuffer, texView));
            ResourceSet set3 = RF.CreateResourceSet(new ResourceSetDescription(layout3, infoBuffer));

            GraphicsPipelineDescription gpd = new GraphicsPipelineDescription(
                BlendStateDescription.SINGLE_OVERRIDE_BLEND,
                DepthStencilStateDescription.DISABLED,
                RasterizerStateDescription.DEFAULT,
                PrimitiveTopology.PointList,
                shaderSet,
                layout,
                new OutputDescription(null, new OutputAttachmentDescription(PixelFormat.B8G8R8A8UNorm)));

            Pipeline pipeline = RF.CreateGraphicsPipeline(ref gpd);

            CommandList cl = RF.CreateCommandList();
            cl.Begin();
            cl.SetPipeline(pipeline);
            cl.SetGraphicsResourceSet(0, set);
            Assert.Throws<VeldridException>(() => cl.SetGraphicsResourceSet(0, set2)); // Wrong type
            Assert.Throws<VeldridException>(() => cl.SetGraphicsResourceSet(0, set3)); // Wrong count
            cl.End();
        }
    }

#if TESTOPENGL
    [Trait("Backend", "OpenGL")]
    public class OpenGLResourceSetTests : ResourceSetTests<OpenGLDeviceCreator> { }
#endif
#if TESTOPENGLES
    [Trait("Backend", "OpenGLES")]
    public class OpenGLESResourceSetTests : ResourceSetTests<OpenGLESDeviceCreator> { }
#endif
#if TESTVULKAN
    [Trait("Backend", "Vulkan")]
    public class VulkanResourceSetTests : ResourceSetTests<VulkanDeviceCreator> { }
#endif
#if TESTD3D11
    [Trait("Backend", "D3D11")]
    public class D3D11ResourceSetTests : ResourceSetTests<D3D11DeviceCreator> { }
#endif
#if TESTMETAL
    [Trait("Backend", "Metal")]
    public class MetalResourceSetTests : ResourceSetTests<MetalDeviceCreator> { }
#endif
}
