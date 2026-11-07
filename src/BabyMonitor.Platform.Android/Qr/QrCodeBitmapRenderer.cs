using Android.Graphics;
using Android.Util;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace BabyMonitor.Platform.Android.Qr;

/// <summary>
/// Рендеринг QR-кода приглашения сопряжения в Android <see cref="Bitmap"/> без сети и без
/// сохранения изображения на диск. QR-код — out-of-band канал передачи одноразового token:
/// payload не логируется, а готовый Bitmap принадлежит вызывающему (Activity) и освобождается им.
/// </summary>
public static class QrCodeBitmapRenderer
{
    /// <summary>Размер стороны QR-кода по умолчанию в пикселях.</summary>
    public const int DefaultSizePixels = 720;

    private const string LogTag = "BabyMonitor.Qr";
    private const int MinSizePixels = 256;
    private const int MaxSizePixels = 2048;
    private const int QuietZoneModules = 2;
    private const string CharacterSet = "UTF-8";
    private const int ArgbBlack = unchecked((int)0xFF000000);
    private const int ArgbWhite = unchecked((int)0xFFFFFFFF);

    /// <summary>
    /// Пробует построить QR-код с payload в виде <see cref="Bitmap"/> (ARGB_8888).
    /// Payload не логируется; при ошибке кодирования/выделения возвращается <see langword="null"/>,
    /// наружу исключение не выбрасывается (кроме нарушения контракта аргументов).
    /// </summary>
    /// <param name="payload">Строка для кодирования (секрет: не логировать).</param>
    /// <param name="sizePixels">
    /// Желаемый размер стороны в пикселях; клампится в диапазон [256, 2048].
    /// </param>
    /// <returns>Bitmap QR-кода или <see langword="null"/>, если код не удалось построить.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="payload"/> пуст.</exception>
    public static Bitmap? TryCreate(string payload, int sizePixels = DefaultSizePixels)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);

        var size = Math.Clamp(sizePixels, MinSizePixels, MaxSizePixels);
        try
        {
            var hints = new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
                [EncodeHintType.CHARACTER_SET] = CharacterSet,
                [EncodeHintType.MARGIN] = QuietZoneModules
            };

            var matrix = new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, size, size, hints);
            if (matrix is null)
            {
                return null;
            }

            return ToBitmap(matrix);
        }
        catch (WriterException exception)
        {
            Log.Warn(LogTag, "QR encoding rejected: " + exception.GetType().Name);
            return null;
        }
        catch (Exception exception)
        {
            // Битый payload/недостаток памяти не должны ронять экран сопряжения.
            Log.Warn(LogTag, "QR bitmap creation failed: " + exception.GetType().Name);
            return null;
        }
    }

    private static Bitmap? ToBitmap(BitMatrix matrix)
    {
        var width = matrix.Width;
        var height = matrix.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                pixels[rowOffset + x] = matrix[x, y] ? ArgbBlack : ArgbWhite;
            }
        }

        var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!);
        if (bitmap is null)
        {
            return null;
        }

        try
        {
            bitmap.SetPixels(pixels, 0, width, 0, 0, width, height);
            return bitmap;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "QR bitmap fill failed: " + exception.GetType().Name);
            RecycleQuietly(bitmap);
            return null;
        }
    }

    private static void RecycleQuietly(Bitmap bitmap)
    {
        try
        {
            bitmap.Recycle();
            bitmap.Dispose();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "QR bitmap recycle failed: " + exception.GetType().Name);
        }
    }
}
