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
    /// <returns>Словарь с ключами v и role.</returns>
    public static IReadOnlyDictionary<string, string> Create(PeerAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PeerDiscoveryConstants.TxtProtocolVersionKey] = advertisement.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
            [PeerDiscoveryConstants.TxtRoleKey] = advertisement.Role.ToString()
        };
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
            || version <= 0)
        {
            return Result<PeerAdvertisement>.Failure(new Error(DiscoveryErrorCodes.InvalidTxt, "Некорректная версия discovery-протокола peer."));
        }

        if (!txtRecords.TryGetValue(PeerDiscoveryConstants.TxtRoleKey, out var roleText)
            || !Enum.TryParse<DeviceRole>(roleText, ignoreCase: false, out var role)
            || role is not DeviceRole.Child)
        {
            return Result<PeerAdvertisement>.Failure(new Error(DiscoveryErrorCodes.InvalidTxt, "Некорректная роль peer в TXT."));
        }

        return Result<PeerAdvertisement>.Success(new PeerAdvertisement(role, version));
    }
}
