#import <Foundation/Foundation.h>

// Only Foundation value objects: no application, windows, WebKit or UI session.
// These methods have exactly the two CGRect argument shapes used by the shell.
@interface BakabaseNativeRectProbe : NSObject {
    NSRect _captured;
}
- (NSValue *)captureRect:(NSRect)rect cookie:(void *)cookie;
- (void)setCapturedRect:(NSRect)rect;
- (NSValue *)capturedRect;
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
@end
