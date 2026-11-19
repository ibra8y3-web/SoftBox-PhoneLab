using System.Windows.Media;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Desktop.Models;

public sealed class ComponentViewModel
{
    public ComponentDiagnostic Diagnostic { get; }
    public string Id => Diagnostic.Id;
    public string Name => Diagnostic.Name;
    public string Description => Diagnostic.Description;
    public string Evidence => Diagnostic.Evidence;
    public string StatusLabel => Diagnostic.StatusLabel;
    public Brush StatusBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Diagnostic.StatusColor));
    public string StatusHex => Diagnostic.StatusColor;

    public ComponentViewModel(ComponentDiagnostic diagnostic) => Diagnostic = diagnostic;
}
