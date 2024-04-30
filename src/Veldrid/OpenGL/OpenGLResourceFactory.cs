using System;

namespace Veldrid.OpenGL
{
    internal class OpenGLResourceFactory : ResourceFactory
    {
        public override GraphicsBackend BackendType => gd.BackendType;
        private readonly OpenGLGraphicsDevice gd;
        private readonly StagingMemoryPool pool;

        public OpenGLResourceFactory(OpenGLGraphicsDevice gd)
            : base(gd.Features)
        {
            this.gd = gd;
            pool = gd.StagingMemoryPool;
        }

        public override CommandList CreateCommandList(ref CommandListDescription description)
        {
            return new OpenGLCommandList(gd, ref description);
        }

        public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
        {
            return new OpenGLFramebuffer(gd, ref description);
        }

        public override Pipeline CreateComputePipeline(ref ComputePipelineDescription description)
        {
            var pipeline = new OpenGLPipeline(gd, ref description);
            gd.EnsureResourceInitialized(pipeline);
            return pipeline;
        }

        public override ResourceLayout CreateResourceLayout(ref ResourceLayoutDescription description)
        {
            return new OpenGLResourceLayout(ref description);
        }

        public override ResourceSet CreateResourceSet(ref ResourceSetDescription description)
        {
            ValidationHelpers.ValidateResourceSet(gd, ref description);
            return new OpenGLResourceSet(ref description);
        }

        public override Fence CreateFence(bool signaled)
        {
            return new OpenGLFence(signaled);
        }

        public override Swapchain CreateSwapchain(ref SwapchainDescription description)
        {
            throw new NotSupportedException("OpenGL does not support creating Swapchain objects.");
        }

        protected override Pipeline CreateGraphicsPipelineCore(ref GraphicsPipelineDescription description)
        {
            return new OpenGLPipeline(gd, ref description);
        }

        protected override Sampler CreateSamplerCore(ref SamplerDescription description)
        {
            return new OpenGLSampler(gd, ref description);
        }

        protected override Shader CreateShaderCore(ref ShaderDescription description)
        {
            var stagingBlock = pool.Stage(description.ShaderBytes);
            var shader = new OpenGLShader(gd, description.Stage, stagingBlock, description.EntryPoint);
            gd.EnsureResourceInitialized(shader);
            return shader;
        }

        protected override Texture CreateTextureCore(ref TextureDescription description)
        {
            return new OpenGLTexture(gd, ref description);
        }

        protected override Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description)
        {
            return new OpenGLTexture(gd, (uint)nativeTexture, ref description);
        }

        protected override TextureView CreateTextureViewCore(ref TextureViewDescription description)
        {
            return new OpenGLTextureView(gd, ref description);
        }

        protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
        {
            return new OpenGLBuffer(
                gd,
                description.SizeInBytes,
                description.Usage);
        }
    }
}
