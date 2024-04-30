using Veldrid.OpenGLBinding;
using static Veldrid.OpenGLBinding.OpenGLNative;
using static Veldrid.OpenGL.OpenGLUtil;

namespace Veldrid.OpenGL
{
    internal unsafe class OpenGLFramebuffer : Framebuffer, IOpenGLDeferredResource
    {
        public uint Framebuffer => framebuffer;

        public override bool IsDisposed => disposeRequested;

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                nameChanged = true;
            }
        }

        public bool Created { get; private set; }
        private readonly OpenGLGraphicsDevice gd;
        private uint framebuffer;

        private string name;
        private bool nameChanged;
        private bool disposeRequested;
        private bool disposed;

        public OpenGLFramebuffer(OpenGLGraphicsDevice gd, ref FramebufferDescription description)
            : base(description.DepthTarget, description.ColorTargets)
        {
            this.gd = gd;
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposeRequested)
            {
                disposeRequested = true;
                gd.EnqueueDisposal(this);
            }
        }

        #endregion

        public void EnsureResourcesCreated()
        {
            if (!Created) CreateGLResources();

            if (nameChanged)
            {
                nameChanged = false;
                if (gd.Extensions.KhrDebug) SetObjectLabel(ObjectLabelIdentifier.Framebuffer, framebuffer, name);
            }
        }

        public void CreateGLResources()
        {
            glGenFramebuffers(1, out framebuffer);
            CheckLastError();

            glBindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            CheckLastError();

            uint colorCount = (uint)ColorTargets.Count;

            if (colorCount > 0)
            {
                for (int i = 0; i < colorCount; i++)
                {
                    var colorAttachment = ColorTargets[i];
                    var glTex = Util.AssertSubtype<Texture, OpenGLTexture>(colorAttachment.Target);
                    glTex.EnsureResourcesCreated();

                    gd.TextureSamplerManager.SetTextureTransient(glTex.TextureTarget, glTex.Texture);
                    CheckLastError();

                    var textureTarget = GetTextureTarget(glTex, colorAttachment.ArrayLayer);

                    if (glTex.ArrayLayers == 1)
                    {
                        glFramebufferTexture2D(
                            FramebufferTarget.Framebuffer,
                            GLFramebufferAttachment.ColorAttachment0 + i,
                            textureTarget,
                            glTex.Texture,
                            (int)colorAttachment.MipLevel);
                        CheckLastError();
                    }
                    else
                    {
                        glFramebufferTextureLayer(
                            FramebufferTarget.Framebuffer,
                            GLFramebufferAttachment.ColorAttachment0 + i,
                            glTex.Texture,
                            (int)colorAttachment.MipLevel,
                            (int)colorAttachment.ArrayLayer);
                        CheckLastError();
                    }
                }

                var bufs = stackalloc DrawBuffersEnum[(int)colorCount];
                for (int i = 0; i < colorCount; i++) bufs[i] = DrawBuffersEnum.ColorAttachment0 + i;
                glDrawBuffers(colorCount, bufs);
                CheckLastError();
            }

            uint depthTextureID = 0;
            var depthTarget = TextureTarget.Texture2D;

            if (DepthTarget != null)
            {
                var glDepthTex = Util.AssertSubtype<Texture, OpenGLTexture>(DepthTarget.Value.Target);
                glDepthTex.EnsureResourcesCreated();
                depthTarget = glDepthTex.TextureTarget;

                depthTextureID = glDepthTex.Texture;

                gd.TextureSamplerManager.SetTextureTransient(depthTarget, glDepthTex.Texture);
                CheckLastError();

                depthTarget = GetTextureTarget(glDepthTex, DepthTarget.Value.ArrayLayer);

                var framebufferAttachment = GLFramebufferAttachment.DepthAttachment;
                if (FormatHelpers.IsStencilFormat(glDepthTex.Format)) framebufferAttachment = GLFramebufferAttachment.DepthStencilAttachment;

                if (glDepthTex.ArrayLayers == 1)
                {
                    glFramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        framebufferAttachment,
                        depthTarget,
                        depthTextureID,
                        (int)DepthTarget.Value.MipLevel);
                    CheckLastError();
                }
                else
                {
                    glFramebufferTextureLayer(
                        FramebufferTarget.Framebuffer,
                        framebufferAttachment,
                        glDepthTex.Texture,
                        (int)DepthTarget.Value.MipLevel,
                        (int)DepthTarget.Value.ArrayLayer);
                    CheckLastError();
                }
            }

            var errorCode = glCheckFramebufferStatus(FramebufferTarget.Framebuffer);
            CheckLastError();
            if (errorCode != FramebufferErrorCode.FramebufferComplete) throw new VeldridException("Framebuffer was not successfully created: " + errorCode);

            Created = true;
        }

        public void DestroyGLResources()
        {
            if (!disposed)
            {
                disposed = true;
                uint framebuffer = this.framebuffer;
                glDeleteFramebuffers(1, ref framebuffer);
                CheckLastError();
            }
        }
    }
}
