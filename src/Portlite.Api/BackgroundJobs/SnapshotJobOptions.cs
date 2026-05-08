namespace Portlite.Api.BackgroundJobs;

public class SnapshotJobOptions
{
    public const string SectionName = "SnapshotJob";

    public bool Enabled { get; set; } = true;
    public int DailyRunHourUtc { get; set; } = 22;
}
