using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace Veldrid.TinyDemo
{
    /// <summary>
    /// This struct represents a vertex with a position and a color.
    /// </summary>
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

    /// <summary>
    /// This class represents a cube with 8 vertices and 36 indices.
    /// </summary>
    public static class Cube
    {
        public static readonly VertexPositionColor[] VERTICES =
        {
            // Front & Back
            new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.5f), RgbaFloat.Red), new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.5f), RgbaFloat.Red),
            new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.5f), RgbaFloat.Red), new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.5f), RgbaFloat.Red),
            new VertexPositionColor(new Vector3(-0.5f, 0.5f, -0.5f), RgbaFloat.Orange), new VertexPositionColor(new Vector3(0.5f, 0.5f, -0.5f), RgbaFloat.Orange),
            new VertexPositionColor(new Vector3(0.5f, -0.5f, -0.5f), RgbaFloat.Orange), new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), RgbaFloat.Orange),

            // Top & Bottom
            new VertexPositionColor(new Vector3(-0.5f, 0.5f, -0.5f), RgbaFloat.Yellow), new VertexPositionColor(new Vector3(0.5f, 0.5f, -0.5f), RgbaFloat.Yellow),
            new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.5f), RgbaFloat.Yellow), new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.5f), RgbaFloat.Yellow),
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), RgbaFloat.Green), new VertexPositionColor(new Vector3(0.5f, -0.5f, -0.5f), RgbaFloat.Green),
            new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.5f), RgbaFloat.Green), new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.5f), RgbaFloat.Green),

            // Left & Right
            new VertexPositionColor(new Vector3(-0.5f, 0.5f, -0.5f), RgbaFloat.Blue), new VertexPositionColor(new Vector3(-0.5f, 0.5f, 0.5f), RgbaFloat.Blue),
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.5f), RgbaFloat.Blue), new VertexPositionColor(new Vector3(-0.5f, -0.5f, -0.5f), RgbaFloat.Blue),
            new VertexPositionColor(new Vector3(0.5f, 0.5f, 0.5f), RgbaFloat.Pink), new VertexPositionColor(new Vector3(0.5f, 0.5f, -0.5f), RgbaFloat.Pink),
            new VertexPositionColor(new Vector3(0.5f, -0.5f, -0.5f), RgbaFloat.Pink), new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.5f), RgbaFloat.Pink)
        };

        public static readonly ushort[] INDICES =
            [0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 8, 9, 10, 8, 10, 11, 12, 14, 13, 12, 15, 14, 16, 17, 18, 16, 18, 19, 20, 21, 22, 20, 22, 23];
    }

    /// <summary>
    /// This struct represents the minimal data needed to render a scene.
    /// </summary>
    public struct SceneData
    {
        public DeviceBuffer VertexBuffer;
        public DeviceBuffer IndexBuffer;
        public DeviceBuffer ModelViewProjectionBuffer;
        public ResourceLayout ModelViewProjectionResourceLayout;
        public ResourceSet ModelViewProjectionResourceSet;
        public Pipeline Pipeline;
    }

    /// <summary>
    /// Main Program class.
    /// </summary>
    public class Program
    {
        private static int windowWidth { get; } = 960;
        private static int windowHeight { get; } = 540;

        public static void Main(string[] args)
        {
            //
            // Step 1 - Create a window to draw into
            //
            createWindow(out var window, out var gd);

            //
            // Step 2 - Create the scene data necessary to render the scene, and allocate the device objects.
            //
            var sd = new SceneData();
            createAllObjects(gd, ref sd);

            //
            // Step 3 - Main loop
            //
            while (window.Exists)
            {
                window.PumpEvents();
                draw(gd, ref sd);
                gd.SwapBuffers();
            }

            //
            // Step 4 - Cleanup
            //
            destroyAllObjects(gd, sd);
            gd.Dispose();
        }

        /// <summary>
        /// Create a window and a graphics device.
        /// </summary>
        /// <param name="window"></param>
        /// <param name="gd"></param>
        private static void createWindow(out Sdl2Window window, out GraphicsDevice gd)
        {
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
        }

        /// <summary>
        /// Create all the device objects necessary to render the scene.  This is done by constructing
        /// A command list and submitting it to the graphics device.
        /// </summary>
        /// <param name="gd"></param>
        /// <param name="sd"></param>
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

        /// <summary>
        /// Draw the scene.  This is done by constructing a command list and submitting it to the
        /// graphics device.
        /// </summary>
        /// <param name="gd"></param>
        /// <param name="sd"></param>
        private static void draw(GraphicsDevice gd, ref SceneData sd)
        {
            var cl = gd.ResourceFactory.CreateCommandList();
            cl.Name = "Draw List";
            cl.Begin();
            {
                cl.SetFramebuffer(gd.SwapchainFramebuffer);
                cl.ClearColorTarget(0, RgbaFloat.Black);
                cl.SetFullViewports();
                cl.SetPipeline(sd.Pipeline);
                cl.SetGraphicsResourceSet(0, sd.ModelViewProjectionResourceSet);
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

                cl.UpdateBuffer(sd.ModelViewProjectionBuffer, 0, modelViewMat);
                cl.DrawIndexed((uint)Cube.INDICES.Length);
            }
            cl.End();
            gd.SubmitCommands(cl);
            cl.Dispose();
        }

        /// <summary>
        /// Destroy all the device objects necessary to render the scene.
        /// </summary>
        /// <param name="gd"></param>
        /// <param name="sd"></param>
        private static void destroyAllObjects(GraphicsDevice gd, SceneData sd)
        {
            gd.WaitForIdle();
            destroyAllDeviceObjects(sd);
            gd.WaitForIdle();
        }

        /// <summary>
        /// This is a helper function to create all the device objects necessary to render the scene.
        /// </summary>
        /// <param name="gd"></param>
        /// <param name="cl"></param>
        /// <param name="sd"></param>
        private static void createAllDeviceObjects(GraphicsDevice gd, CommandList cl, ref SceneData sd)
        {
            // Get a reference to the graphics device's resource factory.
            ResourceFactory factory = gd.ResourceFactory;

            // Create the vertex buffer and initialize
            var vtxBufferDesc = new BufferDescription((uint)(Cube.VERTICES.Length * VertexPositionColor.SIZE_IN_BYTES), BufferUsage.VertexBuffer);
            sd.VertexBuffer = factory.CreateBuffer(vtxBufferDesc);
            cl.UpdateBuffer(sd.VertexBuffer, 0, Cube.VERTICES);

            // Create the index buffer and initialize
            var idxBufferDesc = new BufferDescription((uint)(Cube.INDICES.Length * sizeof(uint)), BufferUsage.IndexBuffer);
            sd.IndexBuffer = factory.CreateBuffer(idxBufferDesc);
            cl.UpdateBuffer(sd.IndexBuffer, 0, Cube.INDICES);

            // Get the Shader file bytes.  These shaders are written in GLSL
            byte[] vsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "TinyDemo.vert")));
            byte[] fsBytes = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "TinyDemo.frag")));

            // Construct a vertex shader description
            var vertexShaderDescription = new ShaderDescription(
                ShaderStages.Vertex,
                vsBytes,
                "main", true);

            // Construct a fragment shader description.
            var fragmentShaderDescription = new ShaderDescription(
                ShaderStages.Fragment,
                fsBytes,
                "main", true);

            // Build the shader objects. This method cross-compiles the shaders for the
            // current graphics device backend.
            (Shader vs, Shader fs) =
                crossCompileShaders(gd, vertexShaderDescription, fragmentShaderDescription);

            // Create the buffer that will be used for the ModelViewProjection matrix.  This
            // Is a uniform buffer, with dynamic usage, as we will be altering its contents
            // on every frame.
            sd.ModelViewProjectionBuffer =
                factory.CreateBuffer(new BufferDescription(64u,
                    BufferUsage.UniformBuffer | BufferUsage.DynamicReadWrite));

            // Create the resource layout for the ModelViewProjection buffer.  This is necessary
            // to describe the layout of the buffer to the pipeline.
            sd.ModelViewProjectionResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ModelViewProjection", ResourceKind.UniformBuffer, ShaderStages.Vertex)
            ));

            // Use the resource layout to create the ResourceSet for the ModelViewProjection buffer.
            // This will be bound on every draw call prior to updating the buffer.
            sd.ModelViewProjectionResourceSet = factory.CreateResourceSet(
                new ResourceSetDescription(sd.ModelViewProjectionResourceLayout, sd.ModelViewProjectionBuffer));

            // Create the vertex layout description.  This describes the layout of the vertex buffer.
            VertexLayoutDescription[] vtxLayoutDescriptionArray =
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3),
                    new VertexElementDescription("Color", VertexElementSemantic.Color, VertexElementFormat.Float4))
            };

            // Create the Graphics pipeline description. This describes the pipeline state that will be set
            // on the graphics device for rendering
            GraphicsPipelineDescription pipelineDescription = new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                gd.IsDepthRangeZeroToOne ? DepthStencilStateDescription.DepthOnlyGreaterEqual : DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerStateDescription.Default,
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(
                    vtxLayoutDescriptionArray,
                    new[] { vs, fs },
                    new[] { new SpecializationConstant(100, gd.IsClipSpaceYInverted) }),
                new ResourceLayout[] { sd.ModelViewProjectionResourceLayout },
                gd.SwapchainFramebuffer.OutputDescription);

            // Create the graphics pipeline
            sd.Pipeline = factory.CreateGraphicsPipeline(pipelineDescription);
        }

        /// <summary>
        /// Destroy all the device objects necessary to render the scene.
        /// </summary>
        /// <param name="sd"></param>
        private static void destroyAllDeviceObjects(SceneData sd)
        {
            sd.VertexBuffer.Dispose();
            sd.IndexBuffer.Dispose();
            sd.ModelViewProjectionBuffer.Dispose();
            sd.ModelViewProjectionResourceLayout.Dispose();
            sd.ModelViewProjectionResourceSet.Dispose();
            sd.Pipeline.Dispose();
        }

        /// <summary>
        /// Cross compile the shaders for the current graphics device backend.
        /// </summary>
        /// <param name="gd"></param>
        /// <param name="vsDesc"></param>
        /// <param name="fsDesc"></param>
        /// <returns></returns>
        private static (Shader, Shader) crossCompileShaders(GraphicsDevice gd, ShaderDescription vsDesc, ShaderDescription fsDesc)
        {
            var target = gd.BackendType switch
            {
                GraphicsBackend.Metal => CrossCompileTarget.MSL,
                GraphicsBackend.Direct3D11 => CrossCompileTarget.HLSL,
                _ => CrossCompileTarget.GLSL
            };

            var result = SpirvCompilation.CompileVertexFragment(
                vsDesc.ShaderBytes, fsDesc.ShaderBytes,
                target,
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

        /// <summary>
        /// Get any required options for cross compiling the shaders.
        /// </summary>
        /// <param name="gd"></param>
        /// <returns></returns>
        public static CrossCompileOptions GetOptions(GraphicsDevice gd)
        {
            SpecializationConstant[] specializations = getSpecializations(gd);

            bool fixClipZ = (gd.BackendType == GraphicsBackend.OpenGL || gd.BackendType == GraphicsBackend.OpenGLES)
                            && !gd.IsDepthRangeZeroToOne;
            const bool invert_y = false;

            return new CrossCompileOptions(fixClipZ, invert_y, specializations);
        }

        /// <summary>
        /// Get any required specializations for cross compiling the shaders.
        /// </summary>
        /// <param name="gd"></param>
        /// <returns></returns>
        private static SpecializationConstant[] getSpecializations(GraphicsDevice gd)
        {
            bool glOrGles = gd.BackendType == GraphicsBackend.OpenGL || gd.BackendType == GraphicsBackend.OpenGLES;

            List<SpecializationConstant> specializations = new List<SpecializationConstant>();
            specializations.Add(new SpecializationConstant(100, gd.IsClipSpaceYInverted));
            specializations.Add(new SpecializationConstant(101, glOrGles));
            specializations.Add(new SpecializationConstant(102, gd.IsDepthRangeZeroToOne));

            PixelFormat swapchainFormat = gd.MainSwapchain.Framebuffer.OutputDescription.ColorAttachments[0].Format;
            bool swapchainIsSrgb = swapchainFormat == PixelFormat.B8_G8_R8_A8_UNorm_SRgb
                                   || swapchainFormat == PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
            specializations.Add(new SpecializationConstant(103, swapchainIsSrgb));

            return specializations.ToArray();
        }
    }
}


