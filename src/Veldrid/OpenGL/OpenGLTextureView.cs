using System.Diagnostics;
using Veldrid.OpenGLBinding;
using static Veldrid.OpenGL.OpenGLUtil;
using static Veldrid.OpenGLBinding.OpenGLNative;

namespace Veldrid.OpenGL
{
    internal class OpenGLTextureView : TextureView, IOpenGLDeferredResource
    {
        public override bool IsDisposed => disposeRequested;

        public new OpenGLTexture Target { get; }

        public uint GLTargetTexture
        {
            get
            {
                Debug.Assert(Created);

                if (textureView == 0)
                {
                    Debug.Assert(Target.Created);
                    return Target.Texture;
                }

                return textureView;
            }
        }

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                nameChanged = true;
            }
        }

        public TextureTarget TextureTarget { get; private set; }

        public bool Created { get; private set; }
        private readonly OpenGLGraphicsDevice gd;
        private readonly bool needsTextureView;
        private uint textureView;
        private bool disposeRequested;
        private bool disposed;

        private string name;
        private bool nameChanged;

        public OpenGLTextureView(OpenGLGraphicsDevice gd, ref TextureViewDescription description)
            : base(ref description)
        {
            this.gd = gd;
            Target = Util.AssertSubtype<Texture, OpenGLTexture>(description.Target);

            if (BaseMipLevel != 0 || MipLevels != Target.MipLevels
                                  || BaseArrayLayer != 0 || ArrayLayers != Target.ArrayLayers
                                  || Format != Target.Format)
            {
                if (this.gd.BackendType == GraphicsBackend.OpenGL)
                {
                    if (!this.gd.Extensions.ArbTextureView)
                    {
                        throw new VeldridException(
                            "TextureView objects covering a subset of a Texture's dimensions or using a different PixelFormat " +
                            "require OpenGL 4.3, or ARB_texture_view.");
                    }
                }
                else
                {
                    if (!this.gd.Extensions.ArbTextureView)
                    {
                        throw new VeldridException(
                            "TextureView objects covering a subset of a Texture's dimensions or using a different PixelFormat are " +
                            "not supported on OpenGL ES.");
                    }
                }

                needsTextureView = true;
            }
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

        public SizedInternalFormat GetReadWriteSizedInternalFormat()
        {
            switch (Target.Format)
            {
                case PixelFormat.R8UNorm:
                    return SizedInternalFormat.R8;

                case PixelFormat.R8SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.R8Snorm;

                case PixelFormat.R8UInt:
                    return SizedInternalFormat.R8ui;

                case PixelFormat.R8SInt:
                    return SizedInternalFormat.R8i;

                case PixelFormat.R16UNorm:
                    return SizedInternalFormat.R16;

                case PixelFormat.R16SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.R16Snorm;

                case PixelFormat.R16UInt:
                    return SizedInternalFormat.R16ui;

                case PixelFormat.R16SInt:
                    return SizedInternalFormat.R16i;

                case PixelFormat.R16Float:
                    return SizedInternalFormat.R16f;

                case PixelFormat.R32UInt:
                    return SizedInternalFormat.R32ui;

                case PixelFormat.R32SInt:
                    return SizedInternalFormat.R32i;

                case PixelFormat.R32Float:
                    return SizedInternalFormat.R32f;

                case PixelFormat.R8G8UNorm:
                    return SizedInternalFormat.R8;

                case PixelFormat.R8G8SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.Rg8Snorm;

                case PixelFormat.R8G8UInt:
                    return SizedInternalFormat.Rg8ui;

                case PixelFormat.R8G8SInt:
                    return SizedInternalFormat.Rg8i;

                case PixelFormat.R16G16UNorm:
                    return SizedInternalFormat.R16;

                case PixelFormat.R16G16SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.Rg16Snorm;

                case PixelFormat.R16G16UInt:
                    return SizedInternalFormat.Rg16ui;

                case PixelFormat.R16G16SInt:
                    return SizedInternalFormat.Rg16i;

                case PixelFormat.R16G16Float:
                    return SizedInternalFormat.Rg16f;

                case PixelFormat.R32G32UInt:
                    return SizedInternalFormat.Rg32ui;

                case PixelFormat.R32G32SInt:
                    return SizedInternalFormat.Rg32i;

                case PixelFormat.R32G32Float:
                    return SizedInternalFormat.Rg32f;

                case PixelFormat.R8G8B8A8UNorm:
                case PixelFormat.B8G8R8A8UNorm:
                    return SizedInternalFormat.Rgba8;

                case PixelFormat.R8G8B8A8SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.Rgba8Snorm;

                case PixelFormat.R8G8B8A8UInt:
                    return SizedInternalFormat.Rgba8ui;

                case PixelFormat.R8G8B8A8SInt:
                    return SizedInternalFormat.Rgba16i;

                case PixelFormat.R16G16B16A16UNorm:
                    return SizedInternalFormat.Rgba16;

                case PixelFormat.R16G16B16A16SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.Rgba16Snorm;

                case PixelFormat.R16G16B16A16UInt:
                    return SizedInternalFormat.Rgba16ui;

                case PixelFormat.R16G16B16A16SInt:
                    return SizedInternalFormat.Rgba16i;

                case PixelFormat.R16G16B16A16Float:
                    return SizedInternalFormat.Rgba16f;

                case PixelFormat.R32G32B32A32UInt:
                    return SizedInternalFormat.Rgba32ui;

                case PixelFormat.R32G32B32A32SInt:
                    return SizedInternalFormat.Rgba32i;

                case PixelFormat.R32G32B32A32Float:
                    return SizedInternalFormat.Rgba32f;

                case PixelFormat.R10G10B10A2UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.Rgb10A2;

                case PixelFormat.R10G10B10A2UInt:
                    return (SizedInternalFormat)PixelInternalFormat.Rgb10A2ui;

                case PixelFormat.R11G11B10Float:
                    return (SizedInternalFormat)PixelInternalFormat.R11fG11fB10f;

                case PixelFormat.D24UNormS8UInt:
                case PixelFormat.D32FloatS8UInt:
                case PixelFormat.Bc1RgbUNorm:
                case PixelFormat.Bc1RgbaUNorm:
                case PixelFormat.Bc2UNorm:
                case PixelFormat.Bc3UNorm:
                case PixelFormat.Bc4UNorm:
                case PixelFormat.Bc4SNorm:
                case PixelFormat.Bc5UNorm:
                case PixelFormat.Bc5SNorm:
                case PixelFormat.Bc7UNorm:
                default:
                    throw Illegal.Value<PixelFormat>();
            }
        }

        public void EnsureResourcesCreated()
        {
            Target.EnsureResourcesCreated();

            if (!Created)
            {
                createGLResources();
                Created = true;
            }

            if (nameChanged && needsTextureView)
            {
                if (gd.Extensions.KhrDebug) SetObjectLabel(ObjectLabelIdentifier.Texture, textureView, name);
            }
        }

        public void DestroyGLResources()
        {
            if (!disposed)
            {
                disposed = true;

                if (textureView != 0)
                {
                    glDeleteTextures(1, ref textureView);
                    CheckLastError();
                }
            }
        }

        private void createGLResources()
        {
            if (!needsTextureView)
            {
                TextureTarget = Target.TextureTarget;
                return;
            }

            glGenTextures(1, out textureView);
            CheckLastError();

            var originalTarget = Target.TextureTarget;
            uint effectiveArrayLayers = ArrayLayers;

            if (originalTarget == TextureTarget.Texture1D)
                TextureTarget = TextureTarget.Texture1D;
            else if (originalTarget == TextureTarget.Texture1DArray)
            {
                if (ArrayLayers > 1)
                    TextureTarget = TextureTarget.Texture1DArray;
                else
                    TextureTarget = TextureTarget.Texture1D;
            }
            else if (originalTarget == TextureTarget.Texture2D)
                TextureTarget = TextureTarget.Texture2D;
            else if (originalTarget == TextureTarget.Texture2DArray)
            {
                if (ArrayLayers > 1)
                    TextureTarget = TextureTarget.Texture2DArray;
                else
                    TextureTarget = TextureTarget.Texture2D;
            }
            else if (originalTarget == TextureTarget.Texture2DMultisample)
                TextureTarget = TextureTarget.Texture2DMultisample;
            else if (originalTarget == TextureTarget.Texture2DMultisampleArray)
            {
                if (ArrayLayers > 1)
                    TextureTarget = TextureTarget.Texture2DMultisampleArray;
                else
                    TextureTarget = TextureTarget.Texture2DMultisample;
            }
            else if (originalTarget == TextureTarget.Texture3D)
                TextureTarget = TextureTarget.Texture3D;
            else if (originalTarget == TextureTarget.TextureCubeMap)
            {
                if (ArrayLayers > 1)
                {
                    TextureTarget = TextureTarget.TextureCubeMap;
                    effectiveArrayLayers *= 6;
                }
                else
                {
                    TextureTarget = TextureTarget.TextureCubeMapArray;
                    effectiveArrayLayers *= 6;
                }
            }
            else
                throw new VeldridException("The given TextureView parameters are not supported with the OpenGL backend.");

            var internalFormat = (PixelInternalFormat)OpenGLFormats.VdToGLSizedInternalFormat(
                Format,
                (Target.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);
            Debug.Assert(Target.Created);
            glTextureView(
                textureView,
                TextureTarget,
                Target.Texture,
                internalFormat,
                BaseMipLevel,
                MipLevels,
                BaseArrayLayer,
                effectiveArrayLayers);
            CheckLastError();
        }
    }
}
