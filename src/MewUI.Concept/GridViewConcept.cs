namespace Aprillz.MewUI.Concept;


sealed class Person
{
    public ObservableValue<bool> IsChecked { get; } = new(false);

    public required string Name { get; init; }

    public ObservableValue<bool> Status { get; } = new();

    public ObservableValue<double> Progress { get; } = new(0);
}

sealed class ComplexPersonRow
{
    public ComplexPersonRow(string name, int roleIndex, bool isOnline, double progress, double score)
    {
        Name = new ObservableValue<string>(name ?? string.Empty);
        RoleIndex = new ObservableValue<int>(roleIndex, v => Math.Clamp(v, 0, 2));
        IsOnline = new ObservableValue<bool>(isOnline);
        IsSelected = new ObservableValue<bool>(false);
        Progress = new ObservableValue<double>(progress, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
        Score = new ObservableValue<double>(score, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
    }

    public ObservableValue<bool> IsSelected { get; }
    public ObservableValue<string> Name { get; }
    public ObservableValue<int> RoleIndex { get; }
    public ObservableValue<bool> IsOnline { get; }
    public ObservableValue<double> Progress { get; }
    public ObservableValue<double> Score { get; }
}
