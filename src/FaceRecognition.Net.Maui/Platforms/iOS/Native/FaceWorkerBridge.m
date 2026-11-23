#import "FaceWorkerBridge.h"
#import "FaceSdkCompat.h"
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#if __has_include(<facerecognitionsdk/FaceRecognitionSDK.h>)
#import <facerecognitionsdk/FaceRecognitionSDK.h>
#endif

static FaceWorkerEventCallback s_callback;
static UIImage *s_lastLiveImage;
static NSInteger s_lastLiveW;
static NSInteger s_lastLiveH;
static BOOL s_videoFrameBusy;
static dispatch_queue_t s_workerQueue;

static dispatch_queue_t FRSWorkerQueue(void)
{
  static dispatch_once_t once;
  dispatch_once(&once, ^{
    s_workerQueue = dispatch_queue_create("com.faceplugin.maui.videoworker", DISPATCH_QUEUE_SERIAL);
  });
  return s_workerQueue;
}

/** Same as RN FRSFixOrientation — bake to scale-1 CGImage before SDK calls. */
static UIImage *FRSFixOrientation(UIImage *image)
{
  if (image == nil) {
    return nil;
  }
  if (image.imageOrientation == UIImageOrientationUp && image.scale == 1.0 && image.CGImage != nil) {
    return image;
  }
  CGSize pixelSize = CGSizeMake(image.size.width * image.scale, image.size.height * image.scale);
  if (pixelSize.width < 1 || pixelSize.height < 1) {
    return nil;
  }
  UIGraphicsBeginImageContextWithOptions(pixelSize, YES, 1);
  [image drawInRect:CGRectMake(0, 0, pixelSize.width, pixelSize.height)];
  UIImage *out = UIGraphicsGetImageFromCurrentImageContext();
  UIGraphicsEndImageContext();
  return out ?: image;
}

static UIImage *FRSImageFromBytes(const void *bytes, int length)
{
  if (bytes == NULL || length <= 0) {
    return nil;
  }
  NSData *data = [NSData dataWithBytes:bytes length:(NSUInteger)length];
  return FRSFixOrientation([UIImage imageWithData:data]);
}

static NSInteger FRSPixelW(UIImage *image)
{
  return image ? (NSInteger)llround(image.size.width * image.scale) : 0;
}

static NSInteger FRSPixelH(UIImage *image)
{
  return image ? (NSInteger)llround(image.size.height * image.scale) : 0;
}

static UIImage *FRSRotateDegrees(UIImage *image, float degrees)
{
  if (image == nil) {
    return nil;
  }
  float deg = fmodf(degrees, 360.f);
  if (fabsf(deg) < 0.01f) {
    return image;
  }
  CGFloat radians = deg * (CGFloat)M_PI / 180.f;
  CGFloat w = (CGFloat)FRSPixelW(image);
  CGFloat h = (CGFloat)FRSPixelH(image);
  if (w < 1 || h < 1) {
    return image;
  }
  CGFloat cosA = fabs(cos(radians));
  CGFloat sinA = fabs(sin(radians));
  CGFloat outW = floor(w * cosA + h * sinA);
  CGFloat outH = floor(w * sinA + h * cosA);
  if (outW < 1 || outH < 1) {
    return image;
  }
  UIGraphicsBeginImageContextWithOptions(CGSizeMake(outW, outH), YES, 1);
  CGContextRef ctx = UIGraphicsGetCurrentContext();
  CGContextTranslateCTM(ctx, outW * 0.5, outH * 0.5);
  CGContextRotateCTM(ctx, radians);
  [image drawInRect:CGRectMake(-w * 0.5, -h * 0.5, w, h)];
  UIImage *rotated = UIGraphicsGetImageFromCurrentImageContext();
  UIGraphicsEndImageContext();
  return rotated ?: image;
}

