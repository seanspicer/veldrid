using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Veldrid.OpenGLBinding;
using static Veldrid.OpenGLBinding.OpenGLNative;
using static Veldrid.OpenGL.OpenGLUtil;

namespace Veldrid.OpenGL
{
    internal unsafe class OpenGLPipeline : Pipeline, IOpenGLDeferredResource
    {
        private const uint gl_invalid_index = 0xFFFFFFFF;
        private readonly OpenGLGraphicsDevice gd;

#if !VALIDATE_USAGE
        public ResourceLayout[] ResourceLayouts { get; }
#endif

        // Graphics Pipeline
        public Shader[] GraphicsShaders { get; }
        public VertexLayoutDescription[] VertexLayouts { get; }
        public BlendStateDescription BlendState { get; }
        public DepthStencilStateDescription DepthStencilState { get; }
        public RasterizerStateDescription RasterizerState { get; }
        public PrimitiveTopology PrimitiveTopology { get; }

        // Compute Pipeline
        public override bool IsComputePipeline { get; }
        public Shader ComputeShader { get; }

        private bool disposeRequested;
        private bool disposed;

        private SetBindingsInfo[] setInfos;

        public int[] VertexStrides { get; }

        public uint Program { get; private set; }

        public uint GetUniformBufferCount(uint setSlot)
        {
            return setInfos[setSlot].UniformBufferCount;
        }

        public uint GetShaderStorageBufferCount(uint setSlot)
        {
            return setInfos[setSlot].ShaderStorageBufferCount;
        }

        public override string Name { get; set; }

        public override bool IsDisposed => disposeRequested;

        public OpenGLPipeline(OpenGLGraphicsDevice gd, ref GraphicsPipelineDescription description)
            : base(ref description)
        {
            this.gd = gd;
            GraphicsShaders = Util.ShallowClone(description.ShaderSet.Shaders);
            VertexLayouts = Util.ShallowClone(description.ShaderSet.VertexLayouts);
            BlendState = description.BlendState.ShallowClone();
            DepthStencilState = description.DepthStencilState;
            RasterizerState = description.RasterizerState;
            PrimitiveTopology = description.PrimitiveTopology;

            int numVertexBuffers = description.ShaderSet.VertexLayouts.Length;
            VertexStrides = new int[numVertexBuffers];
            for (int i = 0; i < numVertexBuffers; i++) VertexStrides[i] = (int)description.ShaderSet.VertexLayouts[i].Stride;

#if !VALIDATE_USAGE
            ResourceLayouts = Util.ShallowClone(description.ResourceLayouts);
#endif
        }

        public OpenGLPipeline(OpenGLGraphicsDevice gd, ref ComputePipelineDescription description)
            : base(ref description)
        {
            this.gd = gd;
            IsComputePipeline = true;
            ComputeShader = description.ComputeShader;
            VertexStrides = Array.Empty<int>();
#if !VALIDATE_USAGE
            ResourceLayouts = Util.ShallowClone(description.ResourceLayouts);
#endif
        }

        public bool Created { get; private set; }

        public void EnsureResourcesCreated()
        {
            if (!Created) createGLResources();
        }

        private void createGLResources()
        {
            if (!IsComputePipeline)
                createGraphicsGLResources();
            else
                createComputeGLResources();

            Created = true;
        }

