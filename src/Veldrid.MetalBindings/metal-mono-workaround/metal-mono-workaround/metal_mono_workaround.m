@import Metal;

#import "metal_mono_workaround.h"

void copyFromBuffer(id<MTLBlitCommandEncoder> encoder,
                    id<MTLBuffer> sourceBuffer,
                    NSUInteger sourceOffset,
                    NSUInteger sourceBytesPerRow,
                    NSUInteger sourceBytesPerImage,
                    MTLSize sourceSize,
                    id<MTLTexture> destinationTexture,
                    NSUInteger destinationSlice,
                    NSUInteger destinationLevel,
                    NSUInteger destinationOriginX,
                    NSUInteger destinationOriginY,
                    NSUInteger destinationOriginZ)
{
    [encoder copyFromBuffer:sourceBuffer
               sourceOffset:sourceOffset
          sourceBytesPerRow:sourceBytesPerRow
        sourceBytesPerImage:sourceBytesPerImage
                 sourceSize:sourceSize
                  toTexture:destinationTexture
           destinationSlice:destinationSlice
           destinationLevel:destinationLevel
          destinationOrigin:MTLOriginMake(destinationOriginX, destinationOriginY, destinationOriginZ)];
}
