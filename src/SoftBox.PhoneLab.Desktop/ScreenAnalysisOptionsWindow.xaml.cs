using System.Windows;
using System.Windows.Controls;

namespace SoftBox.PhoneLab.Desktop;

public partial class ScreenAnalysisOptionsWindow : Window
{
    public string PatternKind { get; private set; } = "white";
    public ScreenAnalysisOptionsWindow() => InitializeComponent();

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        PatternKind = (PatternCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unknown";
        DialogResult = true;
    }
}