#import <Foundation/Foundation.h>
#import <QuartzCore/QuartzCore.h>

typedef void (*CADisplayLinkCallback)(void* proxy);

@interface CADisplayLinkProxy : NSObject
@property (nonatomic, readonly, nonnull) CADisplayLinkCallback callback;
@property (nonatomic, strong, nonnull) CADisplayLink* displayLink;
- (nonnull instancetype)initWithCallback:(nonnull CADisplayLinkCallback)callback;
@end

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
                    NSUInteger destinationOriginZ);

void copyFromTexture(id<MTLBlitCommandEncoder> encoder,
                     id<MTLTexture> sourceTexture,
                     NSUInteger sourceSlice,
                     NSUInteger sourceLevel,
                     MTLOrigin sourceOrigin,
                     MTLSize sourceSize,
                     id<MTLTexture> destinationTexture,
                     NSUInteger destinationSlice,
                     NSUInteger destinationLevel,
                     NSUInteger destinationOriginX,
                     NSUInteger destinationOriginY,
                     NSUInteger destinationOriginZ);

CADisplayLinkProxy* createDisplayLinkProxy(CADisplayLinkCallback callback);
