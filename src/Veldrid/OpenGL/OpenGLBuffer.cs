using System.Diagnostics;
using Veldrid.OpenGLBinding;
using static Veldrid.OpenGLBinding.OpenGLNative;
using static Veldrid.OpenGL.OpenGLUtil;

namespace Veldrid.OpenGL
{
    internal unsafe class OpenGLBuffer : DeviceBuffer, OpenGLDeferredResource
    {
        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }

        public uint Buffer => _buffer;

        public override bool IsDisposed => _disposeRequested;

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _nameChanged = true;
            }
        }

        public bool Created { get; private set; }
        private readonly OpenGLGraphicsDevice _gd;
        private uint _buffer;
        private readonly bool _dynamic;
        private bool _disposeRequested;

        private string _name;
        private bool _nameChanged;

        public OpenGLBuffer(OpenGLGraphicsDevice gd, uint sizeInBytes, BufferUsage usage)
        {
            _gd = gd;
            SizeInBytes = sizeInBytes;
            _dynamic = (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;
            Usage = usage;
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposeRequested)
            {
                _disposeRequested = true;
                _gd.EnqueueDisposal(this);
            }
        }

        #endregion

        public void EnsureResourcesCreated()
        {
            if (!Created) CreateGLResources();

            if (_nameChanged)
            {
                _nameChanged = false;
                if (_gd.Extensions.KHR_Debug) SetObjectLabel(ObjectLabelIdentifier.Buffer, _buffer, _name);
            }
        }

        public void CreateGLResources()
        {
            Debug.Assert(!Created);

            if (_gd.Extensions.ARB_DirectStateAccess)
            {
                uint buffer;
                glCreateBuffers(1, &buffer);
                CheckLastError();
                _buffer = buffer;

                glNamedBufferData(
                    _buffer,
                    SizeInBytes,
                    null,
                    _dynamic ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw);
                CheckLastError();
            }
            else
            {
                glGenBuffers(1, out _buffer);
                CheckLastError();

                glBindBuffer(BufferTarget.CopyReadBuffer, _buffer);
                CheckLastError();

                glBufferData(
                    BufferTarget.CopyReadBuffer,
                    SizeInBytes,
                    null,
                    _dynamic ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw);
                CheckLastError();
            }

            Created = true;
        }

        public void DestroyGLResources()
        {
            uint buffer = _buffer;
            glDeleteBuffers(1, ref buffer);
            CheckLastError();
        }
    }
}