static UIImage *FRSScaleMax(UIImage *image, int maxEdge)
{
  if (image == nil) {
    return nil;
  }
  CGFloat w = (CGFloat)FRSPixelW(image);
  CGFloat h = (CGFloat)FRSPixelH(image);
  CGFloat edge = MAX(w, h);
  if (edge <= maxEdge || maxEdge < 1) {
    return image;
  }
  CGFloat scale = (CGFloat)maxEdge / edge;
  CGFloat nw = MAX(1, floor(w * scale));
  CGFloat nh = MAX(1, floor(h * scale));
  UIGraphicsBeginImageContextWithOptions(CGSizeMake(nw, nh), YES, 1);
  [image drawInRect:CGRectMake(0, 0, nw, nh)];
  UIImage *scaled = UIGraphicsGetImageFromCurrentImageContext();
  UIGraphicsEndImageContext();
  return scaled ?: image;
}

/** RN FRSApplyLiveTransform: bake orientation, rotate, scale long edge, force owned CGImage. */
static UIImage *FRSApplyLiveTransform(UIImage *src, float rotateDegrees, int maxEdge)
{
  UIImage *frame = FRSFixOrientation(src);
  if (frame == nil) {
    return nil;
  }
  frame = FRSRotateDegrees(frame, rotateDegrees);
  frame = FRSScaleMax(frame, MAX(1, maxEdge));
  // Own a scale-1 copy so callers can release the source JPEG UIImage.
  CGFloat w = (CGFloat)FRSPixelW(frame);
  CGFloat h = (CGFloat)FRSPixelH(frame);
  if (w < 1 || h < 1) {
    return frame;
  }
  UIGraphicsBeginImageContextWithOptions(CGSizeMake(w, h), YES, 1);
  [frame drawInRect:CGRectMake(0, 0, w, h)];
  UIImage *owned = UIGraphicsGetImageFromCurrentImageContext();
  UIGraphicsEndImageContext();
  return owned ?: frame;
}

static char *FRSDupNSString(NSString *value)
{
  const char *utf8 = [(value ?: @"") UTF8String];
  if (utf8 == NULL) {
    return strdup("");
  }
  return strdup(utf8);
}

static char *FRSDupCString(const char *value)
{
  return strdup(value ? value : "");
}

void FaceWorkerBridge_SetCallback(FaceWorkerEventCallback callback)
{
  s_callback = callback;
}

void FaceWorkerBridge_InstallHandler(void)
{
  }

void FaceWorkerBridge_ClearHandler(void)
{
  }

int FaceWorkerBridge_Start(float matchThreshold)
{
  [FaceSdkCompat stopWorker];
  s_videoFrameBusy = NO;
  FaceWorkerBridge_InstallHandler();
  [FaceSdkCompat startWorker:matchThreshold];
  return 0;
}

void FaceWorkerBridge_Stop(void)
{
  [FaceSdkCompat stopWorker];
  FaceWorkerBridge_ClearHandler();
  s_videoFrameBusy = NO;
}

char *FaceWorkerBridge_Detect(const void *bytes, int length, unsigned long long flags)
{
  UIImage *image = FRSImageFromBytes(bytes, length);
  if (image == nil) {
    return strdup("");
  }
  NSString *json = [FaceSdkCompat detectJson:image flags:(unsigned long long)flags];
  return FRSDupNSString(json);
}

/** Legacy detect flags as the `param` JSON FaceSdkCompat reads, plus the template keys. */
static NSString *FRSParamFromFlags(unsigned long long flags, int templates, int sortBySize)
{
  NSMutableDictionary *param = [NSMutableDictionary dictionary];
  if (flags == 0 || flags >= 0xffffffffULL) {
    param[@"allAttributes"] = @YES;
  } else {
    param[@"check_pose"] = @((flags & 0x01) != 0);
    param[@"check_landmarks"] = @((flags & 0x02) != 0);
    param[@"estimate_age_gender"] = @(((flags & 0x04) != 0) || ((flags & 0x08) != 0));
    param[@"check_emotion"] = @((flags & 0x10) != 0);
    param[@"check_quality"] = @(((flags & 0x20) != 0) || ((flags & 0x40) != 0));
    param[@"check_eye_closeness"] = @((flags & 0x80) != 0);
    param[@"check_liveness"] = @(((flags & 0x100) != 0) || ((flags & 0x200) != 0));
  }
  param[@"templates"] = @(MAX(0, templates));
  param[@"sortBySize"] = @(sortBySize != 0);
  NSData *data = [NSJSONSerialization dataWithJSONObject:param options:0 error:nil];
  return data ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"";
}

