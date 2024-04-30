using System.Runtime.CompilerServices;

namespace Veldrid.OpenGL.NoAllocEntryList
{
    internal unsafe struct NoAllocSetResourceSetEntry
    {
        public const int MAX_INLINE_DYNAMIC_OFFSETS = 10;

        public readonly uint Slot;
        public readonly Tracked<ResourceSet> ResourceSet;
        public readonly bool IsGraphics;
        public readonly uint DynamicOffsetCount;
        public fixed uint DynamicOffsetsInline[MAX_INLINE_DYNAMIC_OFFSETS];
        public readonly StagingBlock DynamicOffsetsBlock;

        public NoAllocSetResourceSetEntry(
            uint slot,
            Tracked<ResourceSet> rs,
            bool isGraphics,
            uint dynamicOffsetCount,
            ref uint dynamicOffsets)
        {
            Slot = slot;
            ResourceSet = rs;
            IsGraphics = isGraphics;
            DynamicOffsetCount = dynamicOffsetCount;
            for (int i = 0; i < dynamicOffsetCount; i++) DynamicOffsetsInline[i] = Unsafe.Add(ref dynamicOffsets, i);

            DynamicOffsetsBlock = default;
        }

        public NoAllocSetResourceSetEntry(
            uint slot,
            Tracked<ResourceSet> rs,
            bool isGraphics,
            StagingBlock dynamicOffsets)
        {
            Slot = slot;
            ResourceSet = rs;
            IsGraphics = isGraphics;
            DynamicOffsetCount = dynamicOffsets.SizeInBytes / sizeof(uint);
            DynamicOffsetsBlock = dynamicOffsets;
        }
    }
}
