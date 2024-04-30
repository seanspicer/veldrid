using System;
using Vulkan;
using static Vulkan.VulkanNative;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkShader : Shader
    {
        public VkShaderModule ShaderModule => _shaderModule;

        public override bool IsDisposed => _disposed;

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
        private readonly VkShaderModule _shaderModule;
        private bool _disposed;
        private string _name;

        public VkShader(VkGraphicsDevice gd, ref ShaderDescription description)
            : base(description.Stage, description.EntryPoint)
        {
            _gd = gd;

            var shaderModuleCI = VkShaderModuleCreateInfo.New();

            fixed (byte* codePtr = description.ShaderBytes)
            {
                shaderModuleCI.codeSize = (UIntPtr)description.ShaderBytes.Length;
                shaderModuleCI.pCode = (uint*)codePtr;
                var result = vkCreateShaderModule(gd.Device, ref shaderModuleCI, null, out _shaderModule);
                CheckResult(result);
            }
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                vkDestroyShaderModule(_gd.Device, ShaderModule, null);
            }
        }

        #endregion
    }
}