char *FaceWorkerBridge_DetectWithTemplates(
    const void *bytes,
    int length,
    unsigned long long flags,
    int templates,
    int sortBySize)
{
  UIImage *image = FRSImageFromBytes(bytes, length);
  if (image == nil) {
    return strdup("");
  }
  NSString *json = [FaceSdkCompat detectJson:image
                                       param:FRSParamFromFlags(flags, templates, sortBySize)];
  return FRSDupNSString(json);
}

char *FaceWorkerBridge_LicenseStatus(void)
{
  return FRSDupNSString([FaceSdkCompat licenseStatusJson]);
}

int FaceWorkerBridge_SyncWorker(const char *featuresB64Lines, float matchThreshold)
{
  NSMutableArray<NSData *> *features = [NSMutableArray array];
  if (featuresB64Lines != NULL) {
    NSString *joined = [NSString stringWithUTF8String:featuresB64Lines] ?: @"";
    for (NSString *line in [joined componentsSeparatedByString:@"\n"]) {
      if (line.length == 0) {
        continue;
      }
      NSData *data = [[NSData alloc]
          initWithBase64EncodedString:line
                              options:NSDataBase64DecodingIgnoreUnknownCharacters];
      if (data.length > 0) {
        [features addObject:data];
      }
    }
  }
  [FaceSdkCompat syncWorker:features matchThreshold:matchThreshold];
  // 0 is success, as the Android bridges return. The hosts gate their frame loop on
  // `synced == 0`, so returning a count stalled them for any non-empty gallery.
  return 0;
}

char *FaceWorkerBridge_Extract(const void *bytes, int length)
{
  UIImage *image = FRSImageFromBytes(bytes, length);
  if (image == nil) {
    return strdup("");
  }
  NSString *json = [FaceSdkCompat extractFeatureJson:image];
  return FRSDupNSString(json);
}

/** Match RN/Android Utils.cropFace: 1.4× square window scaled to 200×200. */
static UIImage *FRSCropFace(UIImage *image, int x1i, int y1i, int x2i, int y2i)
{
  if (image == nil) {
    return nil;
  }
  CGFloat x1 = x1i, y1 = y1i, x2 = x2i, y2 = y2i;
  CGFloat iw = image.size.width * image.scale;
  CGFloat ih = image.size.height * image.scale;
  if (iw < 2 || ih < 2) {
    return nil;
  }
  // The face box with 12% padding, at the source resolution — what ImageUtils.cropFace stores
  // natively. A 1.4x square scaled to 200x200 framed the face wider and threw away detail.
  CGFloat pad = MAX(4.0, MIN(x2 - x1, y2 - y1) * 0.12);
  CGFloat left = MAX(0.0, x1 - pad);
  CGFloat top = MAX(0.0, y1 - pad);
  CGFloat right = MIN(iw, x2 + pad);
  CGFloat bottom = MIN(ih, y2 + pad);
  if (right - left <= 1 || bottom - top <= 1 || image.CGImage == NULL) {
    return nil;
  }
  CGRect rect = CGRectMake(left, top, right - left, bottom - top);
  CGImageRef cg = CGImageCreateWithImageInRect(image.CGImage, rect);
  if (cg == NULL) {
    return nil;
  }
  UIImage *cropped = [UIImage imageWithCGImage:cg scale:1.0 orientation:UIImageOrientationUp];
  CGImageRelease(cg);
  return cropped;
}

