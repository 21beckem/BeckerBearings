using System.Text;
using GloveGo.App.Services;
using Microsoft.Maui.Storage;

namespace GloveGo.App;

public partial class MainPage : ContentPage
{
    private readonly ICameraService _camera;
    private readonly ISerialService _serial;
    private string? _calibrationPath;

    public MainPage(ICameraService camera, ISerialService serial)
    { InitializeComponent(); _camera = camera; _serial = serial; }

    private async void RefreshClicked(object sender, EventArgs e)
    {
        try { CameraPicker.ItemsSource = (await _camera.ListAsync()).ToList(); SerialPicker.ItemsSource = (await _serial.ListAsync()).ToList(); }
        catch (Exception ex) { DepthStatus.Text = ex.Message; }
    }

    private async Task<string> CalibrationPathAsync()
    {
        if (_calibrationPath is not null) return _calibrationPath;
        var path = Path.Combine(FileSystem.AppDataDirectory, "calibration.json");
        await using var input = await FileSystem.OpenAppPackageFileAsync("calibration.json");
        await using var output = File.Create(path);
        await input.CopyToAsync(output);
        return _calibrationPath = path;
    }

    private async void CaptureClicked(object sender, EventArgs e)
    {
        if (CameraPicker.SelectedItem is not VideoDevice selected) { DepthStatus.Text = "Select a camera."; return; }
        try
        {
            DepthStatus.Text = "Opening camera…";
#if ANDROID
            var permission = await Permissions.RequestAsync<Permissions.Camera>();
            if (permission != PermissionStatus.Granted) throw new InvalidOperationException("Camera permission denied.");
#endif
            await _camera.OpenAsync(selected.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var frame = await _camera.CaptureAsync(timeout.Token);
            var path = await CalibrationPathAsync();
            var start = System.Diagnostics.Stopwatch.StartNew();
            var result = await Task.Run(() => NativeDepth.Process(frame, path));
            start.Stop();
            var bmp = EncodeBmp(result.Preview);
            DepthPreview.Source = ImageSource.FromStream(() => new MemoryStream(bmp));
            DepthStatus.Text = $"Depth complete: {result.ValidPixels:N0} valid pixels, {start.ElapsedMilliseconds} ms";
        }
        catch (Exception ex) { DepthStatus.Text = ex.Message; }
    }

    private static byte[] EncodeBmp(byte[] bgr)
    {
        const int width = 320, height = 240, stride = width * 3;
        var bmp = new byte[54 + bgr.Length];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(bgr, row * stride, bmp, 54 + (height - 1 - row) * stride, stride);
        return bmp;
    }

    private async void OpenSerialClicked(object sender, EventArgs e)
    {
        if (SerialPicker.SelectedItem is not SerialDevice selected) { SerialStatus.Text = "Select a serial device."; return; }
        try { await _serial.OpenAsync(selected.Id, int.Parse(BaudEntry.Text)); SerialStatus.Text = $"Opened {selected.Name}"; }
        catch (Exception ex) { SerialStatus.Text = ex.Message; }
    }

    private async void SendClicked(object sender, EventArgs e)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var sent = Encoding.UTF8.GetBytes(SendEntry.Text ?? "");
            await _serial.WriteAsync(sent, timeout.Token);
            var received = await _serial.ReadAsync(timeout.Token);
            SerialStatus.Text = $"Sent {sent.Length} bytes; received: {Encoding.UTF8.GetString(received)}";
        }
        catch (Exception ex) { SerialStatus.Text = ex.Message; }
    }
}
