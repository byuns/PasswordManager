using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace PasswordManager.App.Converters;

/// <summary>
/// otpauth URI 문자열을 스캔 가능한 QR 코드 이미지로 변환한다(design 5.4, TD-020).
/// System.Drawing에 의존하지 않도록 <see cref="PngByteQRCode"/>로 PNG 바이트를 만들어
/// WPF <see cref="BitmapImage"/>로 로드한다. 값이 비면 null(이미지 없음).
/// </summary>
public sealed class OtpAuthToQrConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string payload || string.IsNullOrEmpty(payload))
            return null;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        byte[] bytes = png.GetGraphic(pixelsPerModule: 10);

        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return (ImageSource)image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
