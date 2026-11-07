using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Discovery;

/// <summary>
/// Privacy-контракт mDNS TXT: только protocol version и роль; персональные данные,
/// device id, ключи и pairing secrets запрещены.
/// </summary>
public sealed class PeerTxtRecordsTests
{
    [Fact]
    public void Create_содержит_только_version_и_role()
    {
        var advertisement = new PeerAdvertisement(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

        var txt = PeerTxtRecords.Create(advertisement);

        Assert.Equal(2, txt.Count);
        Assert.Equal(["role", "v"], txt.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal("Child", txt[PeerDiscoveryConstants.TxtRoleKey]);
        Assert.Equal("1", txt[PeerDiscoveryConstants.TxtProtocolVersionKey]);
    }

    [Fact]
    public void Create_не_содержит_персональных_данных_и_секретов()
    {
        var advertisement = new PeerAdvertisement(DeviceRole.Child, 2);

        var txt = PeerTxtRecords.Create(advertisement);

        string[] forbiddenKeyParts =
        [
            "name", "owner", "device", "id", "key", "token", "secret", "fingerprint", "ssid", "ip"
        ];
        foreach (var key in txt.Keys)
        {
            Assert.DoesNotContain(forbiddenKeyParts, part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Parse_восстанавливает_объявление_из_созданных_TXT()
    {
        var original = new PeerAdvertisement(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

        var parsed = PeerTxtRecords.Parse(PeerTxtRecords.Create(original));

        Assert.True(parsed.IsSuccess);
        Assert.Equal(original, parsed.Value);
    }

    [Fact]
    public void Parse_игнорирует_неизвестные_ключи_для_forward_compatibility()
    {
        var txt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PeerDiscoveryConstants.TxtProtocolVersionKey] = "1",
            [PeerDiscoveryConstants.TxtRoleKey] = "Child",
            ["future"] = "value"
        };

        var parsed = PeerTxtRecords.Parse(txt);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(1, parsed.Value.ProtocolVersion);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("no-version")]
    [InlineData("no-role")]
    public void Parse_без_обязательных_ключей_возвращает_invalid_txt(string scenario)
    {
        var txt = scenario switch
        {
            "empty" => new Dictionary<string, string>(StringComparer.Ordinal),
            "no-version" => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PeerDiscoveryConstants.TxtRoleKey] = "Child"
            },
            _ => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PeerDiscoveryConstants.TxtProtocolVersionKey] = "1"
            }
        };

        var parsed = PeerTxtRecords.Parse(txt);

        Assert.False(parsed.IsSuccess);
        Assert.Equal(DiscoveryErrorCodes.InvalidTxt, parsed.Error!.Code);
    }

    [Fact]
    public void Parse_без_TXT_возвращает_invalid_txt()
    {
        var parsed = PeerTxtRecords.Parse(null);

        Assert.False(parsed.IsSuccess);
        Assert.Equal(DiscoveryErrorCodes.InvalidTxt, parsed.Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2")]
    [InlineData("abc")]
    public void Parse_с_некорректной_версией_возвращает_invalid_txt(string version)
    {
        var txt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PeerDiscoveryConstants.TxtProtocolVersionKey] = version,
            [PeerDiscoveryConstants.TxtRoleKey] = "Child"
        };

        var parsed = PeerTxtRecords.Parse(txt);

        Assert.False(parsed.IsSuccess);
        Assert.Equal(DiscoveryErrorCodes.InvalidTxt, parsed.Error!.Code);
    }

    [Theory]
    [InlineData("Parent")]
    [InlineData("Unknown")]
    [InlineData("child")]
    [InlineData("999")]
    [InlineData("1")]
    public void Parse_с_недопустимой_ролью_возвращает_invalid_txt(string role)
    {
        var txt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PeerDiscoveryConstants.TxtProtocolVersionKey] = "1",
            [PeerDiscoveryConstants.TxtRoleKey] = role
        };

        var parsed = PeerTxtRecords.Parse(txt);

        Assert.False(parsed.IsSuccess);
        Assert.Equal(DiscoveryErrorCodes.InvalidTxt, parsed.Error!.Code);
    }
}
