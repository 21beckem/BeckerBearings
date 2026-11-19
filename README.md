# GloveGo first hardware milestone

The `src/GloveGo.App` .NET 10 MAUI project targets Windows and Android. Its diagnostic
screen selects a 1280×480 side-by-side stereo camera, processes one frame with
`native/glovego_vision`, and sends/receives bytes over a selected serial device.
The calibration in `Resources/Raw/calibration.json` was transcribed from the
Python `cloud-scan/calibration.npz` (640×480 per eye). Whole-frame rotation is 180°.

The native CMake project requires OpenCV 4.12 with `core`, `imgproc`, and `calib3d`.
Build it for each target architecture and package `glovego_vision` and its
OpenCV runtime libraries with the MAUI target. The Android adapter currently uses
Camera2, which requires the device to expose its external UVC camera through the
system camera provider; the third-party USB camera app may instead use direct UVC.
Android serial currently supports CDC-ACM devices (including a genuine Uno's
ATmega16U2), not CH340/CP210x/FTDI clones.

This is the first hardware diagnostic slice. Floor fitting, tracking, map fusion,
continuous capture, and offline replay are subsequent ports.

## Build

Install .NET 10 with the `maui-android` and `maui-windows` workloads, Visual Studio
C++ tools/CMake, Android SDK API 36, NDK r27c, and OpenCV 4.12 source in
`vendor/opencv`. Configure OpenCV with `BUILD_LIST=core,imgproc,calib3d`,
`BUILD_SHARED_LIBS=OFF`, `BUILD_TESTS=OFF`, `BUILD_ANDROID_EXAMPLES=OFF`, and
`BUILD_JAVA=OFF`. Use its build-tree `OpenCVConfig.cmake` with `native/CMakeLists.txt`.
Windows expects the resulting DLL at `build/native-win/Release/glovego_vision.dll`;
Android ARM64 expects the `.so` at `build/native-android/libglovego_vision.so`.
The MAUI project includes these outputs when present.

The Windows native library has been smoke tested against the Python project's
saved raw stereo pair: it returned 58,842 valid disparity pixels, compared with
58,841 from the Python implementation using the same rotation and pair. The
signed Android APK includes the calibration and ARM64 native library. The hardware milestone still requires a run with the
actual camera and Arduino. The Android Camera2 adapter requires the phone's
firmware to expose its USB camera through Camera2. A direct-UVC camera app
working on the phone does not establish that this adapter will see it.
