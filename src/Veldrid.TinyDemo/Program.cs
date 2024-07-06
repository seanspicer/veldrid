using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace SampleExe
{
    public struct SceneData
    {
        public DeviceBuffer VertexBuffer;
        public DeviceBuffer IndexBuffer;
        public DeviceBuffer ModelViewBuffer;
        public ResourceSet ModelViewResourceSet;
        public Pipeline Pipeline;
    }

    public class Program
    {
        private static int windowWidth { get; } = 960;
        private static int windowHeight { get; } = 540;

        public static void Main(string[] args)
        {
            Sdl2Window window;
            GraphicsDevice gd;
            const bool color_srgb = true;

            WindowCreateInfo windowCi = new WindowCreateInfo
            {
                X = 100,
                Y = 100,
                WindowWidth = windowWidth,
                WindowHeight = windowHeight,
                WindowInitialState = WindowState.Normal,
                WindowTitle = "Veldrid TinyDemo",
            };
            GraphicsDeviceOptions gdOptions = new GraphicsDeviceOptions(
                false,
                null,
                false,
                ResourceBindingModel.Improved,
                true,
                true,
                color_srgb);
#if DEBUG
            gdOptions.Debug = true;
#endif
            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCi,
                gdOptions,
                out window,
                out gd);

            var sd = new SceneData();

            createAllObjects(gd, ref sd);

            while (window.Exists)
            {
                window.PumpEvents();

                draw(gd, ref sd);

                gd.SwapBuffers();
            }

            destroyAllObjects(gd, sd);
            gd.Dispose();
        }

        private static void draw(GraphicsDevice gd, ref SceneData sd)
        {
            var cl = gd.ResourceFactory.CreateCommandList();
            cl.Name = "Draw List";
            cl.Begin();
            {
                cl.SetFramebuffer(gd.SwapchainFramebuffer);
                cl.ClearColorTarget(0, RgbaFloat.BLACK);
                cl.SetFullViewports();
                cl.SetPipeline(sd.Pipeline);
                cl.SetGraphicsResourceSet(0, sd.ModelViewResourceSet);
                cl.SetVertexBuffer(0, sd.VertexBuffer);
                cl.SetIndexBuffer(sd.IndexBuffer, IndexFormat.UInt16);
                float timeFactor = Environment.TickCount / 1000f;

                var modelViewMat =
                    Matrix4x4.CreateLookAt(new Vector3(2 * (float)Math.Sin(timeFactor),
                            (float)Math.Sin(timeFactor),
                            2 * (float)Math.Cos(timeFactor)),
                        Vector3.Zero, Vector3.UnitY)
                    * Matrix4x4.CreatePerspectiveFieldOfView(1.05f,
                        (float)windowWidth / windowHeight,
                        .5f,
                        10f);

                cl.UpdateBuffer(sd.ModelViewBuffer, 0, modelViewMat);
                cl.DrawIndexed((uint)Cube.INDICES.Length);
            }
            cl.End();
            gd.SubmitCommands(cl);
            cl.Dispose();
        }

        private static void destroyAllObjects(GraphicsDevice gd, SceneData sd)
        {
            gd.WaitForIdle();
            destroyAllDeviceObjects(sd);
            gd.WaitForIdle();
        }

        private static void createAllObjects(GraphicsDevice gd, ref SceneData sd)
        {
            var cl = gd.ResourceFactory.CreateCommandList();
            cl.Name = "Main Command List";
            cl.Begin();
            createAllDeviceObjects(gd, cl, ref sd);
            cl.End();
            gd.SubmitCommands(cl);
            cl.Dispose();
        }

        private static void createAllDeviceObjects(GraphicsDevice gd, CommandList cl, ref SceneData sd)
        {
            ResourceFactory factory = gd.ResourceFactory;

            var vtxBufferDesc = new BufferDescription((uint)(Cube.VERTICES.Length * VertexPositionColor.SIZE_IN_BYTES), BufferUsage.VertexBuffer);
            sd.VertexBuffer = factory.CreateBuffer(vtxBufferDesc);
            cl.UpdateBuffer(sd.VertexBuffer, 0, Cube.VERTICES);

            var idxBufferDesc =
                new BufferDescription((uint)(Cube.INDICES.Length * sizeof(uint)), BufferUsage.IndexBuffer);
            sd.IndexBuffer = factory.CreateBuffer(idxBufferDesc);
            cl.UpdateBuffer(sd.IndexBuffer, 0, Cube.INDICES);

            // Load shaders
            string vsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "TinyDemo.vert");
            string fsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "TinyDemo.frag");

            byte[] vsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(vsPath));
            byte[] fsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(fsPath));

            var vertexShaderDescription = new ShaderDescription(
                ShaderStages.Vertex,
                vsBytes,
                "main", true);

            var fragmentShaderDescription = new ShaderDescription(
                ShaderStages.Fragment,
                fsBytes,
                "main", true);

            (Shader vs, Shader fs) =
                crossCompileShaders(gd, vertexShaderDescription, fragmentShaderDescription);

            var bindableResourceList = new List<IBindableResource>();

            sd.ModelViewBuffer =
                factory.CreateBuffer(new BufferDescription(64u,
                    BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            bindableResourceList.Add(
                new DeviceBufferRange(sd.ModelViewBuffer, 0, 64u));

            var modelViewResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ModelView", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            ));

            sd.ModelViewResourceSet = factory.CreateResourceSet(
                new ResourceSetDescription(modelViewResourceLayout, sd.ModelViewBuffer));

            VertexLayoutDescription[] vtxLayoutDescriptionArray =
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                    new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4))
            };

            ResourceLayout modelViewMatrixLayout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ModelView",
                        ResourceKind.UniformBuffer,
                        ShaderStages.Vertex,
                        ResourceLayoutElementOptions.DynamicBinding)));

            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription(
                BlendStateDescription.SINGLE_ALPHA_BLEND,
                gd.IsDepthRangeZeroToOne ? DepthStencilStateDescription.DEPTH_ONLY_GREATER_EQUAL : DepthStencilStateDescription.DEPTH_ONLY_LESS_EQUAL,
                RasterizerStateDescription.DEFAULT,
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    vtxLayoutDescriptionArray,
                    new[] { vs, fs },
                    new[] { new SpecializationConstant(100, gd.IsClipSpaceYInverted) }),
                new ResourceLayout[] { modelViewMatrixLayout },
                gd.SwapchainFramebuffer.OutputDescription);

            sd.Pipeline = factory.CreateGraphicsPipeline(pipelineDescription);
        }

        private static void destroyAllDeviceObjects(SceneData sd)
        {
            sd.VertexBuffer.Dispose();
            sd.IndexBuffer.Dispose();
            sd.ModelViewBuffer.Dispose();
            sd.ModelViewResourceSet.Dispose();
            sd.Pipeline.Dispose();
        }

        private static (Shader, Shader) crossCompileShaders(GraphicsDevice gd, ShaderDescription vsDesc, ShaderDescription fsDesc)
        {
            var result = SpirvCompilation.CompileVertexFragment(
                vsDesc.ShaderBytes, fsDesc.ShaderBytes,
                CrossCompileTarget.MSL,
                GetOptions(gd));

            var vertexShaderDescription = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.ASCII.GetBytes(result.VertexShader),
                "main0", vsDesc.Debug);

            var fragmentShaderDescription = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.ASCII.GetBytes(result.FragmentShader),
                "main0", fsDesc.Debug);

            var vs = gd.ResourceFactory.CreateShader(vertexShaderDescription);
            var fs = gd.ResourceFactory.CreateShader(fragmentShaderDescription);

            return (vs, fs);
        }

        public static CrossCompileOptions GetOptions(GraphicsDevice gd)
        {
            SpecializationConstant[] specializations = getSpecializations(gd);

            bool fixClipZ = (gd.BackendType == GraphicsBackend.OpenGL || gd.BackendType == GraphicsBackend.OpenGLES)
                            && !gd.IsDepthRangeZeroToOne;
            const bool invert_y = false;

            return new CrossCompileOptions(fixClipZ, invert_y, specializations);
        }

        private static SpecializationConstant[] getSpecializations(GraphicsDevice gd)
        {
            bool glOrGles = gd.BackendType == GraphicsBackend.OpenGL || gd.BackendType == GraphicsBackend.OpenGLES;

            List<SpecializationConstant> specializations = new List<SpecializationConstant>();
            specializations.Add(new SpecializationConstant(100, gd.IsClipSpaceYInverted));
            specializations.Add(new SpecializationConstant(101, glOrGles));
            specializations.Add(new SpecializationConstant(102, gd.IsDepthRangeZeroToOne));

            PixelFormat swapchainFormat = gd.MainSwapchain.Framebuffer.OutputDescription.ColorAttachments[0].Format;
            bool swapchainIsSrgb = swapchainFormat == PixelFormat.B8G8R8A8UNormSRgb
                                   || swapchainFormat == PixelFormat.R8G8B8A8UNormSRgb;
            specializations.Add(new SpecializationConstant(103, swapchainIsSrgb));

            return specializations.ToArray();
        }
    }
}

