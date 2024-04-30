using System.Runtime.CompilerServices;
using Vulkan;
using static Vulkan.VulkanNative;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkPipeline : Pipeline
    {
        public Vulkan.VkPipeline DevicePipeline => _devicePipeline;

        public VkPipelineLayout PipelineLayout => _pipelineLayout;

        public uint ResourceSetCount { get; }
        public int DynamicOffsetsCount { get; }
        public bool ScissorTestEnabled { get; }

        public override bool IsComputePipeline { get; }

        public ResourceRefCount RefCount { get; }

        public override bool IsDisposed => _destroyed;

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                _gd.SetResourceName(this, value);
            }
        }

        private readonly VkGraphicsDevice _gd;
        private readonly Vulkan.VkPipeline _devicePipeline;
        private readonly VkPipelineLayout _pipelineLayout;
        private readonly VkRenderPass _renderPass;
        private bool _destroyed;
        private string _name;

        public VkPipeline(VkGraphicsDevice gd, ref GraphicsPipelineDescription description)
            : base(ref description)
        {
            _gd = gd;
            IsComputePipeline = false;
            RefCount = new ResourceRefCount(DisposeCore);

            var pipelineCI = VkGraphicsPipelineCreateInfo.New();

            // Blend State
            var blendStateCI = VkPipelineColorBlendStateCreateInfo.New();
            int attachmentsCount = description.BlendState.AttachmentStates.Length;
            var attachmentsPtr
                = stackalloc VkPipelineColorBlendAttachmentState[attachmentsCount];

            for (int i = 0; i < attachmentsCount; i++)
            {
                var vdDesc = description.BlendState.AttachmentStates[i];
                var attachmentState = new VkPipelineColorBlendAttachmentState();
                attachmentState.srcColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceColorFactor);
                attachmentState.dstColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationColorFactor);
                attachmentState.colorBlendOp = VkFormats.VdToVkBlendOp(vdDesc.ColorFunction);
                attachmentState.srcAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceAlphaFactor);
                attachmentState.dstAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationAlphaFactor);
                attachmentState.alphaBlendOp = VkFormats.VdToVkBlendOp(vdDesc.AlphaFunction);
                attachmentState.colorWriteMask = VkFormats.VdToVkColorWriteMask(vdDesc.ColorWriteMask.GetOrDefault());
                attachmentState.blendEnable = vdDesc.BlendEnabled;
                attachmentsPtr[i] = attachmentState;
            }

            blendStateCI.attachmentCount = (uint)attachmentsCount;
            blendStateCI.pAttachments = attachmentsPtr;
            var blendFactor = description.BlendState.BlendFactor;
            blendStateCI.blendConstants_0 = blendFactor.R;
            blendStateCI.blendConstants_1 = blendFactor.G;
            blendStateCI.blendConstants_2 = blendFactor.B;
            blendStateCI.blendConstants_3 = blendFactor.A;

            pipelineCI.pColorBlendState = &blendStateCI;

            // Rasterizer State
            var rsDesc = description.RasterizerState;
            var rsCI = VkPipelineRasterizationStateCreateInfo.New();
            rsCI.cullMode = VkFormats.VdToVkCullMode(rsDesc.CullMode);
            rsCI.polygonMode = VkFormats.VdToVkPolygonMode(rsDesc.FillMode);
            rsCI.depthClampEnable = !rsDesc.DepthClipEnabled;
            rsCI.frontFace = rsDesc.FrontFace == FrontFace.Clockwise ? VkFrontFace.Clockwise : VkFrontFace.CounterClockwise;
            rsCI.lineWidth = 1f;

            pipelineCI.pRasterizationState = &rsCI;

            ScissorTestEnabled = rsDesc.ScissorTestEnabled;

            // Dynamic State
            var dynamicStateCI = VkPipelineDynamicStateCreateInfo.New();
            var dynamicStates = stackalloc VkDynamicState[2];
            dynamicStates[0] = VkDynamicState.Viewport;
            dynamicStates[1] = VkDynamicState.Scissor;
            dynamicStateCI.dynamicStateCount = 2;
            dynamicStateCI.pDynamicStates = dynamicStates;

            pipelineCI.pDynamicState = &dynamicStateCI;

            // Depth Stencil State
            var vdDssDesc = description.DepthStencilState;
            var dssCI = VkPipelineDepthStencilStateCreateInfo.New();
            dssCI.depthWriteEnable = vdDssDesc.DepthWriteEnabled;
            dssCI.depthTestEnable = vdDssDesc.DepthTestEnabled;
            dssCI.depthCompareOp = VkFormats.VdToVkCompareOp(vdDssDesc.DepthComparison);
            dssCI.stencilTestEnable = vdDssDesc.StencilTestEnabled;

            dssCI.front.failOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Fail);
            dssCI.front.passOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Pass);
            dssCI.front.depthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.DepthFail);
            dssCI.front.compareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilFront.Comparison);
            dssCI.front.compareMask = vdDssDesc.StencilReadMask;
            dssCI.front.writeMask = vdDssDesc.StencilWriteMask;
            dssCI.front.reference = vdDssDesc.StencilReference;

            dssCI.back.failOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Fail);
            dssCI.back.passOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Pass);
            dssCI.back.depthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.DepthFail);
            dssCI.back.compareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilBack.Comparison);
            dssCI.back.compareMask = vdDssDesc.StencilReadMask;
            dssCI.back.writeMask = vdDssDesc.StencilWriteMask;
            dssCI.back.reference = vdDssDesc.StencilReference;

            pipelineCI.pDepthStencilState = &dssCI;

            // Multisample
            var multisampleCI = VkPipelineMultisampleStateCreateInfo.New();
            var vkSampleCount = VkFormats.VdToVkSampleCount(description.Outputs.SampleCount);
            multisampleCI.rasterizationSamples = vkSampleCount;
            multisampleCI.alphaToCoverageEnable = description.BlendState.AlphaToCoverageEnabled;

            pipelineCI.pMultisampleState = &multisampleCI;

            // Input Assembly
            var inputAssemblyCI = VkPipelineInputAssemblyStateCreateInfo.New();
            inputAssemblyCI.topology = VkFormats.VdToVkPrimitiveTopology(description.PrimitiveTopology);

            pipelineCI.pInputAssemblyState = &inputAssemblyCI;

            // Vertex Input State
            var vertexInputCI = VkPipelineVertexInputStateCreateInfo.New();

            var inputDescriptions = description.ShaderSet.VertexLayouts;
            uint bindingCount = (uint)inputDescriptions.Length;
            uint attributeCount = 0;
            for (int i = 0; i < inputDescriptions.Length; i++) attributeCount += (uint)inputDescriptions[i].Elements.Length;
            var bindingDescs = stackalloc VkVertexInputBindingDescription[(int)bindingCount];
            var attributeDescs = stackalloc VkVertexInputAttributeDescription[(int)attributeCount];

            int targetIndex = 0;
            int targetLocation = 0;

            for (int binding = 0; binding < inputDescriptions.Length; binding++)
            {
                var inputDesc = inputDescriptions[binding];
                bindingDescs[binding] = new VkVertexInputBindingDescription
                {
                    binding = (uint)binding,
                    inputRate = inputDesc.InstanceStepRate != 0 ? VkVertexInputRate.Instance : VkVertexInputRate.Vertex,
                    stride = inputDesc.Stride
                };

                uint currentOffset = 0;

                for (int location = 0; location < inputDesc.Elements.Length; location++)
                {
                    var inputElement = inputDesc.Elements[location];

                    attributeDescs[targetIndex] = new VkVertexInputAttributeDescription
                    {
                        format = VkFormats.VdToVkVertexElementFormat(inputElement.Format),
                        binding = (uint)binding,
                        location = (uint)(targetLocation + location),
                        offset = inputElement.Offset != 0 ? inputElement.Offset : currentOffset
                    };

                    targetIndex += 1;
                    currentOffset += FormatSizeHelpers.GetSizeInBytes(inputElement.Format);
                }

                targetLocation += inputDesc.Elements.Length;
            }

            vertexInputCI.vertexBindingDescriptionCount = bindingCount;
            vertexInputCI.pVertexBindingDescriptions = bindingDescs;
            vertexInputCI.vertexAttributeDescriptionCount = attributeCount;
            vertexInputCI.pVertexAttributeDescriptions = attributeDescs;

            pipelineCI.pVertexInputState = &vertexInputCI;

            // Shader Stage

            VkSpecializationInfo specializationInfo;
            var specDescs = description.ShaderSet.Specializations;

            if (specDescs != null)
            {
                uint specDataSize = 0;
                foreach (var spec in specDescs) specDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
                byte* fullSpecData = stackalloc byte[(int)specDataSize];
                int specializationCount = specDescs.Length;
                var mapEntries = stackalloc VkSpecializationMapEntry[specializationCount];
                uint specOffset = 0;

                for (int i = 0; i < specializationCount; i++)
                {
                    ulong data = specDescs[i].Data;
                    byte* srcData = (byte*)&data;
                    uint dataSize = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
                    Unsafe.CopyBlock(fullSpecData + specOffset, srcData, dataSize);
                    mapEntries[i].constantID = specDescs[i].ID;
                    mapEntries[i].offset = specOffset;
                    mapEntries[i].size = dataSize;
                    specOffset += dataSize;
                }

                specializationInfo.dataSize = specDataSize;
                specializationInfo.pData = fullSpecData;
                specializationInfo.mapEntryCount = (uint)specializationCount;
                specializationInfo.pMapEntries = mapEntries;
            }

            var shaders = description.ShaderSet.Shaders;
            var stages = new StackList<VkPipelineShaderStageCreateInfo>();

            foreach (var shader in shaders)
            {
                var vkShader = Util.AssertSubtype<Shader, VkShader>(shader);
                var stageCI = VkPipelineShaderStageCreateInfo.New();
                stageCI.module = vkShader.ShaderModule;
                stageCI.stage = VkFormats.VdToVkShaderStages(shader.Stage);
                // stageCI.pName = CommonStrings.main; // Meh
                stageCI.pName = new FixedUtf8String(shader.EntryPoint); // TODO: DONT ALLOCATE HERE
                stageCI.pSpecializationInfo = &specializationInfo;
                stages.Add(stageCI);
            }

            pipelineCI.stageCount = stages.Count;
            pipelineCI.pStages = (VkPipelineShaderStageCreateInfo*)stages.Data;

            // ViewportState
            var viewportStateCI = VkPipelineViewportStateCreateInfo.New();
            viewportStateCI.viewportCount = 1;
            viewportStateCI.scissorCount = 1;

            pipelineCI.pViewportState = &viewportStateCI;

            // Pipeline Layout
            var resourceLayouts = description.ResourceLayouts;
            var pipelineLayoutCI = VkPipelineLayoutCreateInfo.New();
            pipelineLayoutCI.setLayoutCount = (uint)resourceLayouts.Length;
            var dsls = stackalloc VkDescriptorSetLayout[resourceLayouts.Length];
            for (int i = 0; i < resourceLayouts.Length; i++) dsls[i] = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
            pipelineLayoutCI.pSetLayouts = dsls;

            vkCreatePipelineLayout(_gd.Device, ref pipelineLayoutCI, null, out _pipelineLayout);
            pipelineCI.layout = _pipelineLayout;

            // Create fake RenderPass for compatibility.

            var renderPassCI = VkRenderPassCreateInfo.New();
            var outputDesc = description.Outputs;
            var attachments = new StackList<VkAttachmentDescription, Size512Bytes>();

            // TODO: A huge portion of this next part is duplicated in VkFramebuffer.cs.

            var colorAttachmentDescs = new StackList<VkAttachmentDescription>();
            var colorAttachmentRefs = new StackList<VkAttachmentReference>();

            for (uint i = 0; i < outputDesc.ColorAttachments.Length; i++)
            {
                colorAttachmentDescs[i].format = VkFormats.VdToVkPixelFormat(outputDesc.ColorAttachments[i].Format);
                colorAttachmentDescs[i].samples = vkSampleCount;
                colorAttachmentDescs[i].loadOp = VkAttachmentLoadOp.DontCare;
                colorAttachmentDescs[i].storeOp = VkAttachmentStoreOp.Store;
                colorAttachmentDescs[i].stencilLoadOp = VkAttachmentLoadOp.DontCare;
                colorAttachmentDescs[i].stencilStoreOp = VkAttachmentStoreOp.DontCare;
                colorAttachmentDescs[i].initialLayout = VkImageLayout.Undefined;
                colorAttachmentDescs[i].finalLayout = VkImageLayout.ShaderReadOnlyOptimal;
                attachments.Add(colorAttachmentDescs[i]);

                colorAttachmentRefs[i].attachment = i;
                colorAttachmentRefs[i].layout = VkImageLayout.ColorAttachmentOptimal;
            }

            var depthAttachmentDesc = new VkAttachmentDescription();
            var depthAttachmentRef = new VkAttachmentReference();

            if (outputDesc.DepthAttachment != null)
            {
                var depthFormat = outputDesc.DepthAttachment.Value.Format;
                bool hasStencil = FormatHelpers.IsStencilFormat(depthFormat);
                depthAttachmentDesc.format = VkFormats.VdToVkPixelFormat(outputDesc.DepthAttachment.Value.Format, true);
                depthAttachmentDesc.samples = vkSampleCount;
                depthAttachmentDesc.loadOp = VkAttachmentLoadOp.DontCare;
                depthAttachmentDesc.storeOp = VkAttachmentStoreOp.Store;
                depthAttachmentDesc.stencilLoadOp = VkAttachmentLoadOp.DontCare;
                depthAttachmentDesc.stencilStoreOp = hasStencil ? VkAttachmentStoreOp.Store : VkAttachmentStoreOp.DontCare;
                depthAttachmentDesc.initialLayout = VkImageLayout.Undefined;
                depthAttachmentDesc.finalLayout = VkImageLayout.DepthStencilAttachmentOptimal;

                depthAttachmentRef.attachment = (uint)outputDesc.ColorAttachments.Length;
                depthAttachmentRef.layout = VkImageLayout.DepthStencilAttachmentOptimal;
            }

            var subpass = new VkSubpassDescription();
            subpass.pipelineBindPoint = VkPipelineBindPoint.Graphics;
            subpass.colorAttachmentCount = (uint)outputDesc.ColorAttachments.Length;
            subpass.pColorAttachments = (VkAttachmentReference*)colorAttachmentRefs.Data;
            for (int i = 0; i < colorAttachmentDescs.Count; i++) attachments.Add(colorAttachmentDescs[i]);

            if (outputDesc.DepthAttachment != null)
            {
                subpass.pDepthStencilAttachment = &depthAttachmentRef;
                attachments.Add(depthAttachmentDesc);
            }

            var subpassDependency = new VkSubpassDependency();
            subpassDependency.srcSubpass = SubpassExternal;
            subpassDependency.srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput;
            subpassDependency.dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput;
            subpassDependency.dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite;

            renderPassCI.attachmentCount = attachments.Count;
            renderPassCI.pAttachments = (VkAttachmentDescription*)attachments.Data;
            renderPassCI.subpassCount = 1;
            renderPassCI.pSubpasses = &subpass;
            renderPassCI.dependencyCount = 1;
            renderPassCI.pDependencies = &subpassDependency;

            var creationResult = vkCreateRenderPass(_gd.Device, ref renderPassCI, null, out _renderPass);
            CheckResult(creationResult);

            pipelineCI.renderPass = _renderPass;

            var result = vkCreateGraphicsPipelines(_gd.Device, VkPipelineCache.Null, 1, ref pipelineCI, null, out _devicePipeline);
            CheckResult(result);

            ResourceSetCount = (uint)description.ResourceLayouts.Length;
            DynamicOffsetsCount = 0;
            foreach (VkResourceLayout layout in description.ResourceLayouts) DynamicOffsetsCount += layout.DynamicBufferCount;
        }

        public VkPipeline(VkGraphicsDevice gd, ref ComputePipelineDescription description)
            : base(ref description)
        {
            _gd = gd;
            IsComputePipeline = true;
            RefCount = new ResourceRefCount(DisposeCore);

            var pipelineCI = VkComputePipelineCreateInfo.New();

            // Pipeline Layout
            var resourceLayouts = description.ResourceLayouts;
            var pipelineLayoutCI = VkPipelineLayoutCreateInfo.New();
            pipelineLayoutCI.setLayoutCount = (uint)resourceLayouts.Length;
            var dsls = stackalloc VkDescriptorSetLayout[resourceLayouts.Length];
            for (int i = 0; i < resourceLayouts.Length; i++) dsls[i] = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
            pipelineLayoutCI.pSetLayouts = dsls;

            vkCreatePipelineLayout(_gd.Device, ref pipelineLayoutCI, null, out _pipelineLayout);
            pipelineCI.layout = _pipelineLayout;

            // Shader Stage

            VkSpecializationInfo specializationInfo;
            var specDescs = description.Specializations;

            if (specDescs != null)
            {
                uint specDataSize = 0;
                foreach (var spec in specDescs) specDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
                byte* fullSpecData = stackalloc byte[(int)specDataSize];
                int specializationCount = specDescs.Length;
                var mapEntries = stackalloc VkSpecializationMapEntry[specializationCount];
                uint specOffset = 0;

                for (int i = 0; i < specializationCount; i++)
                {
                    ulong data = specDescs[i].Data;
                    byte* srcData = (byte*)&data;
                    uint dataSize = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
                    Unsafe.CopyBlock(fullSpecData + specOffset, srcData, dataSize);
                    mapEntries[i].constantID = specDescs[i].ID;
                    mapEntries[i].offset = specOffset;
                    mapEntries[i].size = dataSize;
                    specOffset += dataSize;
                }

                specializationInfo.dataSize = specDataSize;
                specializationInfo.pData = fullSpecData;
                specializationInfo.mapEntryCount = (uint)specializationCount;
                specializationInfo.pMapEntries = mapEntries;
            }

            var shader = description.ComputeShader;
            var vkShader = Util.AssertSubtype<Shader, VkShader>(shader);
            var stageCI = VkPipelineShaderStageCreateInfo.New();
            stageCI.module = vkShader.ShaderModule;
            stageCI.stage = VkFormats.VdToVkShaderStages(shader.Stage);
            stageCI.pName = CommonStrings.main; // Meh
            stageCI.pSpecializationInfo = &specializationInfo;
            pipelineCI.stage = stageCI;

            var result = vkCreateComputePipelines(
                _gd.Device,
                VkPipelineCache.Null,
                1,
                ref pipelineCI,
                null,
                out _devicePipeline);
            CheckResult(result);

            ResourceSetCount = (uint)description.ResourceLayouts.Length;
            DynamicOffsetsCount = 0;
            foreach (VkResourceLayout layout in description.ResourceLayouts) DynamicOffsetsCount += layout.DynamicBufferCount;
        }

        #region Disposal

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        #endregion

        private void DisposeCore()
        {
            if (!_destroyed)
            {
                _destroyed = true;
                vkDestroyPipelineLayout(_gd.Device, _pipelineLayout, null);
                vkDestroyPipeline(_gd.Device, _devicePipeline, null);
                if (!IsComputePipeline) vkDestroyRenderPass(_gd.Device, _renderPass, null);
            }
        }
    }
}
