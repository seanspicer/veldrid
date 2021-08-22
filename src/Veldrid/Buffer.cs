﻿using System;

namespace Veldrid
{
    /// <summary>
    /// A device resource used to store arbitrary graphics data in various formats.
    /// The size of a <see cref="DeviceBuffer"/> is fixed upon creation, and resizing is not possible.
    /// See <see cref="BufferDescription"/>.
    /// </summary>
    public abstract class DeviceBuffer : DeviceResource, BindableResource, MappableResource, IDisposable
    {
        /// <summary>
        /// The total capacity, in bytes, of the buffer. This value is fixed upon creation.
        /// </summary>
        public abstract uint SizeInBytes { get; }

        /// <summary>
        /// A bitmask indicating how this instance is permitted to be used.
        /// </summary>
        public abstract BufferUsage Usage { get; }

        /// <inheritdoc/>
        public abstract string? Name { get; set; }

        /// <summary>
        /// A bool indicating whether this instance has been disposed.
        /// </summary>
        public abstract bool IsDisposed { get; }

        /// <summary>
        /// Frees unmanaged device resources controlled by this instance.
        /// </summary>
        public abstract void Dispose();

        public uint GetSizeInBytes(uint subresource)
        {
            if (subresource != 0)
            {
                return 0;
            }
            return SizeInBytes;
        }
    }
}
