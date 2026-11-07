using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Util;
using Android.Views;
using ZXing;
using AndroidSize = global::Android.Util.Size;
using Environment = System.Environment;
using OperationCanceledException = System.OperationCanceledException;

namespace BabyMonitor.Platform.Android.Qr;

/// <summary>
/// Сканер QR-кода через Camera2: preview TextureView, ImageReader YUV_420_888 и декодирование
/// ZXing на фоновом HandlerThread. Кадры камеры не сохраняются и не логируются; payload QR
/// передаётся только через <see cref="CodeDetected"/> и считается секретом pairing.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="StartAsync"/> открывает камеру и возвращается после запуска repeating
/// request (кадров не ждёт); <see cref="Stop"/> идемпотентно освобождает ресурсы; <see cref="Dispose"/>
/// вызывает Stop и запрещает дальнейшие запуски. Activity обязана вызывать Stop при уходе с экрана.
/// События <see cref="CodeDetected"/> и <see cref="Failed"/> поднимаются с фонового потока:
/// подписчик обязан сам переключиться в UI-поток через RunOnUiThread. Экземпляр не хранит
/// Activity/TextureView дольше запуска: используется только context приложения.
/// </remarks>
public sealed class QrCameraScanner : IDisposable
{
    private const string LogTag = "BabyMonitor.Qr";
    private const string HandlerThreadName = "BabyMonitor.Qr.Camera";
    private const int ReaderMaxImages = 2;
    private const int DefaultReaderWidth = 640;
    private const int DefaultReaderHeight = 480;
    private const int StopJoinTimeoutMilliseconds = 2000;

    /// <summary>Верхняя граница площади кадра чтения: 720p достаточно для QR и дешевле по CPU.</summary>
    private const long MaxReaderPixels = 1280L * 720L;

    private static readonly TimeSpan SurfaceReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CameraOpenTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SessionConfigureTimeout = TimeSpan.FromSeconds(5);

    private readonly Context _context;
    private readonly int _creationThreadId;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly BarcodeReaderGeneric _decoder;

