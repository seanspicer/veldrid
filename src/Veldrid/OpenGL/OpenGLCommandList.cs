using System;
using System.Collections.Generic;
using System.Diagnostics;
using Veldrid.OpenGL.NoAllocEntryList;

namespace Veldrid.OpenGL
{
    internal class OpenGLCommandList : CommandList
    {
        public override bool IsDisposed => disposed;

        public override string Name { get; set; }

        internal IOpenGLCommandEntryList CurrentCommands { get; private set; }

        internal OpenGLGraphicsDevice Device { get; }

        private readonly object @lock = new object();
        private readonly List<IOpenGLCommandEntryList> availableLists = new List<IOpenGLCommandEntryList>();
        private readonly List<IOpenGLCommandEntryList> submittedLists = new List<IOpenGLCommandEntryList>();
        private bool disposed;

        public OpenGLCommandList(OpenGLGraphicsDevice gd, ref CommandListDescription description)
            : base(ref description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
        {
            Device = gd;
        }

        #region Disposal

        public override void Dispose()
        {
            Device.EnqueueDisposal(this);
        }

        #endregion

        public override void Begin()
        {
            ClearCachedState();
            CurrentCommands?.Dispose();

            CurrentCommands = getFreeCommandList();
            CurrentCommands.Begin();
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            CurrentCommands.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public override void End()
        {
            CurrentCommands.End();
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            CurrentCommands.SetScissorRect(index, x, y, width, height);
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            CurrentCommands.SetViewport(index, ref viewport);
        }

        public void OnSubmitted(IOpenGLCommandEntryList entryList)
        {
            CurrentCommands = null;

            lock (@lock)
            {
                Debug.Assert(!submittedLists.Contains(entryList));
                submittedLists.Add(entryList);

                Debug.Assert(!availableLists.Contains(entryList));
            }
        }

        public void OnCompleted(IOpenGLCommandEntryList entryList)
        {
            lock (@lock)
            {
                entryList.Reset();

                Debug.Assert(!availableLists.Contains(entryList));
                availableLists.Add(entryList);

                Debug.Assert(submittedLists.Contains(entryList));
                submittedLists.Remove(entryList);
            }
        }

        public void DestroyResources()
        {
            lock (@lock)
            {
                CurrentCommands?.Dispose();
                foreach (var list in availableLists) list.Dispose();

                foreach (var list in submittedLists) list.Dispose();

                disposed = true;
            }
        }

        internal void Reset()
        {
            CurrentCommands.Reset();
        }

        protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            CurrentCommands.DrawIndirect(indirectBuffer, offset, drawCount, stride);
        }

        protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            CurrentCommands.DrawIndexedIndirect(indirectBuffer, offset, drawCount, stride);
        }

        protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
        {
            CurrentCommands.DispatchIndirect(indirectBuffer, offset);
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            CurrentCommands.ResolveTexture(source, destination);
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            CurrentCommands.SetFramebuffer(fb);
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetCount, ref uint dynamicOffsets)
        {
            CurrentCommands.SetGraphicsResourceSet(slot, rs, dynamicOffsetCount, ref dynamicOffsets);
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetCount, ref uint dynamicOffsets)
        {
            CurrentCommands.SetComputeResourceSet(slot, rs, dynamicOffsetCount, ref dynamicOffsets);
        }

        protected override void CopyBufferCore(
            DeviceBuffer source,
            uint sourceOffset,
            DeviceBuffer destination,
            uint destinationOffset,
            uint sizeInBytes)
        {
            CurrentCommands.CopyBuffer(source, sourceOffset, destination, destinationOffset, sizeInBytes);
        }

        protected override void CopyTextureCore(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            CurrentCommands.CopyTexture(
                source,
                srcX, srcY, srcZ,
                srcMipLevel,
                srcBaseArrayLayer,
                destination,
                dstX, dstY, dstZ,
                dstMipLevel,
                dstBaseArrayLayer,
                width, height, depth,
                layerCount);
        }

        private IOpenGLCommandEntryList getFreeCommandList()
        {
            lock (@lock)
            {
                if (availableLists.Count > 0)
                {
                    var ret = availableLists[availableLists.Count - 1];
                    availableLists.RemoveAt(availableLists.Count - 1);
                    return ret;
                }

                return new OpenGLNoAllocCommandEntryList(this);
            }
        }

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
        {
            CurrentCommands.ClearColorTarget(index, clearColor);
        }

        private protected override void ClearDepthStencilCore(float depth, byte stencil)
        {
            CurrentCommands.ClearDepthTarget(depth, stencil);
        }

        private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            CurrentCommands.Draw(vertexCount, instanceCount, vertexStart, instanceStart);
        }

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            CurrentCommands.DrawIndexed(indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
        }

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            CurrentCommands.SetIndexBuffer(buffer, format, offset);
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            CurrentCommands.SetPipeline(pipeline);
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            CurrentCommands.SetVertexBuffer(index, buffer, offset);
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            CurrentCommands.UpdateBuffer(buffer, bufferOffsetInBytes, source, sizeInBytes);
        }

        private protected override void GenerateMipmapsCore(Texture texture)
        {
            CurrentCommands.GenerateMipmaps(texture);
        }

        private protected override void PushDebugGroupCore(string name)
        {
            CurrentCommands.PushDebugGroup(name);
        }

        private protected override void PopDebugGroupCore()
        {
            CurrentCommands.PopDebugGroup();
        }

        private protected override void InsertDebugMarkerCore(string name)
        {
            CurrentCommands.InsertDebugMarker(name);
        }
    }
}
