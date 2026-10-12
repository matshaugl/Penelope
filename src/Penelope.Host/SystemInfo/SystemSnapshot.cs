namespace Penelope.Host.SystemInfo;

public sealed record Reading(
    bool Available,
    string? Reason,
    double? Percent,
    string? Detail,
    long? CapacityBytes = null,
    string? ActionLabel = null,
    string? ActionUrl = null);

public sealed record SystemSnapshot(Reading Cpu, Reading Memory, IReadOnlyList<Reading> Gpus, Reading Disk)
{
    public static SystemSnapshot Unavailable()
    {
        var reading = new Reading(false, "Penelope is not reading the computer yet.", null, null);
        return new SystemSnapshot(reading, reading, [reading], reading);
    }
}
