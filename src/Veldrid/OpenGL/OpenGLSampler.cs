using Veldrid.OpenGLBinding;
using static Veldrid.OpenGLBinding.OpenGLNative;
using static Veldrid.OpenGL.OpenGLUtil;

namespace Veldrid.OpenGL
{
    internal unsafe class OpenGLSampler : Sampler, IOpenGLDeferredResource
    {
        public override bool IsDisposed => disposeRequested;

        public uint NoMipmapSampler => noMipmapState.Sampler;
        public uint MipmapSampler => mipmapState.Sampler;

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
        private readonly SamplerDescription description;
        private readonly InternalSamplerState noMipmapState;
        private readonly InternalSamplerState mipmapState;
        private bool disposeRequested;

        private string name;
        private bool nameChanged;

        public OpenGLSampler(OpenGLGraphicsDevice gd, ref SamplerDescription description)
        {
            this.gd = gd;
            this.description = description;

            mipmapState = new InternalSamplerState();
            noMipmapState = new InternalSamplerState();
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
            if (!Created) createGLResources();

            if (nameChanged)
            {
                nameChanged = false;

                if (gd.Extensions.KhrDebug)
                {
                    SetObjectLabel(ObjectLabelIdentifier.Sampler, noMipmapState.Sampler, string.Format("{0}_WithoutMipmapping", name));
                    SetObjectLabel(ObjectLabelIdentifier.Sampler, mipmapState.Sampler, string.Format("{0}_WithMipmapping", name));
                }
            }
        }

        public void DestroyGLResources()
        {
            mipmapState.DestroyGLResources();
            noMipmapState.DestroyGLResources();
        }

        private void createGLResources()
        {
            var backendType = gd.BackendType;
            noMipmapState.CreateGLResources(description, false, backendType);
            mipmapState.CreateGLResources(description, true, backendType);
            Created = true;
        }

        private class InternalSamplerState
        {
            public uint Sampler => sampler;
            private uint sampler;

            public void CreateGLResources(SamplerDescription description, bool mipmapped, GraphicsBackend backend)
            {
                glGenSamplers(1, out sampler);
                CheckLastError();

                glSamplerParameteri(sampler, SamplerParameterName.TextureWrapS, (int)OpenGLFormats.VdToGLTextureWrapMode(description.AddressModeU));
                CheckLastError();
                glSamplerParameteri(sampler, SamplerParameterName.TextureWrapT, (int)OpenGLFormats.VdToGLTextureWrapMode(description.AddressModeV));
                CheckLastError();
                glSamplerParameteri(sampler, SamplerParameterName.TextureWrapR, (int)OpenGLFormats.VdToGLTextureWrapMode(description.AddressModeW));
                CheckLastError();

                if (description.AddressModeU == SamplerAddressMode.Border
                    || description.AddressModeV == SamplerAddressMode.Border
                    || description.AddressModeW == SamplerAddressMode.Border)
                {
                    var borderColor = toRgbaFloat(description.BorderColor);
                    glSamplerParameterfv(sampler, SamplerParameterName.TextureBorderColor, (float*)&borderColor);
                    CheckLastError();
                }

                glSamplerParameterf(sampler, SamplerParameterName.TextureMinLod, description.MinimumLod);
                CheckLastError();
                glSamplerParameterf(sampler, SamplerParameterName.TextureMaxLod, description.MaximumLod);
                CheckLastError();

                if (backend == GraphicsBackend.OpenGL && description.LodBias != 0)
                {
                    glSamplerParameterf(sampler, SamplerParameterName.TextureLodBias, description.LodBias);
                    CheckLastError();
                }

                if (description.Filter == SamplerFilter.Anisotropic)
                {
                    glSamplerParameterf(sampler, SamplerParameterName.TextureMaxAnisotropyExt, description.MaximumAnisotropy);
                    CheckLastError();
                    glSamplerParameteri(sampler, SamplerParameterName.TextureMinFilter, mipmapped ? (int)TextureMinFilter.LinearMipmapLinear : (int)TextureMinFilter.Linear);
                    CheckLastError();
                    glSamplerParameteri(sampler, SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    CheckLastError();
                }
                else
                {
                    OpenGLFormats.VdToGLTextureMinMagFilter(description.Filter, mipmapped, out var min, out var mag);
                    glSamplerParameteri(sampler, SamplerParameterName.TextureMinFilter, (int)min);
                    CheckLastError();
                    glSamplerParameteri(sampler, SamplerParameterName.TextureMagFilter, (int)mag);
                    CheckLastError();
                }

                if (description.ComparisonKind != null)
                {
                    glSamplerParameteri(sampler, SamplerParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
                    CheckLastError();
                    glSamplerParameteri(sampler, SamplerParameterName.TextureCompareFunc, (int)OpenGLFormats.VdToGLDepthFunction(description.ComparisonKind.Value));
                    CheckLastError();
                }
            }

            public void DestroyGLResources()
            {
                glDeleteSamplers(1, ref sampler);
                CheckLastError();
            }

            private RgbaFloat toRgbaFloat(SamplerBorderColor borderColor)
            {
                switch (borderColor)
                {
                    case SamplerBorderColor.TransparentBlack:
                        return new RgbaFloat(0, 0, 0, 0);

                    case SamplerBorderColor.OpaqueBlack:
                        return new RgbaFloat(0, 0, 0, 1);

                    case SamplerBorderColor.OpaqueWhite:
                        return new RgbaFloat(1, 1, 1, 1);

                    default:
                        throw Illegal.Value<SamplerBorderColor>();
                }
            }
        }
    }
}
