namespace HouseFlow.Application.Common;

/// <summary>
/// Device chips of the house banners (C4 cards on P07/P08, P04 invitation): one device type per device.
/// </summary>
public static class DeviceChips
{
    /// <summary>
    /// One <c>Device.Type</c> per device, oldest device first (id as a stable tie-break). Sorted in memory
    /// (not in SQL, whose uuid ordering differs from <see cref="Guid.CompareTo(Guid)"/>) so the house list,
    /// the house detail and the invitation always agree on the order.
    /// </summary>
    public static IReadOnlyList<string> TypesInCreationOrder(IEnumerable<(DateTime CreatedAt, Guid Id, string Type)> devices) =>
        devices.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).Select(d => d.Type).ToList();
}
