using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Veldrid.OpenGL.NoAllocEntryList
{
    internal unsafe class OpenGLNoAllocCommandEntryList : IOpenGLCommandEntryList, IDisposable
    {
        public OpenGLCommandList Parent { get; }
        private readonly StagingMemoryPool memoryPool;
        private readonly List<EntryStorageBlock> blocks = new List<EntryStorageBlock>();
        private readonly List<object> resourceList = new List<object>();
        private readonly List<StagingBlock> stagingBlocks = new List<StagingBlock>();

        // Entry IDs
        private const byte begin_entry_id = 1;
        private static readonly uint begin_entry_size = Util.USizeOf<NoAllocBeginEntry>();

        private const byte clear_color_target_id = 2;
        private static readonly uint clear_color_target_entry_size = Util.USizeOf<NoAllocClearColorTargetEntry>();

        private const byte clear_depth_target_id = 3;
        private static readonly uint clear_depth_target_entry_size = Util.USizeOf<NoAllocClearDepthTargetEntry>();

        private const byte draw_indexed_entry_id = 4;
        private static readonly uint draw_indexed_entry_size = Util.USizeOf<NoAllocDrawIndexedEntry>();

        private const byte end_entry_id = 5;
        private static readonly uint end_entry_size = Util.USizeOf<NoAllocEndEntry>();

        private const byte set_framebuffer_entry_id = 6;
        private static readonly uint set_framebuffer_entry_size = Util.USizeOf<NoAllocSetFramebufferEntry>();

        private const byte set_index_buffer_entry_id = 7;
        private static readonly uint set_index_buffer_entry_size = Util.USizeOf<NoAllocSetIndexBufferEntry>();

        private const byte set_pipeline_entry_id = 8;
        private static readonly uint set_pipeline_entry_size = Util.USizeOf<NoAllocSetPipelineEntry>();

        private const byte set_resource_set_entry_id = 9;
        private static readonly uint set_resource_set_entry_size = Util.USizeOf<NoAllocSetResourceSetEntry>();

        private const byte set_scissor_rect_entry_id = 10;
        private static readonly uint set_scissor_rect_entry_size = Util.USizeOf<NoAllocSetScissorRectEntry>();

        private const byte set_vertex_buffer_entry_id = 11;
        private static readonly uint set_vertex_buffer_entry_size = Util.USizeOf<NoAllocSetVertexBufferEntry>();

        private const byte set_viewport_entry_id = 12;
        private static readonly uint set_viewport_entry_size = Util.USizeOf<NoAllocSetViewportEntry>();

        private const byte update_buffer_entry_id = 13;
        private static readonly uint update_buffer_entry_size = Util.USizeOf<NoAllocUpdateBufferEntry>();

        private const byte copy_buffer_entry_id = 14;
        private static readonly uint copy_buffer_entry_size = Util.USizeOf<NoAllocCopyBufferEntry>();

        private const byte copy_texture_entry_id = 15;
        private static readonly uint copy_texture_entry_size = Util.USizeOf<NoAllocCopyTextureEntry>();

        private const byte resolve_texture_entry_id = 16;
        private static readonly uint resolve_texture_entry_size = Util.USizeOf<NoAllocResolveTextureEntry>();

        private const byte draw_entry_id = 17;
        private static readonly uint draw_entry_size = Util.USizeOf<NoAllocDrawEntry>();

        private const byte dispatch_entry_id = 18;
        private static readonly uint dispatch_entry_size = Util.USizeOf<NoAllocDispatchEntry>();

        private const byte draw_indirect_entry_id = 20;
        private static readonly uint draw_indirect_entry_size = Util.USizeOf<NoAllocDrawIndirectEntry>();

        private const byte draw_indexed_indirect_entry_id = 21;
        private static readonly uint draw_indexed_indirect_entry_size = Util.USizeOf<NoAllocDrawIndexedIndirectEntry>();

        private const byte dispatch_indirect_entry_id = 22;
        private static readonly uint dispatch_indirect_entry_size = Util.USizeOf<NoAllocDispatchIndirectEntry>();

        private const byte generate_mipmaps_entry_id = 23;
        private static readonly uint generate_mipmaps_entry_size = Util.USizeOf<NoAllocGenerateMipmapsEntry>();

        private const byte push_debug_group_entry_id = 24;
        private static readonly uint push_debug_group_entry_size = Util.USizeOf<NoAllocPushDebugGroupEntry>();

        private const byte pop_debug_group_entry_id = 25;
        private static readonly uint pop_debug_group_entry_size = Util.USizeOf<NoAllocPopDebugGroupEntry>();

        private const byte insert_debug_marker_entry_id = 26;
        private static readonly uint insert_debug_marker_entry_size = Util.USizeOf<NoAllocInsertDebugMarkerEntry>();
        private EntryStorageBlock currentBlock;
        private uint totalEntries;

        public OpenGLNoAllocCommandEntryList(OpenGLCommandList cl)
        {
            Parent = cl;
            memoryPool = cl.Device.StagingMemoryPool;
            currentBlock = EntryStorageBlock.New();
            blocks.Add(currentBlock);
        }

        #region Disposal

        public void Dispose()
        {
            flushStagingBlocks();
            resourceList.Clear();
            totalEntries = 0;
            currentBlock = blocks[0];

            foreach (var block in blocks)
            {
                block.Clear();
                block.Free();
            }
        }

        #endregion

        public void Reset()
        {
            flushStagingBlocks();
            resourceList.Clear();
            totalEntries = 0;
            currentBlock = blocks[0];
            foreach (var block in blocks) block.Clear();
        }

        public void* GetStorageChunk(uint size, out byte* terminatorWritePtr)
        {
            terminatorWritePtr = null;

            if (!currentBlock.Alloc(size, out var ptr))
            {
                int currentBlockIndex = blocks.IndexOf(currentBlock);
                bool anyWorked = false;

                for (int i = currentBlockIndex + 1; i < blocks.Count; i++)
                {
                    var nextBlock = blocks[i];

                    if (nextBlock.Alloc(size, out ptr))
                    {
                        currentBlock = nextBlock;
                        anyWorked = true;
                        break;
                    }
                }

                if (!anyWorked)
                {
                    currentBlock = EntryStorageBlock.New();
                    blocks.Add(currentBlock);
                    bool result = currentBlock.Alloc(size, out ptr);
                    Debug.Assert(result);
                }
            }

            if (currentBlock.RemainingSize > size) terminatorWritePtr = (byte*)ptr + size;

            return ptr;
        }

        public void AddEntry<T>(byte id, ref T entry) where T : struct
        {
            uint size = Util.USizeOf<T>();
            AddEntry(id, size, ref entry);
        }

        public void AddEntry<T>(byte id, uint sizeOfT, ref T entry) where T : struct
        {
            Debug.Assert(sizeOfT == Unsafe.SizeOf<T>());
            uint storageSize = sizeOfT + 1; // Include ID
            var storagePtr = GetStorageChunk(storageSize, out byte* terminatorWritePtr);
            Unsafe.Write(storagePtr, id);
            Unsafe.Write((byte*)storagePtr + 1, entry);
            if (terminatorWritePtr != null) *terminatorWritePtr = 0;
            totalEntries += 1;
        }

        public void ExecuteAll(OpenGLCommandExecutor executor)
        {
            int currentBlockIndex = 0;
            var block = blocks[currentBlockIndex];
            uint currentOffset = 0;

            for (uint i = 0; i < totalEntries; i++)
            {
                if (currentOffset == block.TotalSize)
                {
                    currentBlockIndex += 1;
                    block = blocks[currentBlockIndex];
                    currentOffset = 0;
                }

                uint id = Unsafe.Read<byte>(block.BasePtr + currentOffset);

                if (id == 0)
                {
                    currentBlockIndex += 1;
                    block = blocks[currentBlockIndex];
                    currentOffset = 0;
                    id = Unsafe.Read<byte>(block.BasePtr + currentOffset);
                }

                Debug.Assert(id != 0);
                currentOffset += 1;
                byte* entryBasePtr = block.BasePtr + currentOffset;

                switch (id)
                {
                    case begin_entry_id:
                        executor.Begin();
                        currentOffset += begin_entry_size;
                        break;

                    case clear_color_target_id:
                        var ccte = Unsafe.ReadUnaligned<NoAllocClearColorTargetEntry>(entryBasePtr);
                        executor.ClearColorTarget(ccte.Index, ccte.ClearColor);
                        currentOffset += clear_color_target_entry_size;
                        break;

                    case clear_depth_target_id:
                        var cdte = Unsafe.ReadUnaligned<NoAllocClearDepthTargetEntry>(entryBasePtr);
                        executor.ClearDepthStencil(cdte.Depth, cdte.Stencil);
                        currentOffset += clear_depth_target_entry_size;
                        break;

                    case draw_entry_id:
                        var de = Unsafe.ReadUnaligned<NoAllocDrawEntry>(entryBasePtr);
                        executor.Draw(de.VertexCount, de.InstanceCount, de.VertexStart, de.InstanceStart);
                        currentOffset += draw_entry_size;
                        break;

                    case draw_indexed_entry_id:
                        var die = Unsafe.ReadUnaligned<NoAllocDrawIndexedEntry>(entryBasePtr);
                        executor.DrawIndexed(die.IndexCount, die.InstanceCount, die.IndexStart, die.VertexOffset, die.InstanceStart);
                        currentOffset += draw_indexed_entry_size;
                        break;

                    case draw_indirect_entry_id:
                        var drawIndirectEntry = Unsafe.ReadUnaligned<NoAllocDrawIndirectEntry>(entryBasePtr);
                        executor.DrawIndirect(
                            drawIndirectEntry.IndirectBuffer.Get(resourceList),
                            drawIndirectEntry.Offset,
                            drawIndirectEntry.DrawCount,
                            drawIndirectEntry.Stride);
                        currentOffset += draw_indirect_entry_size;
                        break;

                    case draw_indexed_indirect_entry_id:
                        var diie = Unsafe.ReadUnaligned<NoAllocDrawIndexedIndirectEntry>(entryBasePtr);
                        executor.DrawIndexedIndirect(diie.IndirectBuffer.Get(resourceList), diie.Offset, diie.DrawCount, diie.Stride);
                        currentOffset += draw_indexed_indirect_entry_size;
                        break;

                    case dispatch_entry_id:
                        var dispatchEntry = Unsafe.ReadUnaligned<NoAllocDispatchEntry>(entryBasePtr);
                        executor.Dispatch(dispatchEntry.GroupCountX, dispatchEntry.GroupCountY, dispatchEntry.GroupCountZ);
                        currentOffset += dispatch_entry_size;
                        break;

                    case dispatch_indirect_entry_id:
                        var dispatchIndir = Unsafe.ReadUnaligned<NoAllocDispatchIndirectEntry>(entryBasePtr);
                        executor.DispatchIndirect(dispatchIndir.IndirectBuffer.Get(resourceList), dispatchIndir.Offset);
                        currentOffset += dispatch_indirect_entry_size;
                        break;

                    case end_entry_id:
                        executor.End();
                        currentOffset += end_entry_size;
                        break;

                    case set_framebuffer_entry_id:
                        var sfbe = Unsafe.ReadUnaligned<NoAllocSetFramebufferEntry>(entryBasePtr);
                        executor.SetFramebuffer(sfbe.Framebuffer.Get(resourceList));
                        currentOffset += set_framebuffer_entry_size;
                        break;

                    case set_index_buffer_entry_id:
                        var sibe = Unsafe.ReadUnaligned<NoAllocSetIndexBufferEntry>(entryBasePtr);
                        executor.SetIndexBuffer(sibe.Buffer.Get(resourceList), sibe.Format, sibe.Offset);
                        currentOffset += set_index_buffer_entry_size;
                        break;

                    case set_pipeline_entry_id:
                        var spe = Unsafe.ReadUnaligned<NoAllocSetPipelineEntry>(entryBasePtr);
                        executor.SetPipeline(spe.Pipeline.Get(resourceList));
                        currentOffset += set_pipeline_entry_size;
                        break;

                    case set_resource_set_entry_id:
                        var srse = Unsafe.ReadUnaligned<NoAllocSetResourceSetEntry>(entryBasePtr);
                        var rs = srse.ResourceSet.Get(resourceList);
                        uint* dynamicOffsetsPtr = srse.DynamicOffsetCount > NoAllocSetResourceSetEntry.MAX_INLINE_DYNAMIC_OFFSETS
                            ? (uint*)srse.DynamicOffsetsBlock.Data
                            : srse.DynamicOffsetsInline;

                        if (srse.IsGraphics)
                        {
                            executor.SetGraphicsResourceSet(
                                srse.Slot,
                                rs,
                                srse.DynamicOffsetCount,
                                ref Unsafe.AsRef<uint>(dynamicOffsetsPtr));
                        }
                        else
                        {
                            executor.SetComputeResourceSet(
                                srse.Slot,
                                rs,
                                srse.DynamicOffsetCount,
                                ref Unsafe.AsRef<uint>(dynamicOffsetsPtr));
                        }

                        currentOffset += set_resource_set_entry_size;
                        break;

                    case set_scissor_rect_entry_id:
                        var ssre = Unsafe.ReadUnaligned<NoAllocSetScissorRectEntry>(entryBasePtr);
                        executor.SetScissorRect(ssre.Index, ssre.X, ssre.Y, ssre.Width, ssre.Height);
                        currentOffset += set_scissor_rect_entry_size;
                        break;

                    case set_vertex_buffer_entry_id:
                        var svbe = Unsafe.ReadUnaligned<NoAllocSetVertexBufferEntry>(entryBasePtr);
                        executor.SetVertexBuffer(svbe.Index, svbe.Buffer.Get(resourceList), svbe.Offset);
                        currentOffset += set_vertex_buffer_entry_size;
                        break;

                    case set_viewport_entry_id:
                        var svpe = Unsafe.ReadUnaligned<NoAllocSetViewportEntry>(entryBasePtr);
                        executor.SetViewport(svpe.Index, ref svpe.Viewport);
                        currentOffset += set_viewport_entry_size;
                        break;

                    case update_buffer_entry_id:
                        var ube = Unsafe.ReadUnaligned<NoAllocUpdateBufferEntry>(entryBasePtr);
                        byte* dataPtr = (byte*)ube.StagingBlock.Data;
                        executor.UpdateBuffer(
                            ube.Buffer.Get(resourceList),
                            ube.BufferOffsetInBytes,
                            (IntPtr)dataPtr, ube.StagingBlockSize);
                        currentOffset += update_buffer_entry_size;
                        break;

                    case copy_buffer_entry_id:
                        var cbe = Unsafe.ReadUnaligned<NoAllocCopyBufferEntry>(entryBasePtr);
                        executor.CopyBuffer(
                            cbe.Source.Get(resourceList),
                            cbe.SourceOffset,
                            cbe.Destination.Get(resourceList),
                            cbe.DestinationOffset,
                            cbe.SizeInBytes);
                        currentOffset += copy_buffer_entry_size;
                        break;

                    case copy_texture_entry_id:
                        var cte = Unsafe.ReadUnaligned<NoAllocCopyTextureEntry>(entryBasePtr);
                        executor.CopyTexture(
                            cte.Source.Get(resourceList),
                            cte.SrcX, cte.SrcY, cte.SrcZ,
                            cte.SrcMipLevel,
                            cte.SrcBaseArrayLayer,
                            cte.Destination.Get(resourceList),
                            cte.DstX, cte.DstY, cte.DstZ,
                            cte.DstMipLevel,
                            cte.DstBaseArrayLayer,
                            cte.Width, cte.Height, cte.Depth,
                            cte.LayerCount);
                        currentOffset += copy_texture_entry_size;
                        break;

                    case resolve_texture_entry_id:
                        var rte = Unsafe.ReadUnaligned<NoAllocResolveTextureEntry>(entryBasePtr);
                        executor.ResolveTexture(rte.Source.Get(resourceList), rte.Destination.Get(resourceList));
                        currentOffset += resolve_texture_entry_size;
                        break;

                    case generate_mipmaps_entry_id:
                        var gme = Unsafe.ReadUnaligned<NoAllocGenerateMipmapsEntry>(entryBasePtr);
                        executor.GenerateMipmaps(gme.Texture.Get(resourceList));
                        currentOffset += generate_mipmaps_entry_size;
                        break;

                    case push_debug_group_entry_id:
                        var pdge = Unsafe.ReadUnaligned<NoAllocPushDebugGroupEntry>(entryBasePtr);
                        executor.PushDebugGroup(pdge.Name.Get(resourceList));
                        currentOffset += push_debug_group_entry_size;
                        break;

                    case pop_debug_group_entry_id:
                        executor.PopDebugGroup();
                        currentOffset += pop_debug_group_entry_size;
                        break;

                    case insert_debug_marker_entry_id:
                        var idme = Unsafe.ReadUnaligned<NoAllocInsertDebugMarkerEntry>(entryBasePtr);
                        executor.InsertDebugMarker(idme.Name.Get(resourceList));
                        currentOffset += insert_debug_marker_entry_size;
                        break;

                    default:
                        throw new InvalidOperationException("Invalid entry ID: " + id);
                }
            }
        }

        public void Begin()
        {
            var entry = new NoAllocBeginEntry();
            AddEntry(begin_entry_id, ref entry);
        }

        public void ClearColorTarget(uint index, RgbaFloat clearColor)
        {
            var entry = new NoAllocClearColorTargetEntry(index, clearColor);
            AddEntry(clear_color_target_id, ref entry);
        }

        public void ClearDepthTarget(float depth, byte stencil)
        {
            var entry = new NoAllocClearDepthTargetEntry(depth, stencil);
            AddEntry(clear_depth_target_id, ref entry);
        }

        public void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            var entry = new NoAllocDrawEntry(vertexCount, instanceCount, vertexStart, instanceStart);
            AddEntry(draw_entry_id, ref entry);
        }

        public void DrawIndexed(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            var entry = new NoAllocDrawIndexedEntry(indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
            AddEntry(draw_indexed_entry_id, ref entry);
        }

        public void DrawIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            var entry = new NoAllocDrawIndirectEntry(track(indirectBuffer), offset, drawCount, stride);
            AddEntry(draw_indirect_entry_id, ref entry);
        }

        public void DrawIndexedIndirect(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            var entry = new NoAllocDrawIndexedIndirectEntry(track(indirectBuffer), offset, drawCount, stride);
            AddEntry(draw_indexed_indirect_entry_id, ref entry);
        }

        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            var entry = new NoAllocDispatchEntry(groupCountX, groupCountY, groupCountZ);
            AddEntry(dispatch_entry_id, ref entry);
        }

        public void DispatchIndirect(DeviceBuffer indirectBuffer, uint offset)
        {
            var entry = new NoAllocDispatchIndirectEntry(track(indirectBuffer), offset);
            AddEntry(dispatch_indirect_entry_id, ref entry);
        }

        public void End()
        {
            var entry = new NoAllocEndEntry();
            AddEntry(end_entry_id, ref entry);
        }

        public void SetFramebuffer(Framebuffer fb)
        {
            var entry = new NoAllocSetFramebufferEntry(track(fb));
            AddEntry(set_framebuffer_entry_id, ref entry);
        }

        public void SetIndexBuffer(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            var entry = new NoAllocSetIndexBufferEntry(track(buffer), format, offset);
            AddEntry(set_index_buffer_entry_id, ref entry);
        }

        public void SetPipeline(Pipeline pipeline)
        {
            var entry = new NoAllocSetPipelineEntry(track(pipeline));
            AddEntry(set_pipeline_entry_id, ref entry);
        }

        public void SetGraphicsResourceSet(uint slot, ResourceSet rs, uint dynamicOffsetCount, ref uint dynamicOffsets)
        {
            setResourceSet(slot, rs, dynamicOffsetCount, ref dynamicOffsets, true);
        }

        public void SetComputeResourceSet(uint slot, ResourceSet rs, uint dynamicOffsetCount, ref uint dynamicOffsets)
        {
            setResourceSet(slot, rs, dynamicOffsetCount, ref dynamicOffsets, false);
        }

        public void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            var entry = new NoAllocSetScissorRectEntry(index, x, y, width, height);
            AddEntry(set_scissor_rect_entry_id, ref entry);
        }

        public void SetVertexBuffer(uint index, DeviceBuffer buffer, uint offset)
        {
            var entry = new NoAllocSetVertexBufferEntry(index, track(buffer), offset);
            AddEntry(set_vertex_buffer_entry_id, ref entry);
        }

        public void SetViewport(uint index, ref Viewport viewport)
        {
            var entry = new NoAllocSetViewportEntry(index, ref viewport);
            AddEntry(set_viewport_entry_id, ref entry);
        }

        public void ResolveTexture(Texture source, Texture destination)
        {
            var entry = new NoAllocResolveTextureEntry(track(source), track(destination));
            AddEntry(resolve_texture_entry_id, ref entry);
        }

        public void UpdateBuffer(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            var stagingBlock = memoryPool.Stage(source, sizeInBytes);
            stagingBlocks.Add(stagingBlock);
            var entry = new NoAllocUpdateBufferEntry(track(buffer), bufferOffsetInBytes, stagingBlock, sizeInBytes);
            AddEntry(update_buffer_entry_id, ref entry);
        }

        public void CopyBuffer(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
        {
            var entry = new NoAllocCopyBufferEntry(
                track(source),
                sourceOffset,
                track(destination),
                destinationOffset,
                sizeInBytes);
            AddEntry(copy_buffer_entry_id, ref entry);
        }

        public void CopyTexture(
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
            var entry = new NoAllocCopyTextureEntry(
                track(source),
                srcX, srcY, srcZ,
                srcMipLevel,
                srcBaseArrayLayer,
                track(destination),
                dstX, dstY, dstZ,
                dstMipLevel,
                dstBaseArrayLayer,
                width, height, depth,
                layerCount);
            AddEntry(copy_texture_entry_id, ref entry);
        }

        public void GenerateMipmaps(Texture texture)
        {
            var entry = new NoAllocGenerateMipmapsEntry(track(texture));
            AddEntry(generate_mipmaps_entry_id, ref entry);
        }

        public void PushDebugGroup(string name)
        {
            var entry = new NoAllocPushDebugGroupEntry(track(name));
            AddEntry(push_debug_group_entry_id, ref entry);
        }

        public void PopDebugGroup()
        {
            var entry = new NoAllocPopDebugGroupEntry();
            AddEntry(pop_debug_group_entry_id, ref entry);
        }

        public void InsertDebugMarker(string name)
        {
            var entry = new NoAllocInsertDebugMarkerEntry(track(name));
            AddEntry(insert_debug_marker_entry_id, ref entry);
        }

        private void flushStagingBlocks()
        {
            var pool = memoryPool;
            foreach (var block in stagingBlocks) pool.Free(block);

            stagingBlocks.Clear();
        }

        private void setResourceSet(uint slot, ResourceSet rs, uint dynamicOffsetCount, ref uint dynamicOffsets, bool isGraphics)
        {
            NoAllocSetResourceSetEntry entry;

            if (dynamicOffsetCount > NoAllocSetResourceSetEntry.MAX_INLINE_DYNAMIC_OFFSETS)
            {
                var block = memoryPool.GetStagingBlock(dynamicOffsetCount * sizeof(uint));
                stagingBlocks.Add(block);
                for (uint i = 0; i < dynamicOffsetCount; i++) *((uint*)block.Data + i) = Unsafe.Add(ref dynamicOffsets, (int)i);

                entry = new NoAllocSetResourceSetEntry(slot, track(rs), isGraphics, block);
            }
            else
                entry = new NoAllocSetResourceSetEntry(slot, track(rs), isGraphics, dynamicOffsetCount, ref dynamicOffsets);

            AddEntry(set_resource_set_entry_id, ref entry);
        }

        private Tracked<T> track<T>(T item) where T : class
        {
            return new Tracked<T>(resourceList, item);
        }

        private struct EntryStorageBlock : IEquatable<EntryStorageBlock>
        {
            private const int default_storage_block_size = 40000;
            private readonly byte[] bytes;
            private readonly GCHandle gcHandle;
            public readonly byte* BasePtr;

            private uint unusedStart;
            public uint RemainingSize => (uint)bytes.Length - unusedStart;

            public uint TotalSize => (uint)bytes.Length;

            public bool Alloc(uint size, out void* ptr)
            {
                if (RemainingSize < size)
                {
                    ptr = null;
                    return false;
                }

                ptr = BasePtr + unusedStart;
                unusedStart += size;
                return true;
            }

            private EntryStorageBlock(int storageBlockSize)
            {
                bytes = new byte[storageBlockSize];
                gcHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                BasePtr = (byte*)gcHandle.AddrOfPinnedObject().ToPointer();
                unusedStart = 0;
            }

            public static EntryStorageBlock New()
            {
                return new EntryStorageBlock(default_storage_block_size);
            }

            public void Free()
            {
                gcHandle.Free();
            }

            internal void Clear()
            {
                unusedStart = 0;
                Util.ClearArray(bytes);
            }

            public bool Equals(EntryStorageBlock other)
            {
                return bytes == other.bytes;
            }
        }
    }

    /// <summary>
    ///     A handle for an object stored in some List.
    /// </summary>
    /// <typeparam name="T">The type of object to track.</typeparam>
    internal struct Tracked<T> where T : class
    {
        private readonly int index;

        public T Get(List<object> list)
        {
            return (T)list[index];
        }

        public Tracked(List<object> list, T item)
        {
            index = list.Count;
            list.Add(item);
        }
    }
}