        private void createGraphicsGLResources()
        {
            Program = glCreateProgram();
            CheckLastError();

            foreach (var stage in GraphicsShaders)
            {
                var glShader = Util.AssertSubtype<Shader, OpenGLShader>(stage);
                glShader.EnsureResourcesCreated();
                glAttachShader(Program, glShader.Shader);
                CheckLastError();
            }

            uint slot = 0;

            foreach (var layoutDesc in VertexLayouts)
            {
                for (int i = 0; i < layoutDesc.Elements.Length; i++)
                {
                    bindAttribLocation(slot, layoutDesc.Elements[i].Name);
                    slot += 1;
                }
            }

            glLinkProgram(Program);
            CheckLastError();

#if DEBUG && GL_VALIDATE_VERTEX_INPUT_ELEMENTS
            slot = 0;
            foreach (VertexLayoutDescription layoutDesc in VertexLayouts)
            {
                for (int i = 0; i < layoutDesc.Elements.Length; i++)
                {
                    int location = GetAttribLocation(layoutDesc.Elements[i].Name);
                    if (location == -1)
                    {
                        throw new VeldridException("There was no attribute variable with the name " + layoutDesc.Elements[i].Name);
                    }

                    slot += 1;
                }
            }
#endif

            int linkStatus;
            glGetProgramiv(Program, GetProgramParameterName.LinkStatus, &linkStatus);
            CheckLastError();

            if (linkStatus != 1)
            {
                byte* infoLog = stackalloc byte[4096];
                uint bytesWritten;
                glGetProgramInfoLog(Program, 4096, &bytesWritten, infoLog);
                CheckLastError();
                string log = Encoding.UTF8.GetString(infoLog, (int)bytesWritten);
                throw new VeldridException($"Error linking GL program: {log}");
            }

            processResourceSetLayouts(ResourceLayouts);
        }

        private int getAttribLocation(string elementName)
        {
            int byteCount = Encoding.UTF8.GetByteCount(elementName) + 1;
            byte* elementNamePtr = stackalloc byte[byteCount];

            fixed (char* charPtr = elementName)
            {
                int bytesWritten = Encoding.UTF8.GetBytes(charPtr, elementName.Length, elementNamePtr, byteCount);
                Debug.Assert(bytesWritten == byteCount - 1);
            }

            elementNamePtr[byteCount - 1] = 0; // Add null terminator.

            int location = glGetAttribLocation(Program, elementNamePtr);
            return location;
        }

        private void bindAttribLocation(uint slot, string elementName)
        {
            int byteCount = Encoding.UTF8.GetByteCount(elementName) + 1;
            byte* elementNamePtr = stackalloc byte[byteCount];

            fixed (char* charPtr = elementName)
            {
                int bytesWritten = Encoding.UTF8.GetBytes(charPtr, elementName.Length, elementNamePtr, byteCount);
                Debug.Assert(bytesWritten == byteCount - 1);
            }

            elementNamePtr[byteCount - 1] = 0; // Add null terminator.

            glBindAttribLocation(Program, slot, elementNamePtr);
            CheckLastError();
        }

        private void processResourceSetLayouts(ResourceLayout[] layouts)
        {
            int resourceLayoutCount = layouts.Length;
            setInfos = new SetBindingsInfo[resourceLayoutCount];
            int lastTextureLocation = -1;
            int relativeTextureIndex = -1;
            int relativeImageIndex = -1;
            uint storageBlockIndex = 0; // Tracks OpenGL ES storage buffers.

            for (uint setSlot = 0; setSlot < resourceLayoutCount; setSlot++)
            {
                var setLayout = layouts[setSlot];
                var glSetLayout = Util.AssertSubtype<ResourceLayout, OpenGLResourceLayout>(setLayout);
                var resources = glSetLayout.Elements;

                var uniformBindings = new Dictionary<uint, OpenGLUniformBinding>();
                var textureBindings = new Dictionary<uint, OpenGLTextureBindingSlotInfo>();
                var samplerBindings = new Dictionary<uint, OpenGLSamplerBindingSlotInfo>();
                var storageBufferBindings = new Dictionary<uint, OpenGLShaderStorageBinding>();

                var samplerTrackedRelativeTextureIndices = new List<int>();

                for (uint i = 0; i < resources.Length; i++)
                {
                    var resource = resources[i];

                    if (resource.Kind == ResourceKind.UniformBuffer)
                    {
                        uint blockIndex = getUniformBlockIndex(resource.Name);

                        if (blockIndex != gl_invalid_index)
                        {
                            int blockSize;
                            glGetActiveUniformBlockiv(Program, blockIndex, ActiveUniformBlockParameter.UniformBlockDataSize, &blockSize);
                            CheckLastError();
                            uniformBindings[i] = new OpenGLUniformBinding(Program, blockIndex, (uint)blockSize);
                        }
                    }
                    else if (resource.Kind == ResourceKind.TextureReadOnly)
                    {
                        int location = getUniformLocation(resource.Name);
                        relativeTextureIndex += 1;
                        textureBindings[i] = new OpenGLTextureBindingSlotInfo { RelativeIndex = relativeTextureIndex, UniformLocation = location };
                        lastTextureLocation = location;
                        samplerTrackedRelativeTextureIndices.Add(relativeTextureIndex);
                    }
                    else if (resource.Kind == ResourceKind.TextureReadWrite)
                    {
                        int location = getUniformLocation(resource.Name);
                        relativeImageIndex += 1;
                        textureBindings[i] = new OpenGLTextureBindingSlotInfo { RelativeIndex = relativeImageIndex, UniformLocation = location };
                    }
                    else if (resource.Kind == ResourceKind.StructuredBufferReadOnly
                             || resource.Kind == ResourceKind.StructuredBufferReadWrite)
                    {
                        uint storageBlockBinding;

                        if (gd.BackendType == GraphicsBackend.OpenGL)
                            storageBlockBinding = getProgramResourceIndex(resource.Name, ProgramInterface.ShaderStorageBlock);
                        else
                        {
                            storageBlockBinding = storageBlockIndex;
                            storageBlockIndex += 1;
                        }

                        storageBufferBindings[i] = new OpenGLShaderStorageBinding(storageBlockBinding);
                    }
                    else
                    {
                        Debug.Assert(resource.Kind == ResourceKind.Sampler);

                        int[] relativeIndices = samplerTrackedRelativeTextureIndices.ToArray();
                        samplerTrackedRelativeTextureIndices.Clear();
                        samplerBindings[i] = new OpenGLSamplerBindingSlotInfo
                        {
                            RelativeIndices = relativeIndices
                        };
                    }
                }

                setInfos[setSlot] = new SetBindingsInfo(uniformBindings, textureBindings, samplerBindings, storageBufferBindings);
            }
        }