    private HandlerThread? _handlerThread;
    private Handler? _handler;
    private CameraDevice? _cameraDevice;
    private CameraCaptureSession? _captureSession;
    private ImageReader? _imageReader;
    private Surface? _previewSurface;
    private ResourceWaiter<CameraDevice>? _cameraWaiter;
    private ResourceWaiter<CameraCaptureSession>? _sessionWaiter;
    private YPlaneLuminanceSource? _luminanceSource;
    private byte[]? _planeBuffer;
    private byte[]? _luminanceBuffer;
    private int _handlerManagedThreadId;
    private int _detectedRaised;
    private int _decodeErrorLogged;
    private bool _running;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>Создаёт сканер, привязанный к context приложения; Activity не удерживается.</summary>
    /// <param name="activity">Activity экрана сопряжения (нужна только для context и системных сервисов).</param>
    /// <exception cref="ArgumentNullException"><paramref name="activity"/> равен <see langword="null"/>.</exception>
    public QrCameraScanner(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _context = activity.ApplicationContext ?? activity;
        // Сканер создаётся на UI-потоке (composition root Activity): запоминаем его, чтобы не
        // делать блокирующий Join HandlerThread на main thread (CA1422 для Thread.Id).
        _creationThreadId = Environment.CurrentManagedThreadId;
        _decoder = new BarcodeReaderGeneric
        {
            Options =
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true
            }
        };
    }

    /// <summary>
    /// QR-код успешно распознан. Поднимается один раз на запуск с фонового потока;
    /// payload является секретом и не должен логироваться.
    /// </summary>
    public event EventHandler<QrCodeDetectedEventArgs>? CodeDetected;

    /// <summary>
    /// Сканирование не удалось запустить (или продолжать). Поднимается с фонового потока;
    /// <see cref="QrScannerFailedEventArgs.ErrorCode"/> — один из <see cref="QrScannerErrorCodes"/>.
    /// </summary>
    public event EventHandler<QrScannerFailedEventArgs>? Failed;

    /// <summary>Признак активного сканирования (камера открыта и repeating request запущен).</summary>
    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Запускает сканирование: проверяет CAMERA, ждёт готовности preview surface, открывает
    /// заднюю камеру, создаёт ImageReader YUV_420_888 и repeating preview request. Возвращается
    /// после запуска (не ждёт кадров). Ошибки поднимаются через <see cref="Failed"/>, кроме
    /// отмены через <paramref name="cancellationToken"/> (пробрасывается как
    /// <see cref="OperationCanceledException"/>).
    /// </summary>
    /// <param name="preview">TextureView для preview; listener назначается только на время ожидания surface.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <exception cref="ArgumentNullException"><paramref name="preview"/> равен <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    /// <exception cref="ObjectDisposedException">Сканер уже освобождён.</exception>
    public async Task StartAsync(TextureView preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_running)
                {
                    return;
                }

                _stopRequested = false;
                _detectedRaised = 0;
                _decodeErrorLogged = 0;
                _handlerManagedThreadId = 0;
            }

            if (_context.CheckSelfPermission(global::Android.Manifest.Permission.Camera) != Permission.Granted)
            {
                FailStart(
                    QrScannerErrorCodes.CameraPermissionDenied,
                    "Нет разрешения на камеру: сканирование QR-кода недоступно.");
                return;
            }

            SurfaceTexture? surfaceTexture;
            try
            {
                surfaceTexture = await WaitForSurfaceTextureAsync(preview, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Teardown();
                throw;
            }

            if (surfaceTexture is null)
            {
                FailStart(QrScannerErrorCodes.PreviewUnavailable, "Preview камеры недоступен.");
                return;
            }

            if (IsAbortRequested())
            {
                Teardown();
                return;
            }

            var cameraManager = _context.GetSystemService(Context.CameraService) as CameraManager;
            if (cameraManager is null)
            {
                FailStart(QrScannerErrorCodes.CameraUnavailable, "Камера недоступна на устройстве.");
                return;
            }

            string cameraId;
            CameraCharacteristics characteristics;
            try
            {
                var cameraIds = cameraManager.GetCameraIdList();
                var selectedId = cameraIds is null ? null : SelectCameraId(cameraManager, cameraIds);
                if (selectedId is null)
                {
                    FailStart(QrScannerErrorCodes.CameraUnavailable, "Камера недоступна на устройстве.");
                    return;
                }

                cameraId = selectedId;
                characteristics = cameraManager.GetCameraCharacteristics(cameraId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Warn(LogTag, "Camera enumeration failed: " + exception.GetType().Name);
                FailStart(QrScannerErrorCodes.CameraUnavailable, "Камера недоступна на устройстве.");
                return;
            }

            var readerSize = SelectReaderSize(characteristics);
            if (readerSize is null)
            {
                FailStart(QrScannerErrorCodes.CameraUnavailable, "Камера не поддерживает YUV_420_888.");
                return;
            }

            var handlerThread = new HandlerThread(HandlerThreadName);
            handlerThread.Start();
            if (!TryOwn(ref _handlerThread, handlerThread))
            {
                QuitAndDisposeHandlerThread(handlerThread);
                return;
            }

            var looper = handlerThread.Looper;
            if (looper is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось создать поток камеры.");
                return;
            }

            var handler = new Handler(looper);
            if (!TryOwn(ref _handler, handler))
            {
                handler.Dispose();
                Teardown();
                return;
            }

            var imageReader = ImageReader.NewInstance(
                readerSize.Width,
                readerSize.Height,
                ImageFormatType.Yuv420888,
                ReaderMaxImages);
            if (imageReader is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось создать ImageReader камеры.");
                return;
            }

            if (!TryOwn(ref _imageReader, imageReader))
            {
                CloseImageReader(imageReader);
                Teardown();
                return;
            }

            imageReader.SetOnImageAvailableListener(new ImageAvailableListener(this), handler);
            var readerSurface = imageReader.Surface;
            if (readerSurface is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось получить surface для декодирования.");
                return;
            }

            var previewSurface = new Surface(surfaceTexture);
            if (!TryOwn(ref _previewSurface, previewSurface))
            {
                ReleaseSurface(previewSurface);
                Teardown();
                return;
            }

            var cameraWaiter = new ResourceWaiter<CameraDevice>(ReleaseCamera);
            if (!TryOwn(ref _cameraWaiter, cameraWaiter))
            {
                Teardown();
                return;
            }

            try
            {
                cameraManager.OpenCamera(cameraId, new CameraDeviceCallback(this, cameraWaiter), handler);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Warn(LogTag, "Camera open call failed: " + exception.GetType().Name);
                ForgetCameraWaiter(cameraWaiter);
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось открыть камеру.");
                return;
            }

            CameraDevice? cameraDevice;
            try
            {
                cameraDevice = await cameraWaiter.Task.WaitAsync(CameraOpenTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                cameraWaiter.AbandonAndRelease();
                ForgetCameraWaiter(cameraWaiter);
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Камера не открылась за отведённое время.");
                return;
            }
            catch (OperationCanceledException)
            {
                cameraWaiter.AbandonAndRelease();
                ForgetCameraWaiter(cameraWaiter);
                Teardown();
                throw;
            }

            ForgetCameraWaiter(cameraWaiter);
            if (IsAbortRequested())
            {
                if (cameraDevice is not null)
                {
                    ReleaseCamera(cameraDevice);
                }

                Teardown();
                return;
            }

            if (cameraDevice is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось открыть камеру.");
                return;
            }

            if (!TryOwn(ref _cameraDevice, cameraDevice))
            {
                ReleaseCamera(cameraDevice);
                Teardown();
                return;
            }

            var outputs = new List<Surface>(2) { previewSurface, readerSurface };
            var sessionWaiter = new ResourceWaiter<CameraCaptureSession>(ReleaseCaptureSession);
            if (!TryOwn(ref _sessionWaiter, sessionWaiter))
            {
                Teardown();
                return;
            }

            var sessionCallback = new CaptureSessionCallback(sessionWaiter);
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(28))
                {
                    CreateCaptureSessionApi28(cameraDevice, outputs, sessionCallback, handler);
                }
                else
                {
                    CreateCaptureSessionLegacy(cameraDevice, outputs, sessionCallback, handler);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.Warn(LogTag, "Capture session creation failed: " + exception.GetType().Name);
                sessionWaiter.AbandonAndRelease();
                ForgetSessionWaiter(sessionWaiter);
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось создать capture session камеры.");
                return;
            }

            CameraCaptureSession? captureSession;
            try
            {
                captureSession = await sessionWaiter.Task.WaitAsync(SessionConfigureTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                sessionWaiter.AbandonAndRelease();
                ForgetSessionWaiter(sessionWaiter);
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Capture session не сконфигурирована вовремя.");
                return;
            }
            catch (OperationCanceledException)
            {
                sessionWaiter.AbandonAndRelease();
                ForgetSessionWaiter(sessionWaiter);
                Teardown();
                throw;
            }

            ForgetSessionWaiter(sessionWaiter);
            if (IsAbortRequested())
            {
                if (captureSession is not null)
                {
                    ReleaseCaptureSession(captureSession);
                }

                Teardown();
                return;
            }

            if (captureSession is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось создать capture session камеры.");
                return;
            }

            if (!TryOwn(ref _captureSession, captureSession))
            {
                ReleaseCaptureSession(captureSession);
                Teardown();
                return;
            }

            var activeDevice = captureSession.Device;
            if (activeDevice is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Камера закрыта до запуска preview.");
                return;
            }

            var requestBuilder = activeDevice.CreateCaptureRequest(CameraTemplate.Preview);
            if (requestBuilder is null)
            {
                FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось создать preview request.");
                return;
            }

            try
            {
                requestBuilder.AddTarget(previewSurface);
                requestBuilder.AddTarget(readerSurface);
                var previewRequest = requestBuilder.Build();
                if (previewRequest is null)
                {
                    FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось собрать preview request.");
                    return;
                }

                _ = captureSession.SetRepeatingRequest(previewRequest, null, handler);
                previewRequest.Dispose();
            }
            finally
            {
                requestBuilder.Dispose();
            }

            var aborted = false;
            lock (_sync)
            {
                if (_stopRequested || _disposed)
                {
                    aborted = true;
                }
                else
                {
                    _running = true;
                }
            }

            if (aborted)
            {
                Teardown();
            }
        }
        catch (OperationCanceledException)
        {
            Teardown();
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "QR scanner start failed: " + exception.GetType().Name);
            FailStart(QrScannerErrorCodes.CameraOpenFailed, "Не удалось запустить сканер QR-кода.");
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>
    /// Останавливает сканирование и освобождает камеру, capture session, ImageReader, preview
    /// surface и HandlerThread. Идемпотентен; вызывать можно из любого потока, в том числе из
    /// handlers <see cref="CodeDetected"/>.
    /// </summary>
    public void Stop()
    {
        lock (_sync)
        {
            _stopRequested = true;
            _running = false;
        }

        Teardown();
    }

    /// <summary>
    /// Останавливает сканирование и освобождает все ресурсы. После Dispose повторный запуск
    /// невозможен; повторный вызов Dispose безопасен.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopRequested = true;
            _running = false;
        }

        Teardown();
    }

    private void MarkHandlerThread()
    {
        Interlocked.CompareExchange(ref _handlerManagedThreadId, Environment.CurrentManagedThreadId, 0);
    }

    /// <summary>
    /// Камера пропала во время сканирования (другое приложение/система забрала устройство):
    /// освобождаем ресурсы и сообщаем UI, что сканирование остановлено не по инициативе пользователя.
    /// </summary>
    private void OnCameraLost()
    {
        bool wasRunning;
        lock (_sync)
        {
            wasRunning = _running && !_stopRequested && !_disposed;
        }

        if (!wasRunning)
        {
            // Камера ещё не была запущена: ошибку запуска StartAsync сообщит сам через Failed.
            return;
        }

        Stop();
        Failed?.Invoke(this, new QrScannerFailedEventArgs(
            QrScannerErrorCodes.CameraUnavailable,
            "Камера стала недоступна: сканирование остановлено."));
    }

    private static string? SelectCameraId(CameraManager cameraManager, string[] cameraIds)
    {
        string? fallback = null;
        foreach (var cameraId in cameraIds)
        {
            if (string.IsNullOrEmpty(cameraId))
            {
                continue;
            }

            fallback ??= cameraId;
            try
            {
                var characteristics = cameraManager.GetCameraCharacteristics(cameraId);
                var facing = characteristics.Get(CameraCharacteristics.LensFacing);
                if ((facing as Java.Lang.Integer)?.IntValue() == (int)LensFacing.Back)
                {
                    return cameraId;
                }
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "Camera characteristics failed: " + exception.GetType().Name);
            }
        }

        return fallback;
    }

    private static AndroidSize? SelectReaderSize(CameraCharacteristics characteristics)
    {
        StreamConfigurationMap? configurationMap;
        try
        {
            configurationMap = characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap) as StreamConfigurationMap;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Stream configuration read failed: " + exception.GetType().Name);
            return null;
        }

        var sizes = configurationMap?.GetOutputSizes((int)ImageFormatType.Yuv420888);
        if (sizes is null || sizes.Length == 0)
        {
            return new AndroidSize(DefaultReaderWidth, DefaultReaderHeight);
        }

        AndroidSize? best = null;
        AndroidSize? smallest = null;
        var bestPixels = 0L;
        var smallestPixels = long.MaxValue;
        foreach (var size in sizes)
        {
            if (size is null)
            {
                continue;
            }

            if (size.Width <= 0 || size.Height <= 0)
            {
                continue;
            }

            var pixels = (long)size.Width * size.Height;
            if (pixels < smallestPixels)
            {
                smallestPixels = pixels;
                smallest = size;
            }

            if (pixels > MaxReaderPixels || pixels <= bestPixels)
            {
                continue;
            }

            bestPixels = pixels;
            best = size;
        }

        // Если все размеры больше целевой площади — берём минимальный, чтобы не грузить CPU.
        return best ?? smallest ?? new AndroidSize(DefaultReaderWidth, DefaultReaderHeight);
    }

    private static async Task<SurfaceTexture?> WaitForSurfaceTextureAsync(
        TextureView preview,
        CancellationToken cancellationToken)
    {
        var immediate = preview.SurfaceTexture;
        if (immediate is not null)
        {
            return immediate;
        }

        var waiter = new SurfaceTextureWaiter(preview);
        preview.Post(waiter);
        try
        {
            var surfaceTexture = await waiter.Task.WaitAsync(SurfaceReadyTimeout, cancellationToken).ConfigureAwait(false);
            preview.Post(new ActionRunnable(waiter.Detach));
            return surfaceTexture;
        }
        catch (TimeoutException)
        {
            preview.Post(new ActionRunnable(waiter.Detach));
            return null;
        }
        catch (OperationCanceledException)
        {
            preview.Post(new ActionRunnable(waiter.Detach));
            throw;
        }
    }

    private void OnImageAvailable(ImageReader? reader)
    {
        MarkHandlerThread();

        string? payload = null;
        Image? image = null;
        try
        {
            if (!IsRunning)
            {
                return;
            }

            image = reader?.AcquireLatestImage();
            if (image is null)
            {
                return;
            }

            payload = TryDecode(image);
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _decodeErrorLogged, 1) == 0)
            {
                Log.Warn(LogTag, "QR frame processing failed: " + exception.GetType().Name);
            }
        }
        finally
        {
            if (image is not null)
            {
                try
                {
                    image.Close();
                }
                catch (Exception exception)
                {
                    Log.Warn(LogTag, "Camera image close failed: " + exception.GetType().Name);
                }
                finally
                {
                    image.Dispose();
                }
            }
        }

        if (payload is null)
        {
            return;
        }

        lock (_sync)
        {
            // Если пользователь уже остановил сканирование, не поднимаем событие после Stop.
            if (_stopRequested || _disposed || _detectedRaised != 0)
            {
                return;
            }

            _detectedRaised = 1;
        }

        // Сначала останавливаем камеру, чтобы подписчик увидел IsRunning == false.
        Stop();
        CodeDetected?.Invoke(this, new QrCodeDetectedEventArgs(payload));
    }

    private string? TryDecode(Image image)
    {
        var planes = image.GetPlanes();
        if (planes is null || planes.Length == 0)
        {
            return null;
        }

        var plane = planes[0];
        if (plane is null)
        {
            return null;
        }

        var buffer = plane.Buffer;
        if (buffer is null)
        {
            return null;
        }

        var width = image.Width;
        var height = image.Height;
        var rowStride = plane.RowStride;
        var pixelStride = plane.PixelStride;
        if (width <= 0 || height <= 0 || rowStride < width || pixelStride <= 0)
        {
            return null;
        }

        var planeLength = buffer.Remaining();
        var luminanceLength = checked(width * height);
        if (planeLength < luminanceLength)
        {
            return null;
        }

        EnsureFrameBuffers(width, height, luminanceLength, planeLength);
        buffer.Rewind();
        var planeBuffer = _planeBuffer!;
        _ = buffer.Get(planeBuffer, 0, planeLength);

        var luminanceBuffer = _luminanceBuffer!;
        CopyLuminance(planeBuffer, luminanceBuffer, width, height, rowStride, pixelStride);

        var luminanceSource = _luminanceSource!;
        ZXing.Result? result;
        try
        {
            result = _decoder.Decode(luminanceSource);
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref _decodeErrorLogged, 1) == 0)
            {
                Log.Warn(LogTag, "QR decode failed: " + exception.GetType().Name);
            }

            return null;
        }

        var text = result?.Text;
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private void EnsureFrameBuffers(int width, int height, int luminanceLength, int planeLength)
    {
        if (_planeBuffer is null || _planeBuffer.Length != planeLength)
        {
            _planeBuffer = new byte[planeLength];
        }

        if (_luminanceBuffer is null || _luminanceBuffer.Length != luminanceLength)
        {
            _luminanceBuffer = new byte[luminanceLength];
            _luminanceSource = new YPlaneLuminanceSource(_luminanceBuffer, width, height);
        }
    }

    private static void CopyLuminance(
        byte[] plane,
        byte[] luminance,
        int width,
        int height,
        int rowStride,
        int pixelStride)
    {
        if (pixelStride == 1)
        {
            if (rowStride == width)
            {
                System.Buffer.BlockCopy(plane, 0, luminance, 0, luminance.Length);
                return;
            }

            for (var y = 0; y < height; y++)
            {
                System.Buffer.BlockCopy(plane, y * rowStride, luminance, y * width, width);
            }

            return;
        }

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * rowStride;
            var targetRow = y * width;
            for (var x = 0; x < width; x++)
            {
                luminance[targetRow + x] = plane[sourceRow + (x * pixelStride)];
            }
        }
    }

    private bool IsAbortRequested()
    {
        lock (_sync)
        {
            return _stopRequested || _disposed;
        }
    }

    private bool TryOwn<T>(ref T? field, T value)
        where T : class
    {
        lock (_sync)
        {
            if (_stopRequested || _disposed)
            {
                return false;
            }

            field = value;
            return true;
        }
    }

    private void ForgetCameraWaiter(ResourceWaiter<CameraDevice> waiter)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_cameraWaiter, waiter))
            {
                _cameraWaiter = null;
            }
        }
    }

    private void ForgetSessionWaiter(ResourceWaiter<CameraCaptureSession> waiter)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_sessionWaiter, waiter))
            {
                _sessionWaiter = null;
            }
        }
    }

    private void FailStart(string errorCode, string message)
    {
        Teardown();
        if (IsAbortRequested())
        {
            // Stop/Dispose уже отменил запуск: не шлём ошибку после ухода пользователя с экрана.
            return;
        }

        Failed?.Invoke(this, new QrScannerFailedEventArgs(errorCode, message));
    }

    private void Teardown()
    {
        CameraCaptureSession? captureSession;
        CameraDevice? cameraDevice;
        ImageReader? imageReader;
        Surface? previewSurface;
        HandlerThread? handlerThread;
        Handler? handler;
        ResourceWaiter<CameraDevice>? cameraWaiter;
        ResourceWaiter<CameraCaptureSession>? sessionWaiter;
        lock (_sync)
        {
            _running = false;
            captureSession = _captureSession;
            _captureSession = null;
            cameraDevice = _cameraDevice;
            _cameraDevice = null;
            imageReader = _imageReader;
            _imageReader = null;
            previewSurface = _previewSurface;
            _previewSurface = null;
            handlerThread = _handlerThread;
            _handlerThread = null;
            handler = _handler;
            _handler = null;
            cameraWaiter = _cameraWaiter;
            _cameraWaiter = null;
            sessionWaiter = _sessionWaiter;
            _sessionWaiter = null;
            _planeBuffer = null;
            _luminanceBuffer = null;
            _luminanceSource = null;
        }

        cameraWaiter?.AbandonAndRelease();
        sessionWaiter?.AbandonAndRelease();
        ReleaseCaptureSession(captureSession);
        ReleaseCamera(cameraDevice);
        CloseImageReader(imageReader);
        ReleaseSurface(previewSurface);

        if (handlerThread is not null)
        {
            var handlerThreadId = Volatile.Read(ref _handlerManagedThreadId);
            var onOwnThread = handlerThreadId != 0 && Environment.CurrentManagedThreadId == handlerThreadId;
            // Join на main thread блокировал бы UI; для UI-потока ограничиваемся QuitSafely.
            var onMainThread = Environment.CurrentManagedThreadId == _creationThreadId;
            try
            {
                handlerThread.QuitSafely();
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "HandlerThread quit failed: " + exception.GetType().Name);
            }

            if (!onOwnThread && !onMainThread)
            {
                try
                {
                    handlerThread.Join(StopJoinTimeoutMilliseconds);
                }
                catch (Exception exception)
                {
                    Log.Warn(LogTag, "HandlerThread join failed: " + exception.GetType().Name);
                }
            }

            try
            {
                handlerThread.Dispose();
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "HandlerThread dispose failed: " + exception.GetType().Name);
            }
        }

        handler?.Dispose();
    }

    private static void QuitAndDisposeHandlerThread(HandlerThread handlerThread)
    {
        try
        {
            handlerThread.QuitSafely();
            handlerThread.Join(StopJoinTimeoutMilliseconds);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "HandlerThread stop failed: " + exception.GetType().Name);
        }
        finally
        {
            try
            {
                handlerThread.Dispose();
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "HandlerThread dispose failed: " + exception.GetType().Name);
            }
        }
    }

    private static void ReleaseCamera(CameraDevice? cameraDevice)
    {
        if (cameraDevice is null)
        {
            return;
        }

        try
        {
            cameraDevice.Close();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Camera close failed: " + exception.GetType().Name);
        }
        finally
        {
            cameraDevice.Dispose();
        }
    }

    private static void ReleaseCaptureSession(CameraCaptureSession? captureSession)
    {
        if (captureSession is null)
        {
            return;
        }

        try
        {
            captureSession.Close();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Capture session close failed: " + exception.GetType().Name);
        }
        finally
        {
            captureSession.Dispose();
        }
    }

    private static void CloseImageReader(ImageReader? imageReader)
    {
        if (imageReader is null)
        {
            return;
        }

        try
        {
            imageReader.Close();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "ImageReader close failed: " + exception.GetType().Name);
        }
        finally
        {
            imageReader.Dispose();
        }
    }

    private static void ReleaseSurface(Surface? surface)
    {
        if (surface is null)
        {
            return;
        }

        try
        {
            surface.Release();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Preview surface release failed: " + exception.GetType().Name);
        }
        finally
        {
            surface.Dispose();
        }
    }

    [SupportedOSPlatform("android28.0")]
    private static void CreateCaptureSessionApi28(
        CameraDevice cameraDevice,
        IReadOnlyList<Surface> outputs,
        CameraCaptureSession.StateCallback callback,
        Handler handler)
    {
        var configurations = new List<OutputConfiguration>(outputs.Count);
        foreach (var output in outputs)
        {
            configurations.Add(new OutputConfiguration(output));
        }

        var executor = new HandlerExecutor(handler);
        var configuration = new SessionConfiguration(
            (int)SessionType.Regular,
            configurations,
            executor,
            callback);
        cameraDevice.CreateCaptureSession(configuration);
    }

    private static void CreateCaptureSessionLegacy(
        CameraDevice cameraDevice,
        IList<Surface> outputs,
        CameraCaptureSession.StateCallback callback,
        Handler handler)
    {
#pragma warning disable CA1422 // На API 24–27 доступен только устаревший overload; на 28+ используется SessionConfiguration.
        cameraDevice.CreateCaptureSession(outputs, callback, handler);
#pragma warning restore CA1422
    }

    /// <summary>Аргументы успешного распознавания QR-кода.</summary>
    public sealed class QrCodeDetectedEventArgs : EventArgs
    {
        /// <summary>Создаёт аргументы события.</summary>
        /// <param name="payload">Распознанный payload QR (секрет: не логировать).</param>
        /// <exception cref="ArgumentNullException"><paramref name="payload"/> равен <see langword="null"/>.</exception>
        public QrCodeDetectedEventArgs(string payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            Payload = payload;
        }

        /// <summary>Распознанный payload QR-кода.</summary>
        public string Payload { get; }
    }

    /// <summary>Аргументы ошибки сканирования QR-кода.</summary>
    public sealed class QrScannerFailedEventArgs : EventArgs
    {
        /// <summary>Создаёт аргументы события.</summary>
        /// <param name="errorCode">Стабильный код из <see cref="QrScannerErrorCodes"/>.</param>
        /// <param name="message">Безопасное для пользователя сообщение без секретов.</param>
        /// <exception cref="ArgumentException">Код или сообщение пусты.</exception>
        public QrScannerFailedEventArgs(string errorCode, string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);
            ErrorCode = errorCode;
            Message = message;
        }

        /// <summary>Стабильный машинно-читаемый код ошибки.</summary>
        public string ErrorCode { get; }

        /// <summary>Безопасное пользовательское сообщение.</summary>
        public string Message { get; }
    }

    private sealed class ResourceWaiter<T>
        where T : class
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<T?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action<T> _release;
        private T? _resolved;
        private bool _abandoned;

        public ResourceWaiter(Action<T> release) => _release = release;

        public Task<T?> Task => _result.Task;

        public void Success(T resource)
        {
            bool release;
            lock (_gate)
            {
                release = _abandoned || !_result.TrySetResult(resource);
                if (!release)
                {
                    _resolved = resource;
                }
            }

            if (release)
            {
                _release(resource);
            }
        }

        public void Fail()
        {
            lock (_gate)
            {
                if (!_abandoned)
                {
                    _result.TrySetResult(null);
                }
            }
        }

        public void AbandonAndRelease()
        {
            T? late;
            lock (_gate)
            {
                _abandoned = true;
                late = _resolved;
                _resolved = null;
                _result.TrySetResult(null);
            }

            if (late is not null)
            {
                _release(late);
            }
        }
    }

    private sealed class CameraDeviceCallback : CameraDevice.StateCallback
    {
        private readonly QrCameraScanner _scanner;
        private readonly ResourceWaiter<CameraDevice> _waiter;

        public CameraDeviceCallback(QrCameraScanner scanner, ResourceWaiter<CameraDevice> waiter)
        {
            _scanner = scanner;
            _waiter = waiter;
        }

        public override void OnOpened(CameraDevice camera)
        {
            _scanner.MarkHandlerThread();
            _waiter.Success(camera);
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            _scanner.MarkHandlerThread();
            ReleaseCamera(camera);
            _waiter.Fail();
            _scanner.OnCameraLost();
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            _scanner.MarkHandlerThread();
            ReleaseCamera(camera);
            _waiter.Fail();
            _scanner.OnCameraLost();
        }
    }

    private sealed class CaptureSessionCallback : CameraCaptureSession.StateCallback
    {
        private readonly ResourceWaiter<CameraCaptureSession> _waiter;

        public CaptureSessionCallback(ResourceWaiter<CameraCaptureSession> waiter) => _waiter = waiter;

        public override void OnConfigured(CameraCaptureSession session) => _waiter.Success(session);

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            ReleaseCaptureSession(session);
            _waiter.Fail();
        }
    }

    private sealed class HandlerExecutor : Java.Lang.Object, Java.Util.Concurrent.IExecutor
    {
        private readonly Handler _handler;

        public HandlerExecutor(Handler handler) => _handler = handler;

        public void Execute(Java.Lang.IRunnable? command)
        {
            if (command is not null)
            {
                _ = _handler.Post(command);
            }
        }
    }

    private sealed class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private readonly QrCameraScanner _scanner;

        public ImageAvailableListener(QrCameraScanner scanner) => _scanner = scanner;

        public void OnImageAvailable(ImageReader? reader) => _scanner.OnImageAvailable(reader);
    }

    private sealed class ActionRunnable : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly Action _action;

        public ActionRunnable(Action action) => _action = action;

        public void Run() => _action();
    }

    private sealed class SurfaceTextureWaiter : Java.Lang.Object, TextureView.ISurfaceTextureListener, Java.Lang.IRunnable
    {
        private readonly object _gate = new();
        private readonly TextureView _preview;
        private readonly TaskCompletionSource<SurfaceTexture?> _surface =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TextureView.ISurfaceTextureListener? _previous;
        private bool _attached;
        private bool _detached;

        public SurfaceTextureWaiter(TextureView preview) => _preview = preview;

        public Task<SurfaceTexture?> Task => _surface.Task;

        public void Run() => Attach();

        public void Attach()
        {
            lock (_gate)
            {
                if (_detached || _attached)
                {
                    return;
                }

                var immediate = _preview.SurfaceTexture;
                if (immediate is not null)
                {
                    _surface.TrySetResult(immediate);
                    return;
                }

                _previous = _preview.SurfaceTextureListener;
                _preview.SurfaceTextureListener = this;
                _attached = true;
                var available = _preview.SurfaceTexture;
                if (available is not null)
                {
                    _surface.TrySetResult(available);
                }
            }
        }

        public void Detach()
        {
            lock (_gate)
            {
                if (_detached)
                {
                    return;
                }

                _detached = true;
                if (_attached && ReferenceEquals(_preview.SurfaceTextureListener, this))
                {
                    _preview.SurfaceTextureListener = _previous;
                }

                _attached = false;
                _previous = null;
                _surface.TrySetResult(null);
            }
        }

        public void OnSurfaceTextureAvailable(SurfaceTexture? surface, int width, int height)
        {
            if (surface is not null)
            {
                _surface.TrySetResult(surface);
            }

            Detach();
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture? surface)
        {
            Detach();
            return true;
        }

        public void OnSurfaceTextureSizeChanged(SurfaceTexture? surface, int width, int height)
        {
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture? surface)
        {
        }
    }

    private sealed class YPlaneLuminanceSource : LuminanceSource
    {
        private readonly byte[] _luminance;

        public YPlaneLuminanceSource(byte[] luminance, int width, int height)
            : base(width, height)
        {
            _luminance = luminance;
        }

        public override byte[] Matrix => _luminance;

        public override byte[] getRow(int y, byte[] row)
        {
            var target = row is null || row.Length < Width ? new byte[Width] : row;
            Array.Copy(_luminance, y * Width, target, 0, Width);
            return target;
        }
    }
}
