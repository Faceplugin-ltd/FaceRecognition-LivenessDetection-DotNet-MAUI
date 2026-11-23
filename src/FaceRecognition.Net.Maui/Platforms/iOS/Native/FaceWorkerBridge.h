#pragma once

#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void (*FaceWorkerEventCallback)(const char *utf8Json);

void FaceWorkerBridge_SetCallback(FaceWorkerEventCallback callback);
void FaceWorkerBridge_InstallHandler(void);
void FaceWorkerBridge_ClearHandler(void);
int FaceWorkerBridge_Start(float matchThreshold);
void FaceWorkerBridge_Stop(void);

/** Detect / extract with FRSFixOrientation bake. Caller must FaceWorkerBridge_FreeString. */
char *FaceWorkerBridge_Detect(const void *bytes, int length, unsigned long long flags);
char *FaceWorkerBridge_Extract(const void *bytes, int length);
/**
 * Detect and, on the first `templates` faces, take a recognition template from the SAME open
 * engine image — what the native kits do. `sortBySize` puts the largest face first before the
 * templates are taken. Caller must FaceWorkerBridge_FreeString.
 */
char *FaceWorkerBridge_DetectWithTemplates(
    const void *bytes,
    int length,
    unsigned long long flags,
    int templates,
    int sortBySize);
/** Similarity of two base64 templates, 0..1; -1 when either cannot be read. */
float FaceWorkerBridge_Similarity(const char *feature1B64, const char *feature2B64);

/** RN FRSCropFace — 1.4× square → 200×200 JPEG. Caller must FaceWorkerBridge_FreeString (base64). */
char *FaceWorkerBridge_CropFace(const void *bytes, int length, int x1, int y1, int x2, int y2);

/**
 * RN applyLiveFrame: bake + rotate + maxEdge scale, cache last frame, optionally feed VideoWorker.
 * Returns strdup "1|width|height" or "0|0|0". Caller must FaceWorkerBridge_FreeString.
 */
char *FaceWorkerBridge_ApplyLiveFrame(
    const void *bytes,
    int length,
    float rotateDegrees,
    int maxEdge,
    int feedWorker);

/** JPEG of last prepared live frame as base64, or "". Caller must FaceWorkerBridge_FreeString. */
char *FaceWorkerBridge_ExportLastLiveFrame(void);

/** "width|height" of last live frame, or "0|0". Caller must FaceWorkerBridge_FreeString. */
char *FaceWorkerBridge_LastLiveSize(void);

/** License state as JSON, from FaceSdkCompat. Caller must FaceWorkerBridge_FreeString. */
char *FaceWorkerBridge_LicenseStatus(void);

/** Hands the VideoWorker emulation the enrolled templates (base64, newline separated). */
int FaceWorkerBridge_SyncWorker(const char *featuresB64Lines, float matchThreshold);

void FaceWorkerBridge_FreeString(char *value);

#ifdef __cplusplus
}
#endif
