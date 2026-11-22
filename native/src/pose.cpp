#include <opencv2/calib3d.hpp>
#include <opencv2/features2d.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/video/tracking.hpp>
#include <cmath>
#include <memory>
#include <vector>

#if defined(_WIN32)
#define GG_EXPORT extern "C" __declspec(dllexport)
#else
#define GG_EXPORT extern "C" __attribute__((visibility("default")))
#endif

struct PoseState {
    cv::Mat previousGray;
    std::vector<cv::Point2f> previousPixels;
    std::vector<cv::Point3f> previousPoints;
    double x = 0, z = 0, yaw = 0, lastGood = 0, lostSince = -1;
    bool initialized = false;
};

static cv::Mat gravityRotation(const float* normal) {
    cv::Vec3d up(normal[0], normal[1], normal[2]);
    const double upNorm = cv::norm(up);
    if (!std::isfinite(upNorm) || upNorm < 1e-6) up = cv::Vec3d(0, -1, 0);
    else up /= upNorm;
    cv::Vec3d right = std::abs(up[0]) > 0.95 ? cv::Vec3d(0, 0, 1) : cv::Vec3d(1, 0, 0);
    right -= up * right.dot(up);
    const double rightNorm = cv::norm(right);
    if (!std::isfinite(rightNorm) || rightNorm < 1e-6) right = cv::Vec3d(1, 0, 0);
    else right /= rightNorm;
    cv::Vec3d forward = -right.cross(up);
    cv::Mat r = (cv::Mat_<double>(3, 3) << right[0], right[1], right[2], up[0], up[1], up[2], forward[0], forward[1], forward[2]);
    return r;
}

static void lift(const cv::Mat& disparity, const cv::Mat& confidence, const double* q,
                 const std::vector<cv::Point2f>& pixels, std::vector<cv::Point2f>& validPixels,
                 std::vector<cv::Point3f>& points) {
    for (const auto& pixel : pixels) {
        int x = cvRound(pixel.x), y = cvRound(pixel.y);
        if (x < 0 || y < 0 || x >= disparity.cols || y >= disparity.rows || confidence.at<unsigned char>(y, x) != 2) continue;
        double d = disparity.at<float>(y, x); if (!(d > 0)) continue;
        double X = q[0] * pixel.x + q[1] * pixel.y + q[2] * d + q[3];
        double Y = q[4] * pixel.x + q[5] * pixel.y + q[6] * d + q[7];
        double Z = q[8] * pixel.x + q[9] * pixel.y + q[10] * d + q[11];
        double W = q[12] * pixel.x + q[13] * pixel.y + q[14] * d + q[15];
        if (!std::isfinite(W) || W <= 1e-9) continue;
        Z /= W; if (Z < .5 || Z > 3) continue;
        validPixels.push_back(pixel); points.emplace_back(float(X / W), float(Y / W), float(Z));
    }
}

static void selectFeatures(const cv::Mat& gray, const cv::Mat& confidence, const cv::Mat& disparity,
                           std::vector<cv::Point2f>& pixels) {
    cv::Mat mask = (confidence == 2) & (disparity > 0); mask.convertTo(mask, CV_8U, 255);
    for (int by = 0; by < 4; ++by) for (int bx = 0; bx < 4; ++bx) {
        int x0 = bx * gray.cols / 4, x1 = (bx + 1) * gray.cols / 4;
        int y0 = by * gray.rows / 4, y1 = (by + 1) * gray.rows / 4;
        std::vector<cv::Point2f> found; cv::goodFeaturesToTrack(gray(cv::Rect(x0, y0, x1 - x0, y1 - y0)), found, 30, .01, 5, mask(cv::Rect(x0, y0, x1 - x0, y1 - y0)));
        for (auto& point : found) pixels.emplace_back(point.x + x0, point.y + y0);
    }
}

GG_EXPORT void* gg_pose_create() { return new PoseState(); }
GG_EXPORT void gg_pose_destroy(void* handle) { delete static_cast<PoseState*>(handle); }

