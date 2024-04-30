using System;

namespace Veldrid.OpenGL
{
    internal class OpenGLResourceFactory : ResourceFactory
    {
        public override GraphicsBackend BackendType => _gd.BackendType;
        private readonly OpenGLGraphicsDevice _gd;
        private readonly StagingMemoryPool _pool;

        public OpenGLResourceFactory(OpenGLGraphicsDevice gd)
            : base(gd.Features)
        {
            _gd = gd;
            _pool = gd.StagingMemoryPool;
        }

        public override CommandList CreateCommandList(ref CommandListDescription description)
        {
            return new OpenGLCommandList(_gd, ref description);
        }

        public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
        {
            return new OpenGLFramebuffer(_gd, ref description);
        }

        public override Pipeline CreateComputePipeline(ref ComputePipelineDescription description)
        {
            var pipeline = new OpenGLPipeline(_gd, ref description);
            _gd.EnsureResourceInitialized(pipeline);
            return pipeline;
        }

        public override ResourceLayout CreateResourceLayout(ref ResourceLayoutDescription description)
        {
            return new OpenGLResourceLayout(ref description);
        }

        public override ResourceSet CreateResourceSet(ref ResourceSetDescription description)
        {
            ValidationHelpers.ValidateResourceSet(_gd, ref description);
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
            return new OpenGLPipeline(_gd, ref description);
        }

        protected override Sampler CreateSamplerCore(ref SamplerDescription description)
        {
            return new OpenGLSampler(_gd, ref description);
        }

        protected override Shader CreateShaderCore(ref ShaderDescription description)
        {
            var stagingBlock = _pool.Stage(description.ShaderBytes);
            var shader = new OpenGLShader(_gd, description.Stage, stagingBlock, description.EntryPoint);
            _gd.EnsureResourceInitialized(shader);
            return shader;
        }

        protected override Texture CreateTextureCore(ref TextureDescription description)
        {
            return new OpenGLTexture(_gd, ref description);
        }

        protected override Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description)
        {
            return new OpenGLTexture(_gd, (uint)nativeTexture, ref description);
        }

        protected override TextureView CreateTextureViewCore(ref TextureViewDescription description)
        {
            return new OpenGLTextureView(_gd, ref description);
        }

        protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
        {
            return new OpenGLBuffer(
                _gd,
                description.SizeInBytes,
                description.Usage);
        }
    }
}
