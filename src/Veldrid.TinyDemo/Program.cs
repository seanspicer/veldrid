using System.Collections.Generic;
using System.Numerics;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace SampleExe
{
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

            var vtxBufferDesc =
                new BufferDescription((uint) (Cube.Vertices.Length * VertexPositionColor.SizeInBytes), BufferUsage.VertexBuffer);
            var vbo = gd.ResourceFactory.CreateBuffer(vtxBufferDesc);

            var idxBufferDesc =
                new BufferDescription((uint) (Cube.Indices.Length * sizeof(uint)), BufferUsage.IndexBuffer);
            var ibo = gd.ResourceFactory.CreateBuffer(idxBufferDesc);

            string folder = gd.BackendType == GraphicsBackend.Direct3D11 ? "HLSL" : "GLSL";
            string extension = gd.BackendType == GraphicsBackend.Direct3D11 ? "hlsl" : "glsl";

            var resourceLayoutElementDescriptionList = new List<ResourceLayoutElementDescription>();
            var bindableResourceList = new List<IBindableResource>();

            var pd = new GraphicsPipelineDescription();
            pd.PrimitiveTopology = PrimitiveTopology.TriangleList;

            var modelViewBuffer =
                gd.ResourceFactory.CreateBuffer(new BufferDescription(64u,
                    BufferUsage.UniformBuffer | BufferUsage.Dynamic));

            resourceLayoutElementDescriptionList.Add(
                new ResourceLayoutElementDescription("Model", ResourceKind.UniformBuffer, ShaderStages.Vertex,
                    ResourceLayoutElementOptions.DynamicBinding));

            //bindableResourceList.Add(ri.ModelViewBuffer);
            bindableResourceList.Add(
                new DeviceBufferRange(modelViewBuffer, 0, 64u));

            var vtxLayoutDescription = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.Position,
                    VertexElementFormat.Float3),
                new VertexElementDescription("TexCoords", VertexElementSemantic.Color,
                    VertexElementFormat.Float4));

            gd.ResourceFactory.CreateResourceLayout(
                new ResourceLayoutDescription(resourceLayoutElementDescriptionList.ToArray()));

            gd.UpdateBuffer(vbo, 0, Cube.Vertices);
            gd.UpdateBuffer(ibo, 0, Cube.Indices);

            while (window.Exists)
            {
                window.PumpEvents();

                gd.SwapBuffers();
            }
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
