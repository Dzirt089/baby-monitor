using BabyMonitor.Application.Updates;

namespace BabyMonitor.Application.Tests.Updates;

/// <summary>
/// Сравнение наборов SHA-256 signing-сертификатов: нечувствительно к порядку и регистру hex,
/// пустой набор отклоняется, null-коллекции запрещены (FR-005 009).
/// </summary>
public sealed class SigningCertificateSetComparerTests
{
    private const string FirstHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecondHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Одинаковые_наборы_в_разном_порядке_совпадают()
    {
        var first = new[] { FirstHash, SecondHash };
        var second = new[] { SecondHash, FirstHash };

        Assert.True(SigningCertificateSetComparer.AreEqual(first, second));
    }

    [Fact]
    public void Разные_наборы_не_совпадают()
    {
        var first = new[] { FirstHash };
        var second = new[] { SecondHash };

        Assert.False(SigningCertificateSetComparer.AreEqual(first, second));
    }

    [Fact]
    public void Дополнительный_certificate_в_наборе_не_совпадает()
    {
        var first = new[] { FirstHash };
        var second = new[] { FirstHash, SecondHash };

        Assert.False(SigningCertificateSetComparer.AreEqual(first, second));
    }

    [Fact]
    public void Пустой_набор_не_совпадает_с_непустым()
    {
        Assert.False(SigningCertificateSetComparer.AreEqual(Array.Empty<string>(), new[] { FirstHash }));
        Assert.False(SigningCertificateSetComparer.AreEqual(new[] { FirstHash }, Array.Empty<string>()));
    }

    [Fact]
    public void Два_пустых_набора_не_совпадают()
    {
        // Установка без проверки подписи запрещена: пустой набор всегда reject.
        Assert.False(SigningCertificateSetComparer.AreEqual(Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void Сравнение_игнорирует_регистр_hex()
    {
        var lower = new[] { FirstHash };
        var upper = new[] { FirstHash.ToUpperInvariant() };

        Assert.True(SigningCertificateSetComparer.AreEqual(lower, upper));
    }

    [Fact]
    public void Дубликаты_не_влияют_на_сравнение_множеств()
    {
        var first = new[] { FirstHash, FirstHash, SecondHash };
        var second = new[] { SecondHash, FirstHash };

        Assert.True(SigningCertificateSetComparer.AreEqual(first, second));
    }

    [Fact]
    public void Null_коллекции_дают_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => SigningCertificateSetComparer.AreEqual(null!, new[] { FirstHash }));
        Assert.Throws<ArgumentNullException>(
            () => SigningCertificateSetComparer.AreEqual(new[] { FirstHash }, null!));
    }
}
