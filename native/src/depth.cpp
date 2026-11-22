#include <opencv2/calib3d.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/core/persistence.hpp>
#include <cmath>
#include <cstring>
#include <memory>

#if defined(_WIN32)
#define GG_EXPORT extern "C" __declspec(dllexport)
#else
#define GG_EXPORT extern "C" __attribute__((visibility("default")))
#endif

struct StereoDepth {
    cv::Mat leftX, leftY, rightX, rightY;
    cv::Ptr<cv::StereoSGBM> matcher;
    cv::Mat kernel = cv::Mat::ones(3, 3, CV_8U);
    cv::Mat q;
    int maxDisparity = 48;
    float minDisparity = 3.5f;
    float textureMinimum = 3.0f;
};

GG_EXPORT void* gg_create(const char* calibrationPath) {
    try {
        cv::FileStorage file(calibrationPath, cv::FileStorage::READ);
        if (!file.isOpened()) return nullptr;
        cv::Mat k1, d1, k2, d2, r1, r2, p1, p2, q;
        file["K1"] >> k1; file["D1"] >> d1;
        file["K2"] >> k2; file["D2"] >> d2;
        file["R1"] >> r1; file["R2"] >> r2;
        file["P1"] >> p1; file["P2"] >> p2;
        if (k1.empty() || d1.empty() || k2.empty() || d2.empty() ||
            r1.empty() || r2.empty() || p1.empty() || p2.empty()) return nullptr;
        auto depth = std::make_unique<StereoDepth>();
        cv::Size size(640, 480);
        cv::initUndistortRectifyMap(k1, d1, r1, p1.colRange(0, 3), size, CV_16SC2,
                                    depth->leftX, depth->leftY);
        cv::initUndistortRectifyMap(k2, d2, r2, p2.colRange(0, 3), size, CV_16SC2,
                                    depth->rightX, depth->rightY);
        if (file["Q"].empty()) {
            const double baseline = -p2.at<double>(0, 3) / p2.at<double>(0, 0);
            q = (cv::Mat_<double>(4, 4) <<
                1, 0, 0, -p1.at<double>(0, 2),
                0, 1, 0, -p1.at<double>(1, 2),
                0, 0, 0, p1.at<double>(0, 0),
                0, 0, 1 / baseline, (p1.at<double>(0, 2) - p2.at<double>(0, 2)) / baseline);
        } else {
            file["Q"] >> q;
        }
        if (q.rows != 4 || q.cols != 4) return nullptr;
        q.convertTo(depth->q, CV_64F);
        // Python's DepthMapper scales image coordinates before reprojection.
        depth->q.col(3) *= 0.5;
        const int block = 3;
        depth->matcher = cv::StereoSGBM::create(0, depth->maxDisparity, block, 8 * block * block,
            32 * block * block, 1, 31, 10, 120, 2, cv::StereoSGBM::MODE_SGBM_3WAY);
        return depth.release();
    } catch (const cv::Exception&) { return nullptr; }
}

GG_EXPORT void gg_destroy(void* handle) { delete static_cast<StereoDepth*>(handle); }

