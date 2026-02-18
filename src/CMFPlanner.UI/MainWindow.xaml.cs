using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CMFPlanner.Core.Models;
using CMFPlanner.Dicom;
using CMFPlanner.Plugins;

namespace CMFPlanner.UI;

public partial class MainWindow : Window
{
    private readonly PluginManager _pluginManager;
    private readonly IDicomLoader  _dicomLoader;
    private DicomVolume?           _currentVolume;

    // Workflow step definitions — order matches the clinical workflow.
    private static readonly (string Title, string Description)[] Steps =
    [
        ("Import DICOM",      "Load CT or CBCT series"),
        ("Segmentation",      "Segment bone and soft tissue"),
        ("Identify Segments", "Skull, mandible, teeth"),
        ("Dental Casts",      "Align STL arches to CT model"),
        ("NHP",               "Natural head position"),
        ("Osteotomies",       "Plan virtual bone cuts"),
        ("Segment Movement",  "Reposition bones in 3D"),
        ("Splint Generation", "Compute surgical splints"),
        ("Export",            "STL files and PDF report"),
    ];

    public MainWindow(PluginManager pluginManager, IDicomLoader dicomLoader)
    {
        _pluginManager = pluginManager;
        _dicomLoader   = dicomLoader;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Startup ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulateWorkflowSteps();
        RefreshStatusBar();
    }

    // ── Open DICOM ─────────────────────────────────────────────────────────

    private async void OpenDicom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select DICOM Series Folder",
        };

        if (dialog.ShowDialog() != true) return;

        await LoadDicomFolderAsync(dialog.FolderName);
    }

    private async Task LoadDicomFolderAsync(string folderPath)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingProgress.IsIndeterminate = true;
        LoadingText.Text = "Scanning DICOM files…";
        StatusActiveTool.Text = "Loading…";
        StatusMeasurements.Text = string.Empty;
        StatusMessages.Text = string.Empty;

        var progress = new Progress<(int Done, int Total)>(p =>
        {
            LoadingProgress.IsIndeterminate = false;
            LoadingProgress.Value = p.Total > 0 ? (double)p.Done / p.Total * 100.0 : 0;
            LoadingText.Text = $"Reading file {p.Done} of {p.Total}…";
        });

        try
        {
            _currentVolume = await _dicomLoader.LoadSeriesAsync(folderPath, progress);

            StatusActiveTool.Text = "Ready";
            StatusMeasurements.Text =
                $"{_currentVolume.PatientName}  ·  {FormatStudyDate(_currentVolume.StudyDate)}  ·  {_currentVolume.Modality}";
            StatusMessages.Text =
                $"{_currentVolume.SliceCount} slices  {_currentVolume.Rows}×{_currentVolume.Columns}  " +
                $"{_currentVolume.PixelSpacingX:F3}×{_currentVolume.PixelSpacingY:F3}×{_currentVolume.SliceThickness:F3} mm";
        }
        catch (OperationCanceledException)
        {
            StatusActiveTool.Text = "Load cancelled";
        }
        catch (Exception ex)
        {
            StatusActiveTool.Text = "Load failed";
            StatusMessages.Text = ex.Message;
            MessageBox.Show(ex.Message, "DICOM Load Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatStudyDate(string raw)
    {
        // DICOM date: YYYYMMDD → YYYY-MM-DD
        if (raw.Length == 8 && raw.All(char.IsDigit))
            return $"{raw[..4]}-{raw[4..6]}-{raw[6..]}";
        return raw;
    }

    // ── Workflow step list ─────────────────────────────────────────────────

    private void PopulateWorkflowSteps()
    {
        var separatorColor = Color.FromRgb(0x3E, 0x3E, 0x42);
        var subtleColor    = Color.FromRgb(0x85, 0x85, 0x85);
        var badgeColor     = Color.FromRgb(0x3E, 0x3E, 0x42);

        for (int i = 0; i < Steps.Length; i++)
        {
            var (title, description) = Steps[i];
            int number = i + 1;

            var item = new Border
            {
                Background      = Brushes.Transparent,
                BorderBrush     = new SolidColorBrush(separatorColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(10, 8, 10, 8),
                Cursor          = Cursors.Hand,
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Width               = 20,
                Height              = 20,
                Background          = new SolidColorBrush(badgeColor),
                CornerRadius        = new CornerRadius(10),
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(0, 2, 0, 0),
                Child = new TextBlock
                {
                    Text                = number.ToString(),
                    Foreground          = new SolidColorBrush(subtleColor),
                    FontSize            = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(badge, 0);

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text         = title,
                Foreground   = new SolidColorBrush(subtleColor),
                FontSize     = 12,
                FontWeight   = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
            });
            textStack.Children.Add(new TextBlock
            {
                Text         = description,
                Foreground   = new SolidColorBrush(subtleColor),
                FontSize     = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 1, 0, 0),
            });
            Grid.SetColumn(textStack, 1);

            row.Children.Add(badge);
            row.Children.Add(textStack);
            item.Child = row;

            WorkflowStepsPanel.Children.Add(item);
        }
    }

    // ── Status bar ─────────────────────────────────────────────────────────

    private void RefreshStatusBar()
    {
        int count = _pluginManager.LoadedPlugins.Count;
        StatusMessages.Text = count > 0
            ? $"{count} plugin{(count == 1 ? "" : "s")} loaded"
            : string.Empty;
    }

    // ── Menu handlers ──────────────────────────────────────────────────────

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuToggleWorkflow_Click(object sender, RoutedEventArgs e)
    {
        bool show = MenuToggleWorkflow.IsChecked;
        LeftPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LeftColumn.Width = show ? new GridLength(200) : new GridLength(0);
    }

    private void MenuToggleProperties_Click(object sender, RoutedEventArgs e)
    {
        bool show = MenuToggleProperties.IsChecked;
        RightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightColumn.Width = show ? new GridLength(250) : new GridLength(0);
    }
}