static int poseUpdateImpl(void* handle, const unsigned char* grayBytes, const float* disparityBytes,
                             const unsigned char* confidenceBytes, int width, int height, const double* q,
                             const float* previousFloorNormal, const float* currentFloorNormal,
                             int floorUsable, double timestamp, double* x, double* z, double* yaw,
                             int* trackingOk, int* inliers, int* resetMap) {
    if (!handle || !grayBytes || !disparityBytes || !confidenceBytes || !q || !x || !z || !yaw || !trackingOk || !inliers || !resetMap || width < 16 || height < 16) return 1;
    for (int i = 0; i < 16; ++i) if (!std::isfinite(q[i])) return 1;
    auto& state = *static_cast<PoseState*>(handle); *trackingOk = 0; *inliers = 0; *resetMap = 0;
    cv::Mat gray(height, width, CV_8U, const_cast<unsigned char*>(grayBytes));
    cv::Mat disparity(height, width, CV_32F, const_cast<float*>(disparityBytes));
    cv::Mat confidence(height, width, CV_8U, const_cast<unsigned char*>(confidenceBytes));
    if (!floorUsable) { if (state.lostSince < 0) state.lostSince = timestamp; if (timestamp - state.lostSince >= 2) { state.initialized = false; state.previousGray.release(); } *x = state.x; *z = state.z; *yaw = state.yaw; return 0; }
    if (!state.initialized) {
        std::vector<cv::Point2f> pixels, validPixels; std::vector<cv::Point3f> points; selectFeatures(gray, confidence, disparity, pixels); lift(disparity, confidence, q, pixels, validPixels, points);
        if (points.size() < 25) { if (state.lostSince < 0) state.lostSince = timestamp; *x = state.x; *z = state.z; *yaw = state.yaw; return 0; }
        bool recovering = state.lostSince >= 0; if (recovering) *resetMap = 1;
        state.previousGray = gray.clone(); state.previousPixels = std::move(validPixels); state.previousPoints = std::move(points); state.initialized = true; state.lastGood = timestamp; state.lostSince = -1; *trackingOk = 1; *x = state.x; *z = state.z; *yaw = state.yaw; return 0;
    }
    std::vector<cv::Point2f> target; std::vector<unsigned char> status; std::vector<float> error;
    cv::calcOpticalFlowPyrLK(state.previousGray, gray, state.previousPixels, target, status, error, cv::Size(15, 15), 3, cv::TermCriteria(cv::TermCriteria::COUNT | cv::TermCriteria::EPS, 20, .03));
    std::vector<cv::Point3f> object; std::vector<cv::Point2f> image, source; for (size_t i = 0; i < target.size(); ++i) if (status[i] && target[i].x >= 0 && target[i].x < width && target[i].y >= 0 && target[i].y < height) { object.push_back(state.previousPoints[i]); image.push_back(target[i]); source.push_back(state.previousPixels[i]); }
    // Match pose_tracker.py::_camera_matrix exactly. Q is row-major here:
    // focal=Q[2,3], cx=-Q[0,3], cy=-Q[1,3].
    cv::Mat camera = (cv::Mat_<double>(3, 3) << q[11], 0, -q[3], 0, q[11], -q[7], 0, 0, 1), rvec, tvec, inlierIndex;
    bool solved = object.size() >= 25 && cv::solvePnPRansac(object, image, camera, cv::noArray(), rvec, tvec, false, 100, 2, .99, inlierIndex, cv::SOLVEPNP_EPNP);
    if (solved && inlierIndex.rows >= 25) {
        cv::Mat rotation; cv::Rodrigues(rvec, rotation); cv::Mat center = -rotation.t() * tvec; cv::Mat sourceGravity = gravityRotation(previousFloorNormal); cv::Mat currentGravity = gravityRotation(currentFloorNormal); cv::Mat centerGravity = sourceGravity * center; cv::Mat relative = sourceGravity * rotation.t() * currentGravity.t(); cv::Mat forward = relative * (cv::Mat_<double>(3, 1) << 0, 0, 1); double relativeYaw = std::atan2(forward.at<double>(0), forward.at<double>(2)); double c = std::cos(state.yaw), s = std::sin(state.yaw); state.x += c * centerGravity.at<double>(0) + s * centerGravity.at<double>(2); state.z += -s * centerGravity.at<double>(0) + c * centerGravity.at<double>(2); state.yaw += relativeYaw; state.lastGood = timestamp; state.lostSince = -1; *trackingOk = 1; *inliers = inlierIndex.rows;
    } else { if (state.lostSince < 0) state.lostSince = timestamp; if (timestamp - state.lostSince >= 2) { state.initialized = false; state.previousGray.release(); } }
    state.previousGray = gray.clone(); state.previousPixels = std::move(image); state.previousPoints.clear(); std::vector<cv::Point2f> validPixels; lift(disparity, confidence, q, state.previousPixels, validPixels, state.previousPoints); state.previousPixels = std::move(validPixels); *x = state.x; *z = state.z; *yaw = state.yaw; return 0;
}

GG_EXPORT int gg_pose_update(void* handle, const unsigned char* grayBytes, const float* disparityBytes,
                             const unsigned char* confidenceBytes, int width, int height, const double* q,
                             const float* previousFloorNormal, const float* currentFloorNormal,
                             int floorUsable, double timestamp, double* x, double* z, double* yaw,
                             int* trackingOk, int* inliers, int* resetMap) {
    try {
        return poseUpdateImpl(handle, grayBytes, disparityBytes, confidenceBytes, width, height, q,
                              previousFloorNormal, currentFloorNormal, floorUsable, timestamp,
                              x, z, yaw, trackingOk, inliers, resetMap);
    } catch (const cv::Exception&) {
        // OpenCV treats malformed/temporarily empty tracking input as an
        // ordinary rejected frame in the Python implementation. Do the same
        // here: preserve the last pose, discard the bad tracking state, and
        // let the next valid frame reinitialize without tearing down mapping.
        if (handle && x && z && yaw && trackingOk && inliers && resetMap) {
            auto& state = *static_cast<PoseState*>(handle);
            state.initialized = false;
            state.previousGray.release();
            state.previousPixels.clear();
            state.previousPoints.clear();
            state.lostSince = timestamp;
            *x = state.x; *z = state.z; *yaw = state.yaw;
            *trackingOk = 0; *inliers = 0; *resetMap = 0;
            return 0;
        }
        return 2;
    } catch (...) {
        if (handle && x && z && yaw && trackingOk && inliers && resetMap) {
            auto& state = *static_cast<PoseState*>(handle);
            state.initialized = false;
            state.previousGray.release();
            state.previousPixels.clear();
            state.previousPoints.clear();
            state.lostSince = timestamp;
            *x = state.x; *z = state.z; *yaw = state.yaw;
            *trackingOk = 0; *inliers = 0; *resetMap = 0;
            return 0;
        }
        return 3;
    }
}