float FaceWorkerBridge_Similarity(const char *feature1B64, const char *feature2B64)
{
  if (feature1B64 == NULL || feature2B64 == NULL) {
    return -1;
  }
  @autoreleasepool {
    NSData *left = [[NSData alloc]
        initWithBase64EncodedString:[NSString stringWithUTF8String:feature1B64]
                            options:NSDataBase64DecodingIgnoreUnknownCharacters];
    NSData *right = [[NSData alloc]
        initWithBase64EncodedString:[NSString stringWithUTF8String:feature2B64]
                            options:NSDataBase64DecodingIgnoreUnknownCharacters];
    if (left.length == 0 || right.length == 0) {
      return -1;
    }
    @try {
      float value = [FaceRecognitionSDK similarityWithFeature1:left feature2:right];
      return isnan(value) ? -1 : value;
    } @catch (NSException *e) {
      return -1;
    }
  }
}

char *FaceWorkerBridge_CropFace(const void *bytes, int length, int x1, int y1, int x2, int y2)
{
  UIImage *image = FRSImageFromBytes(bytes, length);
  UIImage *cropped = FRSCropFace(image, x1, y1, x2, y2);
  if (cropped == nil) {
    return strdup("");
  }
  NSData *data = UIImageJPEGRepresentation(cropped, 0.85);
  if (data == nil) {
    return strdup("");
  }
  NSString *b64 = [data base64EncodedStringWithOptions:0];
  return FRSDupNSString(b64);
}

char *FaceWorkerBridge_ApplyLiveFrame(
    const void *bytes,
    int length,
    float rotateDegrees,
    int maxEdge,
    int feedWorker)
{
  if (feedWorker && s_videoFrameBusy) {
    // RN: return last size with ingested=false while previous add is in flight.
    char buf[64];
    snprintf(buf, sizeof(buf), "0|%ld|%ld", (long)s_lastLiveW, (long)s_lastLiveH);
    return FRSDupCString(buf);
  }

  UIImage *raw = FRSImageFromBytes(bytes, length);
  UIImage *prepared = FRSApplyLiveTransform(raw, rotateDegrees, maxEdge);
  if (prepared == nil) {
    return FRSDupCString("0|0|0");
  }

  s_lastLiveImage = prepared;
  s_lastLiveW = FRSPixelW(prepared);
  s_lastLiveH = FRSPixelH(prepared);

  if (feedWorker) {
    s_videoFrameBusy = YES;
    UIImage *toFeed = prepared;
    dispatch_async(FRSWorkerQueue(), ^{
      @try {
        (void)({ NSString *_ev = [FaceSdkCompat workerFrameJson:toFeed]; if (_ev && s_callback) { s_callback(_ev.UTF8String); } 0; });
      } @catch (__unused NSException *e) {
      }
      s_videoFrameBusy = NO;
    });
  }

  char buf[64];
  snprintf(buf, sizeof(buf), "1|%ld|%ld", (long)s_lastLiveW, (long)s_lastLiveH);
  return FRSDupCString(buf);
}

char *FaceWorkerBridge_ExportLastLiveFrame(void)
{
  UIImage *image = s_lastLiveImage;
  if (image == nil) {
    return strdup("");
  }
  NSData *data = UIImageJPEGRepresentation(image, 0.85);
  if (data == nil) {
    return strdup("");
  }
  return FRSDupNSString([data base64EncodedStringWithOptions:0]);
}

char *FaceWorkerBridge_LastLiveSize(void)
{
  char buf[64];
  snprintf(buf, sizeof(buf), "%ld|%ld", (long)s_lastLiveW, (long)s_lastLiveH);
  return FRSDupCString(buf);
}

void FaceWorkerBridge_FreeString(char *value)
{
  if (value != NULL) {
    free(value);
  }
}
