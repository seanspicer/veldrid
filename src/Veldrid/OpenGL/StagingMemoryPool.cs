using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Veldrid.OpenGL
{
    internal sealed unsafe class StagingMemoryPool : IDisposable
    {
        private const uint minimum_capacity = 128;

        private readonly List<StagingBlock> storage;
        private readonly SortedList<uint, uint> availableBlocks;
        private readonly object @lock = new object();
        private bool disposed;

        public StagingMemoryPool()
        {
            storage = new List<StagingBlock>();
            availableBlocks = new SortedList<uint, uint>(new CapacityComparer());
        }

        #region Disposal

        public void Dispose()
        {
            lock (@lock)
            {
                availableBlocks.Clear();
                foreach (var block in storage) Marshal.FreeHGlobal((IntPtr)block.Data);
                storage.Clear();
                disposed = true;
            }
        }

        #endregion

        public StagingBlock Stage(IntPtr source, uint sizeInBytes)
        {
            rent(sizeInBytes, out var block);
            Unsafe.CopyBlock(block.Data, source.ToPointer(), sizeInBytes);
            return block;
        }

        public StagingBlock Stage(byte[] bytes)
        {
            rent((uint)bytes.Length, out var block);
            Marshal.Copy(bytes, 0, (IntPtr)block.Data, bytes.Length);
            return block;
        }

        public StagingBlock GetStagingBlock(uint sizeInBytes)
        {
            rent(sizeInBytes, out var block);
            return block;
        }

        public StagingBlock RetrieveById(uint id)
        {
            return storage[(int)id];
        }

        public void Free(StagingBlock block)
        {
            lock (@lock)
            {
                if (!disposed)
                {
                    Debug.Assert(block.Id < storage.Count);
                    availableBlocks.Add(block.Capacity, block.Id);
                }
            }
        }

        private void rent(uint size, out StagingBlock block)
        {
            lock (@lock)
            {
                var available = availableBlocks;
                var indices = available.Values;

                for (int i = 0; i < available.Count; i++)
                {
                    int index = (int)indices[i];
                    var current = storage[index];

                    if (current.Capacity >= size)
                    {
                        available.RemoveAt(i);
                        current.SizeInBytes = size;
                        block = current;
                        storage[index] = current;
                        return;
                    }
                }

                allocate(size, out block);
            }
        }

        private void allocate(uint sizeInBytes, out StagingBlock stagingBlock)
        {
            uint capacity = Math.Max(minimum_capacity, sizeInBytes);
            IntPtr ptr = Marshal.AllocHGlobal((int)capacity);
            uint id = (uint)storage.Count;
            stagingBlock = new StagingBlock(id, (void*)ptr, capacity, sizeInBytes);
            storage.Add(stagingBlock);
        }

        private class CapacityComparer : IComparer<uint>
        {
            public int Compare(uint x, uint y)
            {
                return x >= y ? 1 : -1;
            }
        }
    }

    internal unsafe struct StagingBlock
    {
        public readonly uint Id;
        public readonly void* Data;
        public readonly uint Capacity;
        public uint SizeInBytes;

        public StagingBlock(uint id, void* data, uint capacity, uint size)
        {
            Id = id;
            Data = data;
            Capacity = capacity;
            SizeInBytes = size;
        }
    }
}
