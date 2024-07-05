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
        public static void Main(string[] args)
        {
            Sdl2Window window;
            GraphicsDevice gd;
            bool colorSrgb = true;

            WindowCreateInfo windowCI = new WindowCreateInfo
            {
                X = 100,
                Y = 100,
                WindowWidth = 960,
                WindowHeight = 540,
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
                colorSrgb);
#if DEBUG
            gdOptions.Debug = true;
#endif
            VeldridStartup.CreateWindowAndGraphicsDevice(
                windowCI,
                gdOptions,
                //VeldridStartup.GetPlatformDefaultBackend(),
                //GraphicsBackend.Metal,
                //GraphicsBackend.Vulkan,
                //GraphicsBackend.OpenGL,
                //GraphicsBackend.OpenGLES,
                out window,
                out gd);

            var sd = new SceneData();

            CreateAllObjects(gd, ref sd);

            while (window.Exists)
            {
                window.PumpEvents();

                Draw(gd, ref sd);

                gd.SwapBuffers();
            }

            DestroyAllObjects();
            gd.Dispose();
        }

        private static void Draw(GraphicsDevice gd, ref SceneData sd)
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

                var modelViewMat = Matrix4x4.CreateLookAt(
                        new Vector3(2 * (float)Math.Sin(timeFactor), (float)Math.Sin(timeFactor), 2 * (float)Math.Cos(timeFactor)),
                        Vector3.Zero,
                        Vector3.UnitY)
                    * Matrix4x4.CreatePerspectiveFieldOfView(1.05f, (float)1280 / 1024, .5f, 10f);

                cl.UpdateBuffer(sd.ModelViewBuffer, 0, modelViewMat);
                cl.DrawIndexed((uint)Cube.Indices.Length);
            }
            cl.End();
            gd.SubmitCommands(cl);
            cl.Dispose();
        }

        private static void DestroyAllObjects()
        {
            throw new System.NotImplementedException();
        }

        public static void CreateAllObjects(GraphicsDevice gd, ref SceneData sd)
        {
            var cl = gd.ResourceFactory.CreateCommandList();
            cl.Name = "Main Command List";
            cl.Begin();
            CreateAllDeviceObjects(gd, cl, ref sd);
            cl.End();
            gd.SubmitCommands(cl);
            cl.Dispose();
        }

        public static void CreateAllDeviceObjects(GraphicsDevice gd, CommandList cl, ref SceneData sd)
        {
            ResourceFactory factory = gd.ResourceFactory;

            var vtxBufferDesc =
                new BufferDescription((uint) (Cube.Vertices.Length * VertexPositionColor.SizeInBytes), BufferUsage.VertexBuffer);
            sd.VertexBuffer = factory.CreateBuffer(vtxBufferDesc);
            cl.UpdateBuffer(sd.VertexBuffer, 0, Cube.Vertices);

            var idxBufferDesc =
                new BufferDescription((uint) (Cube.Indices.Length * sizeof(uint)), BufferUsage.IndexBuffer);
            sd.IndexBuffer = factory.CreateBuffer(idxBufferDesc);
            cl.UpdateBuffer(sd.IndexBuffer, 0, Cube.Indices);

            // Load shaders
            string vsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", $"TinyDemo.vert");
            string fsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", $"TinyDemo.frag");

            var vsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(vsPath));
            var fsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(fsPath));

            var vertexShaderDescription = new ShaderDescription(
                ShaderStages.Vertex,
                vsBytes,
                "main", true);

            var fragmentShaderDescription = new ShaderDescription(
                ShaderStages.Fragment,
                fsBytes,
                "main", true);

            (Shader vs, Shader fs) =
                CrossCompileShaders(gd, vertexShaderDescription, fragmentShaderDescription);

            var bindableResourceList = new List<IBindableResource>();

            var pd = new GraphicsPipelineDescription();
            pd.PrimitiveTopology = PrimitiveTopology.TriangleList;

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

        private static (Shader, Shader) CrossCompileShaders(GraphicsDevice gd, ShaderDescription vsDesc, ShaderDescription fsDesc)
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
            SpecializationConstant[] specializations = GetSpecializations(gd);

            bool fixClipZ = (gd.BackendType == GraphicsBackend.OpenGL || gd.BackendType == GraphicsBackend.OpenGLES)
                            && !gd.IsDepthRangeZeroToOne;
            bool invertY = false;

            return new CrossCompileOptions(fixClipZ, invertY, specializations);
        }

        private static SpecializationConstant[] GetSpecializations(GraphicsDevice gd)
        {
            bool glOrGles = gd.BackendType == GraphicsBackend.OpenGL || gd.BackendType == GraphicsBackend.OpenGLES;

            List<SpecializationConstant> specializations = new List<SpecializationConstant>();
            specializations.Add(new SpecializationConstant(100, gd.IsClipSpaceYInverted));
            specializations.Add(new SpecializationConstant(101, glOrGles)); // TextureCoordinatesInvertedY
            specializations.Add(new SpecializationConstant(102, gd.IsDepthRangeZeroToOne));

            PixelFormat swapchainFormat = gd.MainSwapchain.Framebuffer.OutputDescription.ColorAttachments[0].Format;
            bool swapchainIsSrgb = swapchainFormat == PixelFormat.B8G8R8A8UNormSRgb
                                   || swapchainFormat == PixelFormat.R8G8B8A8UNormSRgb;
            specializations.Add(new SpecializationConstant(103, swapchainIsSrgb));

            return specializations.ToArray();
        }
    }
}

