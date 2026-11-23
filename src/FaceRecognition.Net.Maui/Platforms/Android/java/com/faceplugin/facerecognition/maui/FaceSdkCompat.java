package com.faceplugin.facerecognition.maui;

import android.graphics.Bitmap;
import android.graphics.Rect;
import android.util.Base64;

import com.faceplugin.facerecognitionsdk.FaceRecognitionSDK;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * The bridge's view of the engine.
 *
 * The SDK's Android API changed: detection runs through {@code FaceRecognitionSDK.Image}, and
 * the VideoWorker, {@code cropFace}, {@code quality}, {@code extractFeature} and the landmark
 * mode were removed. This adapter keeps the JSON the C# layer already parses, so nothing above
 * the bridge changes, and implements what the engine no longer provides.
 *
 * Every call here must already hold the engine's turn — the engine is not re-entrant.
 */
final class FaceSdkCompat {

    /** Smile at or above this reads as "Smiling", matching the Android app. */
    private static final float SMILE_THRESHOLD = 0.5f;

    private static volatile boolean workerRunning;
    private static volatile float workerThreshold = FaceRecognitionSDK.MATCH_THRESHOLD;
    private static volatile List<byte[]> workerDb = new ArrayList<>();

    private FaceSdkCompat() {}

    // ---------------------------------------------------------------- lifecycle

    static String licenseStatusJson() {
        FaceRecognitionSDK.License l = FaceRecognitionSDK.license();
        try {
            JSONObject o = new JSONObject();
            o.put("licensed", l != null && l.licensed);
            o.put("level", l == null ? -1 : l.level);
            o.put("levelName", l == null || l.levelName == null ? "None" : l.levelName);
            o.put("recognition", l != null && l.recognition);
            o.put("liveness", l != null && l.liveness);
            o.put("label", l == null || l.label == null ? "Not licensed" : l.label);
            return o.toString();
        } catch (Throwable t) {
            return "{}";
        }
    }

    // ---------------------------------------------------------------- detection

    /** Faces as a JSON array, in the shape the C# FaceJson parser reads. */
    static String detectJson(Bitmap bitmap, long flags) {
        return detectJson(bitmap, flags, 0, false);
    }

    /**
     * Faces as a JSON array, with a recognition template on the first
     * {@code templates} faces — read from the same open image as the detection, which is what
     * the Android app's kit does.
     *
     * <p>Extracting templates from a second detect would cost a full attribute pass per face
     * and, worse, could attribute a template to the wrong person when two faces are close
     * together or the subject moved between the two passes.
     *
     * @param sortBySize order the faces largest first before the templates are taken
     */
    static String detectJson(Bitmap bitmap, long flags, int templates, boolean sortBySize) {
        JSONArray out = new JSONArray();
        FaceRecognitionSDK.Image image = FaceRecognitionSDK.Image.from(bitmap);
        if (image == null) {
            return out.toString();
        }
        try {
            List<FaceRecognitionSDK.Face> faces = image.detect(optionsFromFlags(flags));
            if (faces != null) {
                List<FaceRecognitionSDK.Face> ordered = new ArrayList<>(faces);
                if (sortBySize) {
                    java.util.Collections.sort(ordered, new java.util.Comparator<FaceRecognitionSDK.Face>() {
                        @Override
                        public int compare(FaceRecognitionSDK.Face a, FaceRecognitionSDK.Face b) {
                            long areaA = (long) a.bounds.width() * a.bounds.height();
                            long areaB = (long) b.bounds.width() * b.bounds.height();
                            return Long.compare(areaB, areaA);
                        }
                    });
                }
                for (int i = 0; i < ordered.size(); i++) {
                    JSONObject json = faceJson(ordered.get(i));
                    if (i < templates) {
                        // Timed per face, so the host can still show detection and extraction
                        // as two steps even though they share one engine pass.
                        long started = System.nanoTime();
                        byte[] bytes = null;
                        try {
                            bytes = image.template(ordered.get(i));
                        } catch (Throwable ignored) {
                            // no template for this face
                        }
                        json.put("templateMs", (System.nanoTime() - started) / 1e6);
                        if (bytes != null && bytes.length > 0) {
                            json.put("template", Base64.encodeToString(bytes, Base64.NO_WRAP));
                            json.put("templateBytes", bytes.length);
                        } else {
                            json.put("template", JSONObject.NULL);
                            json.put("templateBytes", 0);
                        }
                    }
                    out.put(json);
                }
            }
        } catch (Throwable ignored) {
            // A frame the engine refuses is reported as no faces, not as a crash.
        } finally {
            image.close();
        }
        return out.toString();
    }

    /** The strongest face's template, as {@code {"feature":"<base64>"}}. */
    static String extractFeatureJson(Bitmap bitmap) {
        FaceRecognitionSDK.Image image = FaceRecognitionSDK.Image.from(bitmap);
        if (image == null) {
            return "{}";
        }
        try {
            FaceRecognitionSDK.Face face = largest(image.detect(allOptions()));
            if (face == null) {
                return "{}";
            }
            byte[] bytes = image.template(face);
            if (bytes == null || bytes.length == 0) {
                return "{}";
            }
            return new JSONObject()
                    .put("feature", Base64.encodeToString(bytes, Base64.NO_WRAP))
                    .toString();
        } catch (Throwable t) {
            return "{}";
        } finally {
            image.close();
        }
    }