GG_EXPORT int gg_process_full(void* handle, const unsigned char* bgr, int width, int height,
                              float* disparityOut, unsigned char* confidenceOut,
                              unsigned char* grayOut, double* qOut, int* validPixels) {
    if (!handle || !bgr || !disparityOut || !confidenceOut || !grayOut || !qOut || !validPixels ||
        width != 1280 || height != 480)
        return 1;
    try {
        auto& depth = *static_cast<StereoDepth*>(handle);
        cv::Mat frame(height, width, CV_8UC3, const_cast<unsigned char*>(bgr));
        cv::Mat rotated; cv::rotate(frame, rotated, cv::ROTATE_180);
        cv::Mat left = rotated(cv::Rect(0, 0, 640, 480));
        cv::Mat right = rotated(cv::Rect(640, 0, 640, 480));
        cv::Mat grayLeft, grayRight, rectLeft, rectRight;
        cv::cvtColor(left, grayLeft, cv::COLOR_BGR2GRAY);
        cv::cvtColor(right, grayRight, cv::COLOR_BGR2GRAY);
        cv::remap(grayLeft, rectLeft, depth.leftX, depth.leftY, cv::INTER_LINEAR);
        cv::remap(grayRight, rectRight, depth.rightX, depth.rightY, cv::INTER_LINEAR);
        cv::Mat smallLeft, smallRight;
        cv::resize(rectLeft, smallLeft, cv::Size(320, 240), 0, 0, cv::INTER_AREA);
        cv::resize(rectRight, smallRight, cv::Size(320, 240), 0, 0, cv::INTER_AREA);
        cv::Mat paddedLeft, paddedRight;
        cv::copyMakeBorder(smallLeft, paddedLeft, 0, 0, 48, 0, cv::BORDER_REPLICATE);
        cv::copyMakeBorder(smallRight, paddedRight, 0, 0, 48, 0, cv::BORDER_REPLICATE);
        cv::Mat raw, disparity;
        depth.matcher->compute(paddedLeft, paddedRight, raw);
        raw(cv::Rect(48, 0, 320, 240)).convertTo(disparity, CV_32F, 1.0 / 16.0);
        cv::threshold(disparity, disparity, depth.minDisparity, 0, cv::THRESH_TOZERO);
        for (int y = 0; y < disparity.rows; ++y) {
            float* row = disparity.ptr<float>(y);
            for (int x = 0; x < disparity.cols; ++x)
                if (x - row[x] < 1) row[x] = 0;
        }
        cv::Mat hi, lo, highInput, lowInput;
        highInput = disparity.clone(); lowInput = disparity.clone();
        lowInput.setTo(10000, disparity <= 0);
        cv::dilate(highInput, hi, depth.kernel);
        cv::erode(lowInput, lo, depth.kernel);
        cv::Mat edge = (hi - lo) > cv::max(1.5, 0.05 * disparity);
        disparity.setTo(0, edge & (disparity > 0));
        cv::Mat blurred, gradient, texture, confidence(disparity.size(), CV_8U, cv::Scalar(0));
        cv::GaussianBlur(smallLeft, blurred, cv::Size(3, 3), 0);
        cv::Sobel(blurred, gradient, CV_32F, 1, 0, 3);
        cv::boxFilter(cv::abs(gradient), texture, CV_32F, cv::Size(7, 7));
        const cv::Mat valid = disparity > 0;
        confidence.setTo(1, valid);
        confidence.setTo(2, valid & (texture >= depth.textureMinimum));
        *validPixels = cv::countNonZero(disparity > 0);
        std::memcpy(disparityOut, disparity.data, 320 * 240 * sizeof(float));
        std::memcpy(confidenceOut, confidence.data, 320 * 240);
        std::memcpy(grayOut, smallLeft.data, 320 * 240);
        std::memcpy(qOut, depth.q.data, 16 * sizeof(double));
        return 0;
    } catch (const cv::Exception&) { return 2; }
}

GG_EXPORT int gg_process(void* handle, const unsigned char* bgr, int width, int height,
                         unsigned char* previewBgr, int* validPixels) {
    if (!previewBgr) return 1;
    float disparity[320 * 240];
    unsigned char confidence[320 * 240], gray[320 * 240];
    double q[16];
    const int result = gg_process_full(handle, bgr, width, height, disparity, confidence, gray, q, validPixels);
    if (result != 0) return result;
    cv::Mat disparityImage(240, 320, CV_32F, disparity);
    cv::Mat scaled, color;
    disparityImage.convertTo(scaled, CV_8U, 255.0 / 48.0);
    cv::applyColorMap(scaled, color, cv::COLORMAP_TURBO);
    color.setTo(cv::Scalar(20, 20, 20), disparityImage <= 0);
    const cv::Mat weak(240, 320, CV_8U, confidence);
    color.setTo(cv::Scalar(7, 7, 7), weak == 1);
    std::memcpy(previewBgr, color.data, 320 * 240 * 3);
    return 0;
}