//     VertexBuffer vb = gd.ResourceFactory.CreateVertexBuffer(Cube.Vertices, new VertexDescriptor(VertexPositionColor.SizeInBytes, 2), false);
        //     IndexBuffer ib = gd.ResourceFactory.CreateIndexBuffer(Cube.Indices, false);
        //
        //     string folder = rc.BackendType == GraphicsBackend.Direct3D11 ? "HLSL" : "GLSL";
        //     string extension = rc.BackendType == GraphicsBackend.Direct3D11 ? "hlsl" : "glsl";
        //
        //     VertexInputLayout inputLayout = rc.ResourceFactory.CreateInputLayout(new VertexInputDescription[]
        //     {
        //         new VertexInputDescription(
        //             new VertexInputElement("Position", VertexSemanticType.Position, VertexElementFormat.Float3),
        //             new VertexInputElement("Color", VertexSemanticType.Color, VertexElementFormat.Float4))
        //     });
        //
        //     string vsPath = Path.Combine(AppContext.BaseDirectory, folder, $"vertex.{extension}");
        //     string fsPath = Path.Combine(AppContext.BaseDirectory, folder, $"fragment.{extension}");
        //
        //     Shader vs = gd.ResourceFactory.CreateShader(ShaderStages.Vertex, File.ReadAllText(vsPath));
        //     Shader fs = gd.ResourceFactory.CreateShader(ShaderStages.Fragment, File.ReadAllText(fsPath));
        //
        //     ShaderSet shaderSet = rc.ResourceFactory.CreateShaderSet(inputLayout, vs, fs);
        //     ShaderResourceBindingSlots bindingSlots = rc.ResourceFactory.CreateShaderResourceBindingSlots(
        //         shaderSet,
        //         new ShaderResourceDescription("ViewProjectionMatrix", ShaderConstantType.Matrix4x4));
        //     ConstantBuffer viewProjectionBuffer = rc.ResourceFactory.CreateConstantBuffer(ShaderConstantType.Matrix4x4);
        //
        //     while (window.Exists)
        //     {
        //         InputSnapshot snapshot = window.PumpEvents();
        //         gd.ClearBuffer();
        //
        //         gd.SetViewport(0, 0, window.Width, window.Height);
        //         float timeFactor = Environment.TickCount / 1000f;
        //         viewProjectionBuffer.SetData(
        //             Matrix4x4.CreateLookAt(
        //                 new Vector3(2 * (float)Math.Sin(timeFactor), (float)Math.Sin(timeFactor), 2 * (float)Math.Cos(timeFactor)),
        //                 Vector3.Zero,
        //                 Vector3.UnitY)
        //                 * Matrix4x4.CreatePerspectiveFieldOfView(1.05f, (float)window.Width / window.Height, .5f, 10f));
        //         rc.SetVertexBuffer(0, vb);
        //         rc.IndexBuffer = ib;
        //         rc.ShaderSet = shaderSet;
        //         rc.ShaderResourceBindingSlots = bindingSlots;
        //         rc.SetConstantBuffer(0, viewProjectionBuffer);
        //         rc.DrawIndexedPrimitives(Cube.Indices.Length);
        //
        //         rc.SwapBuffers();
        //     }
        // }
        //
        public struct VertexPositionColor
        {
            public const uint SizeInBytes = 32;

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
            public static readonly VertexPositionColor[] Vertices =
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

        public static readonly ushort[] Indices =
            { 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 8, 9, 10, 8, 10, 11, 12, 14, 13, 12, 15, 14, 16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23 };
    }
    // }
//}