    /** Quality for the strongest face, in the shape the bridge's quality() returned. */
    static String qualityJson(Bitmap bitmap) {
        FaceRecognitionSDK.Image image = FaceRecognitionSDK.Image.from(bitmap);
        if (image == null) {
            return "{}";
        }
        try {
            FaceRecognitionSDK.Face face = largest(image.detect(allOptions()));
            if (face == null) {
                return "{}";
            }
            JSONObject o = new JSONObject();
            o.put("quality", Float.isNaN(face.quality) ? JSONObject.NULL : (double) face.quality);
            o.put("score", Float.isNaN(face.score) ? JSONObject.NULL : (double) face.score);
            return o.toString();
        } catch (Throwable t) {
            return "{}";
        } finally {
            image.close();
        }
    }

    // ---------------------------------------------------------------- VideoWorker, emulated

    static void startWorker(float matchThreshold) {
        workerThreshold = matchThreshold;
        // A new session starts without a gallery: the capture guide never matches frames.
        workerDb = new ArrayList<>();
        workerRunning = true;
    }

    static void stopWorker() {
        workerRunning = false;
        workerDb = new ArrayList<>();
    }

    static void syncWorker(List<byte[]> features, float matchThreshold) {
        workerDb = features;
        workerThreshold = matchThreshold;
    }

    /**
     * One tracking event for a frame, in the JSON shape the worker used to emit, or null when
     * no session is running. The C# capture and identify pages read this unchanged.
     */
    private static long lastMatchMs = 0L;
    private static final long MATCH_INTERVAL_MS = 400L;

    /** At most one gallery scan every MATCH_INTERVAL_MS while tracking. */
    private static boolean dueForMatch() {
        long now = android.os.SystemClock.elapsedRealtime();
        if (now - lastMatchMs < MATCH_INTERVAL_MS) {
            return false;
        }
        lastMatchMs = now;
        return true;
    }

    static String workerFrameJson(Bitmap frame) {
        if (!workerRunning) {
            return null;
        }
        FaceRecognitionSDK.Image image = FaceRecognitionSDK.Image.from(frame);
        if (image == null) {
            return null;
        }
        try {
            // What the native capture guide asks for: a box, the pose and the landmarks it
            // draws. Eye openness is an estimator per frame and the gate does not read it.
            FaceRecognitionSDK.DetectOptions options = new FaceRecognitionSDK.DetectOptions();
            options.pose = true;
            options.points = true;
            List<FaceRecognitionSDK.Face> faces = image.detect(options);
            JSONArray tracked = new JSONArray();
            List<byte[]> db = workerDb;

            if (faces != null) {
                for (int index = 0; index < faces.size(); index++) {
                    FaceRecognitionSDK.Face f = faces.get(index);
                    Rect b = f.bounds;
                    JSONObject o = new JSONObject();
                    o.put("track_id", index);
                    o.put("faceRegion", new JSONObject()
                            .put("x", b.left).put("y", b.top)
                            .put("width", b.width()).put("height", b.height()));
                    o.put("facePose", new JSONObject()
                            .put("yaw", num(f.yaw)).put("pitch", num(f.pitch)).put("roll", num(f.roll)));
                    JSONArray points = new JSONArray();
                    if (f.points != null) {
                        for (int i = 0; i + 1 < f.points.length; i += 2) {
                            points.put(new JSONObject()
                                    .put("x", (double) f.points[i])
                                    .put("y", (double) f.points[i + 1]));
                        }
                    }
                    o.put("facePoints", points);
                    if (!Float.isNaN(f.eyesOpen)) {
                        o.put("eyes_open", (double) f.eyesOpen);
                    }
                    o.put("weak", false);

                    // A template plus a scan of the whole gallery is far too much to do on
                    // every frame; the real VideoWorker matched on its own cadence.
                    if (index == 0 && !db.isEmpty() && dueForMatch()) {
                        byte[] feature = null;
                        try {
                            feature = image.template(f);
                        } catch (Throwable ignored) {
                            // no template for this frame
                        }
                        if (feature != null && feature.length > 0) {
                            int bestIndex = -1;
                            float best = -1f;
                            for (int candidate = 0; candidate < db.size(); candidate++) {
                                float score;
                                try {
                                    score = FaceRecognitionSDK.similarity(feature, db.get(candidate));
                                } catch (Throwable ignored) {
                                    score = -1f;
                                }
                                if (!Float.isNaN(score) && score > best) {
                                    best = score;
                                    bestIndex = candidate;
                                }
                            }
                            if (bestIndex >= 0) {
                                o.put("match", new JSONObject()
                                        .put("matched", best >= workerThreshold)
                                        .put("person_index", bestIndex)
                                        .put("score", (double) best));
                            }
                        }
                    }
                    tracked.put(o);
                }
            }

            return new JSONObject()
                    .put("event", "tracking")
                    .put("frame_width", frame.getWidth())
                    .put("frame_height", frame.getHeight())
                    .put("faces", tracked)
                    .toString();
        } catch (Throwable t) {
            return null;
        } finally {
            image.close();
        }
    }

