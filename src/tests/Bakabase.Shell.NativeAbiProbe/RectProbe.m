#import <Foundation/Foundation.h>

// Only Foundation value objects: no application, windows, WebKit or UI session.
// These methods have the CGRect and NSInteger/BOOL argument shapes used by the shell.
@interface BakabaseNativeRectProbe : NSObject {
    NSRect _captured;
    NSInteger _capturedPolicy;
}
- (NSValue *)captureRect:(NSRect)rect cookie:(void *)cookie;
- (void)setCapturedRect:(NSRect)rect;
- (NSValue *)capturedRect;
- (BOOL)capturePolicy:(NSInteger)policy;
- (NSValue *)capturedPolicy;
@end

@implementation BakabaseNativeRectProbe
- (NSValue *)captureRect:(NSRect)rect cookie:(void *)cookie {
    if (cookie != (void *)0x5A17) return nil;
    return [NSValue valueWithRect:rect];
}
- (void)setCapturedRect:(NSRect)rect {
    _captured = rect;
}
- (NSValue *)capturedRect {
    return [NSValue valueWithRect:_captured];
}
- (BOOL)capturePolicy:(NSInteger)policy {
    _capturedPolicy = policy;
    return policy == 0 || policy > 1;
}
- (NSValue *)capturedPolicy {
    return [NSValue valueWithBytes:&_capturedPolicy objCType:@encode(NSInteger)];
}
@end
