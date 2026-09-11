namespace Atlas.Worker;

/// <summary>Bound from the "OverdueTicketWorker" configuration section.</summary>
public sealed class OverdueTicketWorkerOptions
{
    public const string SectionName = "OverdueTicketWorker";

    /// <summary>
    /// How often to check for newly-overdue tickets. 5 minutes by default —
    /// frequent enough to be useful, not so frequent that a quiet database
    /// gets hammered with an empty query every few seconds. Clamped to at
    /// least 1 minute in <see cref="OverdueTicketWorker"/> regardless of what
    /// this is set to, so a typo (e.g. 0) can't spin the loop.
    /// </summary>
    public int PollingIntervalMinutes { get; set; } = 5;
}
