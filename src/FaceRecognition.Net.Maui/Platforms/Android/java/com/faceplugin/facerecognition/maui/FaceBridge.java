package com.faceplugin.facerecognition.maui;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Matrix;
import android.util.Base64;

import com.faceplugin.facerecognitionsdk.FaceRecognitionSDK;

import java.io.ByteArrayOutputStream;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;

/** Thin Java entry the MAUI app calls. The AAR stays the Face Recognition SDK. */
public final class FaceBridge {
    private static final ArrayDeque<String> EVENTS = new ArrayDeque<>();
    private static Bitmap lastLiveBitmap;

    private FaceBridge() {}

    /**
     * The engine no longer has a VideoWorker to listen to. Tracking events are produced
     * per frame by {@link FaceSdkCompat} and queued here, so pollEvent is unchanged.
     */
    public static void installListener() {
        synchronized (EVENTS) {
            EVENTS.clear();
        }
    }

    private static void queueEvent(String json) {
        if (json == null) {
            return;
        }
        synchronized (EVENTS) {
            EVENTS.addLast(json);
            while (EVENTS.size() > 8) {
                EVENTS.removeFirst();
            }
        }
    }

    public static String pollEvent() {
        synchronized (EVENTS) {
            return EVENTS.isEmpty() ? "" : EVENTS.removeFirst();
        }
    }

    public static String machineCode(Context context) {
        String value = FaceRecognitionSDK.machineCode(context);
        return value == null ? "" : value;
    }

    public static int activate(Context context, String license) {
        return FaceRecognitionSDK.activate(context, license);
    }

    public static int init(Context context) throws java.io.IOException {
        return FaceRecognitionSDK.init(context);
    }

    public static String lastError() {
        String value = FaceRecognitionSDK.lastLicenseError();
        return value == null ? "" : value;
    }

    public static String licenseStatus() {
        String value = FaceSdkCompat.licenseStatusJson();
        return value == null ? "" : value;
    }

    public static int setLandmarkMode(int mode) {
        // The engine always returns the full point set now; the mode is accepted and
        // ignored so the C# layer needs no change.
        return 0;
    }

    public static String detect(Bitmap bitmap, int flags) {
        String value = FaceSdkCompat.detectJson(bitmap, flags);
        return value == null ? "" : value;
    }

    /**
        * Detection with a recognition template on the first {@code templates} faces, taken
        * from the same open image. See {@link FaceSdkCompat#detectJson}.
        */
    public static String detectWithTemplates(Bitmap bitmap, int flags, int templates, boolean sortBySize) {
        String value = FaceSdkCompat.detectJson(bitmap, flags, templates, sortBySize);
        return value == null ? "" : value;
    }

    public static String extractFeature(Bitmap bitmap) {
        String value = FaceSdkCompat.extractFeatureJson(bitmap);
        return value == null ? "" : value;
    }