        private uint getUniformBlockIndex(string resourceName)
        {
            int byteCount = Encoding.UTF8.GetByteCount(resourceName) + 1;
            byte* resourceNamePtr = stackalloc byte[byteCount];

            fixed (char* charPtr = resourceName)
            {
                int bytesWritten = Encoding.UTF8.GetBytes(charPtr, resourceName.Length, resourceNamePtr, byteCount);
                Debug.Assert(bytesWritten == byteCount - 1);
            }

            resourceNamePtr[byteCount - 1] = 0; // Add null terminator.

            uint blockIndex = glGetUniformBlockIndex(Program, resourceNamePtr);
            CheckLastError();
#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
            if (blockIndex == GL_INVALID_INDEX)
            {
                uint uniformBufferIndex = 0;
                uint bufferNameByteCount = 64;
                byte* bufferNamePtr = stackalloc byte[(int)bufferNameByteCount];
                var names = new List<string>();
                while (true)
                {
                    uint actualLength;
                    glGetActiveUniformBlockName(_program, uniformBufferIndex, bufferNameByteCount, &actualLength, bufferNamePtr);

                    if (glGetError() != 0)
                    {
                        break;
                    }

                    string name = Encoding.UTF8.GetString(bufferNamePtr, (int)actualLength);
                    names.Add(name);
                    uniformBufferIndex++;
                }

                throw new VeldridException($"Unable to bind uniform buffer \"{resourceName}\" by name. Valid names for this pipeline are: {string.Join(", ", names)}");
            }
#endif
            return blockIndex;
        }

        private int getUniformLocation(string resourceName)
        {
            int byteCount = Encoding.UTF8.GetByteCount(resourceName) + 1;
            byte* resourceNamePtr = stackalloc byte[byteCount];

            fixed (char* charPtr = resourceName)
            {
                int bytesWritten = Encoding.UTF8.GetBytes(charPtr, resourceName.Length, resourceNamePtr, byteCount);
                Debug.Assert(bytesWritten == byteCount - 1);
            }

            resourceNamePtr[byteCount - 1] = 0; // Add null terminator.

            int location = glGetUniformLocation(Program, resourceNamePtr);
            CheckLastError();

#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
            if (location == -1)
            {
                ReportInvalidUniformName(resourceName);
            }
#endif
            return location;
        }