    // ---------------------------------------------------------------- face to JSON

    private static JSONObject faceJson(FaceRecognitionSDK.Face f) throws Exception {
        Rect b = f.bounds;
        JSONObject o = new JSONObject();
        o.put("x1", b.left);
        o.put("y1", b.top);
        o.put("x2", b.right);
        o.put("y2", b.bottom);
        o.put("score", numOrNull(f.score));
        o.put("yaw", num(f.yaw));
        o.put("roll", num(f.roll));
        o.put("pitch", num(f.pitch));
        if (Float.isNaN(f.liveness)) {
            o.put("liveness", 0.0);
            o.put("livenessLabel", "");
        } else {
            o.put("liveness", (double) f.liveness);
            o.put("livenessLabel", f.liveness > FaceRecognitionSDK.LIVENESS_THRESHOLD ? "Live" : "Fake");
        }
        o.put("quality", numOrNull(f.quality));
        o.put("face_quality", Float.isNaN(f.quality) ? num(f.score) : (double) f.quality);
        o.put("face_luminance", 0.0);
        if (Float.isNaN(f.eyesOpen)) {
            o.put("left_eye_closed", 0.0);
            o.put("right_eye_closed", 0.0);
        } else {
            double closed = 1.0 - f.eyesOpen;
            o.put("eyes_open", (double) f.eyesOpen);
            o.put("left_eye_closed", closed);
            o.put("right_eye_closed", closed);
        }
        o.put("smile", numOrNull(f.smile));
        o.put("emotionLabel",
                Float.isNaN(f.smile) ? "" : (f.smile >= SMILE_THRESHOLD ? "Smiling" : "Neutral"));
        o.put("mouth_opened", numOrNull(f.mouthOpen));
        o.put("face_occlusion", 0.0);
        o.put("age", f.age > 0 ? f.age : 0);
        o.put("gender", f.gender);
        o.put("genderLabel", f.genderLabel == null ? "" : f.genderLabel);
        o.put("maskLabel", "");
        o.put("qualityLabel", "");
        // Open/Closed words depend on the app's eye threshold, so the engine does not label eyes.
        o.put("eyesLeftLabel", "");
        o.put("eyesRightLabel", "");
        o.put("glassesLabel", "");
        o.put("sunglassesLabel", "");
        o.put("occlusionLabel", "");
        o.put("attributes", new JSONObject());
        JSONArray landmarks = new JSONArray();
        int count = f.points == null ? 0 : f.points.length / 2;
        for (int i = 0; i < count * 2; i++) {
            landmarks.put((double) f.points[i]);
        }
        o.put("landmarkCount", count);
        o.put("landmarks", landmarks);
        return o;
    }

    // ---------------------------------------------------------------- helpers

    static FaceRecognitionSDK.DetectOptions allOptions() {
        FaceRecognitionSDK.DetectOptions o = new FaceRecognitionSDK.DetectOptions();
        o.liveness = true;
        o.quality = true;
        o.pose = true;
        o.ageGender = true;
        o.eyes = true;
        o.expression = true;
        o.points = true;
        return o;
    }

    /** The old flag bitmask. Anything out of range asks for everything. */
    private static FaceRecognitionSDK.DetectOptions optionsFromFlags(long flags) {
        if (flags < 0 || flags >= 0xffffffffL) {
            return allOptions();
        }
        FaceRecognitionSDK.DetectOptions o = new FaceRecognitionSDK.DetectOptions();
        o.pose = (flags & 0x01) != 0;
        o.points = (flags & 0x02) != 0;
        o.ageGender = (flags & 0x04) != 0 || (flags & 0x08) != 0;
        o.expression = (flags & 0x10) != 0;
        o.quality = (flags & 0x20) != 0 || (flags & 0x40) != 0;
        o.eyes = (flags & 0x80) != 0;
        o.liveness = (flags & 0x100) != 0 || (flags & 0x200) != 0;
        return o;
    }

    private static FaceRecognitionSDK.Face largest(List<FaceRecognitionSDK.Face> faces) {
        if (faces == null || faces.isEmpty()) {
            return null;
        }
        FaceRecognitionSDK.Face best = faces.get(0);
        for (FaceRecognitionSDK.Face f : faces) {
            if ((long) f.bounds.width() * f.bounds.height()
                    > (long) best.bounds.width() * best.bounds.height()) {
                best = f;
            }
        }
        return best;
    }

    private static double num(float v) {
        return Float.isNaN(v) ? 0.0 : (double) v;
    }

    private static Object numOrNull(float v) {
        return Float.isNaN(v) ? JSONObject.NULL : (Double) (double) v;
    }
}