    /** Flutter/RN cropFace — 1.4× square scaled to 200×200 JPEG base64. */
    public static String cropFaceBase64(Bitmap src, int x1, int y1, int x2, int y2) {
        Bitmap crop = cropFaceBitmap(src, x1, y1, x2, y2);
        if (crop == null) {
            return "";
        }
        Bitmap scaled = null;
        try {
            scaled = Bitmap.createScaledBitmap(crop, 200, 200, true);
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            scaled.compress(Bitmap.CompressFormat.JPEG, 85, out);
            return Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP);
        } finally {
            if (scaled != null && scaled != crop && !scaled.isRecycled()) {
                scaled.recycle();
            }
            if (crop != src && !crop.isRecycled()) {
                crop.recycle();
            }
        }
    }

    private static Bitmap cropFaceBitmap(Bitmap src, int x1, int y1, int x2, int y2) {
        if (src == null) {
            return null;
        }
        int centerX = (x1 + x2) / 2;
        int centerY = (y1 + y2) / 2;
        int cropWidth = (int) ((x2 - x1) * 1.4f);
        if (cropWidth < 2) {
            cropWidth = Math.max(2, Math.max(x2 - x1, y2 - y1));
        }
        int cropX1 = Math.max(0, centerX - cropWidth / 2);
        int cropY1 = Math.max(0, centerY - cropWidth / 2);
        int cropX2 = Math.min(src.getWidth() - 1, centerX + cropWidth / 2);
        int cropY2 = Math.min(src.getHeight() - 1, centerY + cropWidth / 2);
        int w = cropX2 - cropX1 + 1;
        int h = cropY2 - cropY1 + 1;
        if (w <= 1 || h <= 1) {
            return null;
        }
        return Bitmap.createBitmap(src, cropX1, cropY1, w, h);
    }

    public static String quality(Bitmap bitmap) {
        String value = FaceSdkCompat.qualityJson(bitmap);
        return value == null ? "" : value;
    }

    public static float similarity(byte[] a, byte[] b) {
        return FaceRecognitionSDK.similarity(a, b);
    }

    public static int startVideoWorker(float threshold) {
        // Flutter/RN: stop any previous worker, then install listener and start.
        FaceSdkCompat.stopWorker();
        synchronized (EVENTS) {
            EVENTS.clear();
        }
        installListener();
        FaceSdkCompat.startWorker(threshold);
        return 0;
    }

    public static void stopVideoWorker() {
        FaceSdkCompat.stopWorker();
        synchronized (EVENTS) {
            EVENTS.clear();
        }
    }

    public static int syncDatabase(String packedBase64, float threshold) {
        List<byte[]> list = new ArrayList<>();
        if (packedBase64 != null && !packedBase64.isEmpty()) {
            for (String part : packedBase64.split("\n")) {
                if (!part.isEmpty()) {
                    list.add(Base64.decode(part, Base64.DEFAULT));
                }
            }
        }
        FaceSdkCompat.syncWorker(list, threshold);
        return 0;
    }

    public static int addFrame(Bitmap bitmap) {
        queueEvent(FaceSdkCompat.workerFrameJson(bitmap));
        return 0;
    }

    /**
     * Same as RN/Flutter applyLiveFrame: rotate + scale long edge, cache, optionally feed worker.
     * Always owns a copy so C# can recycle the decode bitmap without killing the worker frame.
     * Returns "1|width|height" or "0|0|0".
     */
    public static String applyLiveFrame(Bitmap src, float rotateDegrees, int maxEdge, boolean feedWorker) {
        if (src == null) {
            return "0|0|0";
        }
        Bitmap owned = src.copy(src.getConfig() != null ? src.getConfig() : Bitmap.Config.ARGB_8888, false);
        if (owned == null) {
            return "0|0|0";
        }
        Bitmap prepared = applyLiveTransform(owned, rotateDegrees, Math.max(1, maxEdge));
        if (prepared != owned && !owned.isRecycled()) {
            owned.recycle();
        }
        if (lastLiveBitmap != null && lastLiveBitmap != prepared && !lastLiveBitmap.isRecycled()) {
            lastLiveBitmap.recycle();
        }
        lastLiveBitmap = prepared;
        if (feedWorker) {
            queueEvent(FaceSdkCompat.workerFrameJson(prepared));
        }
        return "1|" + prepared.getWidth() + "|" + prepared.getHeight();
    }

    /** JPEG bytes of last prepared live frame (Base64), or empty. */
    public static String exportLastLiveFrameBase64() {
        Bitmap prepared = lastLiveBitmap;
        if (prepared == null) {
            return "";
        }
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        prepared.compress(Bitmap.CompressFormat.JPEG, 85, out);
        return Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP);
    }

    public static String lastLiveSize() {
        Bitmap prepared = lastLiveBitmap;
        if (prepared == null) {
            return "0|0";
        }
        return prepared.getWidth() + "|" + prepared.getHeight();
    }

    /** Rotate by degrees then scale long edge ≤ maxEdge. No front/back branching. */
    private static Bitmap applyLiveTransform(Bitmap src, float rotateDegrees, int maxEdge) {
        Bitmap frame = src;
        float deg = rotateDegrees % 360f;
        if (Math.abs(deg) > 0.01f) {
            Matrix matrix = new Matrix();
            matrix.postRotate(deg);
            Bitmap rotated = Bitmap.createBitmap(frame, 0, 0, frame.getWidth(), frame.getHeight(), matrix, true);
            if (rotated != frame && frame != src && !frame.isRecycled()) {
                frame.recycle();
            }
            frame = rotated;
        }
        return scaleMax(frame, maxEdge);
    }

    private static Bitmap scaleMax(Bitmap src, int maxEdge) {
        int w = src.getWidth();
        int h = src.getHeight();
        int edge = Math.max(w, h);
        if (edge <= maxEdge) {
            return src;
        }
        float scale = maxEdge / (float) edge;
        Bitmap scaled = Bitmap.createScaledBitmap(
            src, Math.max(1, (int) (w * scale)), Math.max(1, (int) (h * scale)), true);
        if (scaled != src && !src.isRecycled()) {
            src.recycle();
        }
        return scaled;
    }

    public static int deinit() {
        FaceRecognitionSDK.close();
        return 0;
    }
}
