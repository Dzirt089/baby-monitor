using System.Globalization;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Формирование и разбор mDNS TXT-записей advertisement. В TXT допускаются только
/// protocol version и роль; любые персональные данные, ключи и pairing secrets запрещены.
/// </summary>
public static class PeerTxtRecords
{
    /// <summary>Создаёт TXT-словарь для advertisement (только разрешённые ключи).</summary>
    /// <param name="advertisement">Объявление Child.</param>
    /// <returns>Immutable-словарь с ключами v и role; role — исходное имя DeviceRole.Child.</returns>
    public static IReadOnlyDictionary<string, string> Create(PeerAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        var records = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PeerDiscoveryConstants.TxtProtocolVersionKey] = advertisement.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            [PeerDiscoveryConstants.TxtRoleKey] = nameof(DeviceRole.Child)
        };

        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(records);
    }

    /// <summary>
    /// Разбирает TXT-записи peer. Неизвестные ключи игнорируются (forward compatibility),
    /// обязательные ключи и значения валидируются.
    /// </summary>
    /// <param name="txtRecords">TXT-записи из NSD.</param>
    /// <returns>Объявление peer или типизированная ошибка.</returns>
    public static Result<PeerAdvertisement> Parse(IReadOnlyDictionary<string, string>? txtRecords)
    {
        if (txtRecords is null)
        {
            return Result<PeerAdvertisement>.Failure(new Error(DiscoveryErrorCodes.InvalidTxt, "Peer не опубликовал TXT-записи."));
        }

        if (!txtRecords.TryGetValue(PeerDiscoveryConstants.TxtProtocolVersionKey, out var versionText)
            || !int.TryParse(versionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            || version is < 1 or > PeerDiscoveryConstants.CurrentProtocolVersion)
        {
            return Result<PeerAdvertisement>.Failure(new Error(DiscoveryErrorCodes.InvalidTxt, "Некорректная или неподдерживаемая версия discovery-протокола peer."));
        }

        // Wire-формат role — исходное имя DeviceRole.Child ("Child"), не числовое значение enum.
        if (!txtRecords.TryGetValue(PeerDiscoveryConstants.TxtRoleKey, out var roleText)
            || !string.Equals(roleText, nameof(DeviceRole.Child), StringComparison.Ordinal))
        {
            return Result<PeerAdvertisement>.Failure(new Error(DiscoveryErrorCodes.InvalidTxt, "Некорректная роль peer в TXT."));
        }

        return Result<PeerAdvertisement>.Success(new PeerAdvertisement(DeviceRole.Child, version));
    }
}
