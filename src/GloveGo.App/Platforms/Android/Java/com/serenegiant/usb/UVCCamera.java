package com.serenegiant.usb;

import android.view.Surface;

/** Small capture-only bridge for the native UVC driver. */
public final class UVCCamera {
    public static final int FRAME_FORMAT_YUYV = 0;
    public static final int FRAME_FORMAT_MJPEG = 1;
    public static final int PIXEL_FORMAT_RGBX = 3;

    private long mNativePtr;

    static {
        System.loadLibrary("jpeg-turbo1500");
        System.loadLibrary("usb100");
        System.loadLibrary("uvc");
        System.loadLibrary("UVCCamera");
    }

    public UVCCamera() {
        mNativePtr = nativeCreate();
        if (mNativePtr == 0) throw new IllegalStateException("Could not create UVC camera.");
    }

    public void open(int vendorId, int productId, int fileDescriptor, int bus, int address, String usbfs) {
        int result = nativeConnect(mNativePtr, vendorId, productId, fileDescriptor, bus, address, usbfs);
        if (result != 0) throw new IllegalStateException("UVC open failed: " + result);
    }

    public String getSupportedSize() { return nativeGetSupportedSize(mNativePtr); }

    public void setPreviewSize(int width, int height, int minFps, int maxFps, int format, float bandwidth) {
        int result = nativeSetPreviewSize(mNativePtr, width, height, minFps, maxFps, format, bandwidth);
        if (result != 0) throw new IllegalArgumentException("UVC mode rejected: " + width + "x" + height + ", format " + format);
    }

    public void setFrameCallback(IFrameCallback callback, int format) {
        if (nativeSetFrameCallback(mNativePtr, callback, format) != 0)
            throw new IllegalStateException("Could not set UVC frame callback.");
    }

    public void startPreview() {
        if (nativeStartPreview(mNativePtr) != 0) throw new IllegalStateException("Could not start UVC preview.");
    }

    public void close() {
        if (mNativePtr != 0) {
            nativeStopPreview(mNativePtr);
            nativeRelease(mNativePtr);
        }
    }

    public void destroy() {
        close();
        if (mNativePtr != 0) nativeDestroy(mNativePtr);
        mNativePtr = 0;
    }

    private native long nativeCreate();
    private native void nativeDestroy(long id);
    private static native int nativeConnect(long id, int vendorId, int productId, int fileDescriptor, int bus, int address, String usbfs);
    private static native int nativeRelease(long id);
    private static native int nativeSetStatusCallback(long id, IStatusCallback callback);
    private static native int nativeSetButtonCallback(long id, IButtonCallback callback);
    private static native String nativeGetSupportedSize(long id);
    private static native int nativeSetPreviewSize(long id, int width, int height, int minFps, int maxFps, int format, float bandwidth);
    private static native int nativeStartPreview(long id);
    private static native int nativeStopPreview(long id);
    private static native int nativeSetPreviewDisplay(long id, Surface surface);
    private static native int nativeSetFrameCallback(long id, IFrameCallback callback, int format);
}
