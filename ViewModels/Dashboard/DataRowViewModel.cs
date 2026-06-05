namespace GeneralHostFrontend.ViewModels.Dashboard;

public sealed record DataRowViewModel(
    string Id,
    string Time,
    string Level,
    string Code,
    string Message,
    string Confirmed);
