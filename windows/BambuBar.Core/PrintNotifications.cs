namespace BambuBar.Core;

public sealed record PrintNotice(string Kind, string TitleKey, string Body, bool IsError = false);

public sealed class PrintNotifications
{
    private readonly Dictionary<string, PrinterStatus> previous = [];
    public void Reset(string serial) => previous.Remove(serial);
    public IReadOnlyList<PrintNotice> Observe(string serial, PrinterStatus status)
    {
        if (!previous.TryGetValue(serial, out var old)) { previous[serial] = status; return []; }
        previous[serial] = status;
        var result = new List<PrintNotice>();
        var job = status.Job.Length > 0 ? status.Job : old.Job.Length > 0 ? old.Job : TextCatalog.Get("Untitled print");
        var newJob = old.TaskId is not null && status.TaskId is not null ? old.TaskId != status.TaskId : old.Job.Length > 0 && status.Job.Length > 0 && old.Job != status.Job;
        if (status.Active && (!old.Active || newJob)) result.Add(new("start", "Print started", job));
        if (old.Active && status.State == JobState.Finished)
            result.Add(new("end", "Print finished", TextCatalog.Get("%@ finished. It's ready to take off the plate.", job)));
        if (old.Active && status.State == JobState.Failed)
            result.Add(new("end", status.Canceled ? "Print canceled" : "Print failed", status.Canceled ? job :
                TextCatalog.Get("%@: %@", job, status.Issues.FirstOrDefault()?.Message ?? TextCatalog.Get("the printer stopped the job.")), !status.Canceled));
        if (old.State is JobState.Printing or JobState.Preparing && status.State == JobState.Paused)
            result.Add(new("pause", "Print paused", TextCatalog.Get("%@: %@", job, PrintStages.Describe(status))));
        foreach (var issue in status.Issues.Where(i => i.Severity <= IssueSeverity.Serious && old.Issues.All(o => o.Code != i.Code)))
            result.Add(new("error", issue.Severity == IssueSeverity.Fatal ? "Error" : "Warning", issue.Message, true));
        return result;
    }
}