        private uint getProgramResourceIndex(string resourceName, ProgramInterface resourceType)
        {
            int byteCount = Encoding.UTF8.GetByteCount(resourceName) + 1;

            byte* resourceNamePtr = stackalloc byte[byteCount];

            fixed (char* charPtr = resourceName)
            {
                int bytesWritten = Encoding.UTF8.GetBytes(charPtr, resourceName.Length, resourceNamePtr, byteCount);
                Debug.Assert(bytesWritten == byteCount - 1);
            }

            resourceNamePtr[byteCount - 1] = 0; // Add null terminator.

            uint binding = glGetProgramResourceIndex(Program, resourceType, resourceNamePtr);
            CheckLastError();
#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
            if (binding == GL_INVALID_INDEX)
            {
                ReportInvalidResourceName(resourceName, resourceType);
            }
#endif
            return binding;
        }

#if DEBUG && GL_VALIDATE_SHADER_RESOURCE_NAMES
        void ReportInvalidUniformName(string uniformName)
        {
            uint uniformIndex = 0;
            uint resourceNameByteCount = 64;
            byte* resourceNamePtr = stackalloc byte[(int)resourceNameByteCount];

            var names = new List<string>();
            while (true)
            {
                uint actualLength;
                int size;
                uint type;
                glGetActiveUniform(_program, uniformIndex, resourceNameByteCount,
                    &actualLength, &size, &type, resourceNamePtr);

                if (glGetError() != 0)
                {
                    break;
                }

                string name = Encoding.UTF8.GetString(resourceNamePtr, (int)actualLength);
                names.Add(name);
                uniformIndex++;
            }

            throw new VeldridException($"Unable to bind uniform \"{uniformName}\" by name. Valid names for this pipeline are: {string.Join(", ", names)}");
        }

