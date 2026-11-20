using Java.Interop;
using Android.Runtime;

namespace GloveGo.App.Platforms.Android;

[Register("com/serenegiant/usb/UVCCamera", DoNotGenerateAcw = true)]
internal sealed unsafe class JavaUvcCamera : Java.Lang.Object
{
    internal const int FrameFormatYuyv = 0;
    internal const int FrameFormatMjpeg = 1;
    internal const int PixelFormatRgbx = 3;

    private static readonly JniPeerMembers Members = new("com/serenegiant/usb/UVCCamera", typeof(JavaUvcCamera));
    public override JniPeerMembers JniPeerMembers => Members;

    public JavaUvcCamera() : base(IntPtr.Zero, JniHandleOwnership.DoNotTransfer)
    {
        var handle = Members.InstanceMethods.StartCreateInstance("()V", GetType(), null);
        SetHandle(handle.Handle, JniHandleOwnership.TransferLocalRef);
        Members.InstanceMethods.FinishCreateInstance("()V", this, null);
    }

    public void Open(int vendorId, int productId, int fileDescriptor, int bus, int address, string usbfs)
    {
        var nativePath = JniEnvironment.Strings.NewString(usbfs);
        Span<JniArgumentValue> args = stackalloc JniArgumentValue[6];
        args[0] = new JniArgumentValue(vendorId); args[1] = new JniArgumentValue(productId);
        args[2] = new JniArgumentValue(fileDescriptor); args[3] = new JniArgumentValue(bus);
        args[4] = new JniArgumentValue(address); args[5] = new JniArgumentValue(nativePath.Handle);
        fixed (JniArgumentValue* pointer = args)
            Members.InstanceMethods.InvokeVirtualVoidMethod("open.(IIIIILjava/lang/String;)V", this, pointer);
        JniObjectReference.Dispose(ref nativePath);
    }

    public void SetPreviewSize(int width, int height, int minFps, int maxFps, int format, float bandwidth)
    {
        Span<JniArgumentValue> args = stackalloc JniArgumentValue[6];
        args[0] = new JniArgumentValue(width); args[1] = new JniArgumentValue(height);
        args[2] = new JniArgumentValue(minFps); args[3] = new JniArgumentValue(maxFps);
        args[4] = new JniArgumentValue(format); args[5] = new JniArgumentValue(bandwidth);
        fixed (JniArgumentValue* pointer = args)
            Members.InstanceMethods.InvokeVirtualVoidMethod("setPreviewSize.(IIIIIF)V", this, pointer);
    }

    public void SetFrameCallback(Java.Lang.Object callback, int format)
    {
        Span<JniArgumentValue> args = stackalloc JniArgumentValue[2];
        args[0] = new JniArgumentValue(callback); args[1] = new JniArgumentValue(format);
        fixed (JniArgumentValue* pointer = args)
            Members.InstanceMethods.InvokeVirtualVoidMethod("setFrameCallback.(Lcom/serenegiant/usb/IFrameCallback;I)V", this, pointer);
    }

    public void StartPreview() => Members.InstanceMethods.InvokeVirtualVoidMethod("startPreview.()V", this, null);

    protected override void Dispose(bool disposing)
    {
        if (Handle != IntPtr.Zero) Members.InstanceMethods.InvokeVirtualVoidMethod("destroy.()V", this, null);
        base.Dispose(disposing);
    }
}