public struct VertexPositionColor
{
    public const uint SIZE_IN_BYTES = 32;

    public Vector3 Position;
    public RgbaFloat Color;

    public VertexPositionColor(Vector3 position, RgbaFloat color)
    {
        Position = position;
        Color = color;
    }
}

public static class Cube
{
    public static readonly VertexPositionColor[] VERTICES =
    {
        // Front & Back
        new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.5f), RgbaFloat.RED), new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.5f), RgbaFloat.RED),
        new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.5f), RgbaFloat.RED), new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.5f), RgbaFloat.RED),
        new VertexPositionColor(new Vector3(-0.5f, 0.5f, -0.5f), RgbaFloat.ORANGE), new VertexPositionColor(new Vector3(0.5f, 0.5f, -0.5f), RgbaFloat.ORANGE),
        new VertexPositionColor(new Vector3(0.5f, -0.5f, -0.5f), RgbaFloat.ORANGE), new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), RgbaFloat.ORANGE),

        // Top & Bottom
        new VertexPositionColor(new Vector3(-0.5f, 0.5f, -0.5f), RgbaFloat.YELLOW), new VertexPositionColor(new Vector3(0.5f, 0.5f, -0.5f), RgbaFloat.YELLOW),
        new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.5f), RgbaFloat.YELLOW), new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.5f), RgbaFloat.YELLOW),
        new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), RgbaFloat.GREEN), new VertexPositionColor(new Vector3(0.5f, -0.5f, -0.5f), RgbaFloat.GREEN),
        new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.5f), RgbaFloat.GREEN), new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.5f), RgbaFloat.GREEN),

        // Left & Right
        new VertexPositionColor(new Vector3(-0.5f, 0.5f, -0.5f), RgbaFloat.BLUE), new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.5f), RgbaFloat.BLUE),
        new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.5f), RgbaFloat.BLUE), new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), RgbaFloat.BLUE),
        new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.5f), RgbaFloat.PINK), new VertexPositionColor(new Vector3(0.5f, 0.5f, -0.5f), RgbaFloat.PINK),
        new VertexPositionColor(new Vector3(0.5f, -0.5f, -0.5f), RgbaFloat.PINK), new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.5f), RgbaFloat.PINK)
    };

    public static readonly ushort[] INDICES =
        [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 8, 9, 10, 8, 10, 11, 12, 14, 13, 12, 15, 14, 16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23];
}
