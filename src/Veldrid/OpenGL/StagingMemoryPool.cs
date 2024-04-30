using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Veldrid.OpenGL
{
    internal sealed unsafe class StagingMemoryPool : IDisposable
    {
        private const uint MinimumCapacity = 128;

        private readonly List<StagingBlock> _storage;
        private readonly SortedList<uint, uint> _availableBlocks;
        private readonly object _lock = new object();
        private bool _disposed;

        public StagingMemoryPool()
        {
            _storage = new List<StagingBlock>();
            _availableBlocks = new SortedList<uint, uint>(new CapacityComparer());
        }

        #region Disposal

        public void Dispose()
        {
            lock (_lock)
            {
                _availableBlocks.Clear();
                foreach (var block in _storage) Marshal.FreeHGlobal((IntPtr)block.Data);
                _storage.Clear();
                _disposed = true;
            }
        }

        #endregion

        public StagingBlock Stage(IntPtr source, uint sizeInBytes)
        {
            Rent(sizeInBytes, out var block);
            Unsafe.CopyBlock(block.Data, source.ToPointer(), sizeInBytes);
            return block;
        }

        public StagingBlock Stage(byte[] bytes)
        {
            Rent((uint)bytes.Length, out var block);
            Marshal.Copy(bytes, 0, (IntPtr)block.Data, bytes.Length);
            return block;
        }

        public StagingBlock GetStagingBlock(uint sizeInBytes)
        {
            Rent(sizeInBytes, out var block);
            return block;
        }

        public StagingBlock RetrieveById(uint id)
        {
            return _storage[(int)id];
        }

        public void Free(StagingBlock block)
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    Debug.Assert(block.Id < _storage.Count);
                    _availableBlocks.Add(block.Capacity, block.Id);
                }
            }
        }

        private void Rent(uint size, out StagingBlock block)
        {
            lock (_lock)
            {
                var available = _availableBlocks;
                var indices = available.Values;

                for (int i = 0; i < available.Count; i++)
                {
                    int index = (int)indices[i];
                    var current = _storage[index];

                    if (current.Capacity >= size)
                    {
                        available.RemoveAt(i);
                        current.SizeInBytes = size;
                        block = current;
                        _storage[index] = current;
                        return;
                    }
                }

                Allocate(size, out block);
            }
        }

        private void Allocate(uint sizeInBytes, out StagingBlock stagingBlock)
        {
            uint capacity = Math.Max(MinimumCapacity, sizeInBytes);
            IntPtr ptr = Marshal.AllocHGlobal((int)capacity);
            uint id = (uint)_storage.Count;
            stagingBlock = new StagingBlock(id, (void*)ptr, capacity, sizeInBytes);
            _storage.Add(stagingBlock);
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