        void ReportInvalidResourceName(string resourceName, ProgramInterface resourceType)
        {
            // glGetProgramInterfaceiv and glGetProgramResourceName are only available in 4.3+
            if (_gd.ApiVersion.Major < 4 || (_gd.ApiVersion.Major == 4 && _gd.ApiVersion.Minor < 3))
            {
                return;
            }

            int maxLength = 0;
            int resourceCount = 0;
            glGetProgramInterfaceiv(_program, resourceType, ProgramInterfaceParameterName.MaxNameLength, &maxLength);
            glGetProgramInterfaceiv(_program, resourceType, ProgramInterfaceParameterName.ActiveResources, &resourceCount);
            byte* resourceNamePtr = stackalloc byte[maxLength];

            var names = new List<string>();
            for (uint resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
            {
                uint actualLength;
                glGetProgramResourceName(_program, resourceType, resourceIndex, (uint)maxLength, &actualLength, resourceNamePtr);

                if (glGetError() != 0)
                {
                    break;
                }

                string name = Encoding.UTF8.GetString(resourceNamePtr, (int)actualLength);
                names.Add(name);
            }

            throw new VeldridException($"Unable to bind {resourceType} \"{resourceName}\" by name. Valid names for this pipeline are: {string.Join(", ", names)}");
        }
#endif

        private void createComputeGLResources()
        {
            Program = glCreateProgram();
            CheckLastError();
            var glShader = Util.AssertSubtype<Shader, OpenGLShader>(ComputeShader);
            glShader.EnsureResourcesCreated();
            glAttachShader(Program, glShader.Shader);
            CheckLastError();

            glLinkProgram(Program);
            CheckLastError();

            int linkStatus;
            glGetProgramiv(Program, GetProgramParameterName.LinkStatus, &linkStatus);
            CheckLastError();

            if (linkStatus != 1)
            {
                byte* infoLog = stackalloc byte[4096];
                uint bytesWritten;
                glGetProgramInfoLog(Program, 4096, &bytesWritten, infoLog);
                CheckLastError();
                string log = Encoding.UTF8.GetString(infoLog, (int)bytesWritten);
                throw new VeldridException($"Error linking GL program: {log}");
            }

            processResourceSetLayouts(ResourceLayouts);
        }

        public bool GetUniformBindingForSlot(uint set, uint slot, out OpenGLUniformBinding binding)
        {
            Debug.Assert(setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
            var setInfo = setInfos[set];
            return setInfo.GetUniformBindingForSlot(slot, out binding);
        }

        public bool GetTextureBindingInfo(uint set, uint slot, out OpenGLTextureBindingSlotInfo binding)
        {
            Debug.Assert(setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
            var setInfo = setInfos[set];
            return setInfo.GetTextureBindingInfo(slot, out binding);
        }

        public bool GetSamplerBindingInfo(uint set, uint slot, out OpenGLSamplerBindingSlotInfo binding)
        {
            Debug.Assert(setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
            var setInfo = setInfos[set];
            return setInfo.GetSamplerBindingInfo(slot, out binding);
        }

        public bool GetStorageBufferBindingForSlot(uint set, uint slot, out OpenGLShaderStorageBinding binding)
        {
            Debug.Assert(setInfos != null, "EnsureResourcesCreated must be called before accessing resource set information.");
            var setInfo = setInfos[set];
            return setInfo.GetStorageBufferBindingForSlot(slot, out binding);
        }

        public override void Dispose()
        {
            if (!disposeRequested)
            {
                disposeRequested = true;
                gd.EnqueueDisposal(this);
            }
        }

        public void DestroyGLResources()
        {
            if (!disposed)
            {
                disposed = true;
                glDeleteProgram(Program);
                CheckLastError();
            }
        }
    }

    internal struct SetBindingsInfo
    {
        private readonly Dictionary<uint, OpenGLUniformBinding> uniformBindings;
        private readonly Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings;
        private readonly Dictionary<uint, OpenGLSamplerBindingSlotInfo> samplerBindings;
        private readonly Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings;

        public uint UniformBufferCount { get; }
        public uint ShaderStorageBufferCount { get; }

        public SetBindingsInfo(
            Dictionary<uint, OpenGLUniformBinding> uniformBindings,
            Dictionary<uint, OpenGLTextureBindingSlotInfo> textureBindings,
            Dictionary<uint, OpenGLSamplerBindingSlotInfo> samplerBindings,
            Dictionary<uint, OpenGLShaderStorageBinding> storageBufferBindings)
        {
            this.uniformBindings = uniformBindings;
            UniformBufferCount = (uint)uniformBindings.Count;
            this.textureBindings = textureBindings;
            this.samplerBindings = samplerBindings;
            this.storageBufferBindings = storageBufferBindings;
            ShaderStorageBufferCount = (uint)storageBufferBindings.Count;
        }

        public bool GetTextureBindingInfo(uint slot, out OpenGLTextureBindingSlotInfo binding)
        {
            return textureBindings.TryGetValue(slot, out binding);
        }

        public bool GetSamplerBindingInfo(uint slot, out OpenGLSamplerBindingSlotInfo binding)
        {
            return samplerBindings.TryGetValue(slot, out binding);
        }

        public bool GetUniformBindingForSlot(uint slot, out OpenGLUniformBinding binding)
        {
            return uniformBindings.TryGetValue(slot, out binding);
        }

        public bool GetStorageBufferBindingForSlot(uint slot, out OpenGLShaderStorageBinding binding)
        {
            return storageBufferBindings.TryGetValue(slot, out binding);
        }
    }

    internal struct OpenGLTextureBindingSlotInfo
    {
        /// <summary>
        ///     The relative index of this binding with relation to the other textures used by a shader.
        ///     Generally, this is the texture unit that the binding will be placed into.
        /// </summary>
        public int RelativeIndex;

        /// <summary>
        ///     The uniform location of the binding in the shader program.
        /// </summary>
        public int UniformLocation;
    }

    internal struct OpenGLSamplerBindingSlotInfo
    {
        /// <summary>
        ///     The relative indices of this binding with relation to the other textures used by a shader.
        ///     Generally, these are the texture units that the sampler will be bound to.
        /// </summary>
        public int[] RelativeIndices;
    }

    internal class OpenGLUniformBinding
    {
        public uint Program { get; }
        public uint BlockLocation { get; }
        public uint BlockSize { get; }

        public OpenGLUniformBinding(uint program, uint blockLocation, uint blockSize)
        {
            Program = program;
            BlockLocation = blockLocation;
            BlockSize = blockSize;
        }
    }

    internal class OpenGLShaderStorageBinding
    {
        public uint StorageBlockBinding { get; }

        public OpenGLShaderStorageBinding(uint storageBlockBinding)
        {
            StorageBlockBinding = storageBlockBinding;
        }
    }
}
