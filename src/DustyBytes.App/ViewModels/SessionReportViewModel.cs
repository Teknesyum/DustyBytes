using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DustyBytes.App.Services;
using DustyBytes.Core.Model;

namespace DustyBytes.App.ViewModels;

public sealed partial class SessionReportViewModel : ObservableObject
{
    readonly MainViewModel _main;

    public SessionReportViewModel(MainViewModel main, SessionReport report)
    {
        _main = main;
        Report = report;
        Progress = main.NewProgress();
    }

    public SessionReport Report { get; }
    public TaskProgressViewModel Progress { get; }

    public string Title => Report.DryRun ? Report.Title + " provası bitti; dosyalar yerinde" : Report.Title + " bitti";
    public string SpaceText => Report.SpaceText;
    public string QuarantineText => Report.QuarantineText;
    public string PurgedText => Report.PurgedText;
    public bool HasQuarantined => Report.HasQuarantined && !Report.DryRun;
    public bool HasPurged => Report.HasPurged && !Report.DryRun;
    public string CleanText => Report.CleanText;
    public bool HasClean => Report.HasClean;
    public bool CanUndo => Report.CanUndo && !_undone;

    bool _undone;

    [RelayCommand]
    private async Task Undo()
    {
        if (!CanUndo || Progress.IsRunning)
            return;
        var response = await _main.Quarantine.RestoreSessionAsync(Report.Id, Progress);
        if (response is null)
            return;
        if (response.Ok)
        {
            _undone = true;
            _main.Session.RestoreUnits(Report.Units);
            _main.Notify($"{Format.Count(Report.QuarantinedCount)} öğe yerine döndü");
            Dismiss();
        }
        else
        {
            _main.Fail("Geri alma tamamlanamadı: " + response.Message);
        }
        OnPropertyChanged(nameof(CanUndo));
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (ReferenceEquals(_main.Report, this))
            _main.Report = null;
    }
}
