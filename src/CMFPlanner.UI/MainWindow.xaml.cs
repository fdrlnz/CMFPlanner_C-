using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CMFPlanner.Core.Models;
using CMFPlanner.Core.Session;
using CMFPlanner.Dicom;
using CMFPlanner.Plugins;
using CMFPlanner.Visualization;
using HelixToolkit.Wpf;

namespace CMFPlanner.UI;

public partial class MainWindow : Window
{
    private readonly PluginManager   _pluginManager;
    private readonly IDicomLoader    _dicomLoader;
    private readonly ISessionState   _sessionState;
    private readonly IVolumeBuilder  _volumeBuilder;
    private readonly IMeshExtractor  _meshExtractor;

    private DicomVolume? _currentVolume;
    private VolumeData?  _volumeData;

    // Bone isovalue in Hounsfield Units.
    private const short BoneThreshold = 400;

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

    public MainWindow(
        PluginManager   pluginManager,
        IDicomLoader    dicomLoader,
        ISessionState   sessionState,
        IVolumeBuilder  volumeBuilder,
        IMeshExtractor  meshExtractor)
    {
        _pluginManager = pluginManager;
        _dicomLoader   = dicomLoader;
        _sessionState  = sessionState;
        _volumeBuilder = volumeBuilder;
        _meshExtractor = meshExtractor;
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
        LoadingOverlay.Visibility       = Visibility.Visible;
        LoadingProgress.IsIndeterminate = true;
        LoadingText.Text                = "Scanning DICOM files…";
        StatusActiveTool.Text           = "Loading…";
        StatusMeasurements.Text         = string.Empty;
        StatusMessages.Text             = string.Empty;

        try
        {
            // ── Phase 1: read metadata / sort slices ──────────────────────

            var phase1 = new Progress<(int Done, int Total)>(p =>
            {
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Value = p.Total > 0 ? (double)p.Done / p.Total * 100.0 : 0;
                LoadingText.Text = $"Reading DICOM file {p.Done} of {p.Total}…";
            });

            _currentVolume = await _dicomLoader.LoadSeriesAsync(folderPath, phase1);
            _sessionState.DicomVolume = _currentVolume;

            // ── Phase 2: load pixel data and build vtkImageData ───────────

            LoadingProgress.IsIndeterminate = true;
            LoadingText.Text = "Building 3D volume…";

            var phase2 = new Progress<(int Done, int Total)>(p =>
            {
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Value = p.Total > 0 ? (double)p.Done / p.Total * 100.0 : 0;
                LoadingText.Text = $"Processing slice {p.Done} of {p.Total}…";
            });

            _volumeData = await _volumeBuilder.BuildAsync(_currentVolume, phase2);
            _sessionState.VolumeData = _volumeData;

            // ── Phase 3: marching cubes → bone mesh ───────────────────────

            LoadingProgress.IsIndeterminate = true;
            LoadingText.Text = "Extracting bone surface…";

            var phase3 = new Progress<(int Done, int Total)>(p =>
            {
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Value = p.Total > 0 ? (double)p.Done / p.Total * 100.0 : 0;
                LoadingText.Text = $"Marching cubes row {p.Done} of {p.Total}…";
            });

            var meshData = await _meshExtractor.ExtractAsync(_volumeData, BoneThreshold, 2, phase3);
            DisplayBoneMesh(meshData);

            // ── Update status bar ─────────────────────────────────────────

            StatusActiveTool.Text = "Ready";
            StatusMeasurements.Text =
                $"{_currentVolume.PatientName}  ·  {FormatStudyDate(_currentVolume.StudyDate)}  ·  {_currentVolume.Modality}";
            StatusMessages.Text =
                $"{_currentVolume.SliceCount} slices  " +
                $"{_currentVolume.Rows}×{_currentVolume.Columns}  " +
                $"{_currentVolume.PixelSpacingX:F3}×{_currentVolume.PixelSpacingY:F3}×{_currentVolume.SpacingZ:F3} mm";
        }
        catch (OperationCanceledException)
        {
            StatusActiveTool.Text = "Load cancelled";
        }
        catch (Exception ex)
        {
            StatusActiveTool.Text = "Load failed";
            StatusMessages.Text   = ex.Message;
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
                Width             = 20,
                Height            = 20,
                Background        = new SolidColorBrush(badgeColor),
                CornerRadius      = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Top,
                Margin            = new Thickness(0, 2, 0, 0),
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

    // ── 3D viewport ────────────────────────────────────────────────────────

    private void DisplayBoneMesh(MeshData data)
    {
        if (data.VertexCount == 0) return;

        // Build WPF MeshGeometry3D from flat arrays.
        var geometry = new MeshGeometry3D();

        var positions = geometry.Positions = new Point3DCollection(data.VertexCount);
        var normals   = geometry.Normals   = new Vector3DCollection(data.VertexCount);
        var indices   = geometry.TriangleIndices = new Int32Collection(data.Indices.Length);

        float[] p = data.Positions;
        float[] n = data.Normals;
        int count = data.VertexCount;

        for (int i = 0; i < count; i++)
        {
            int o = i * 3;
            positions.Add(new Point3D(p[o], p[o+1], p[o+2]));
            normals.Add(new Vector3D(n[o], n[o+1], n[o+2]));
        }

        foreach (int idx in data.Indices)
            indices.Add(idx);

        // Bone-coloured material (warm off-white).
        var boneBrush = new SolidColorBrush(Color.FromRgb(230, 220, 200));
        var material  = new DiffuseMaterial(boneBrush);

        var model  = new GeometryModel3D(geometry, material) { BackMaterial = material };
        var visual = new ModelVisual3D { Content = model };

        // Remove any previous bone mesh, keep lights.
        var toRemove = Viewport3D.Children
            .OfType<ModelVisual3D>()
            .Where(v => v.Content is GeometryModel3D)
            .ToList();
        foreach (var v in toRemove)
            Viewport3D.Children.Remove(v);

        Viewport3D.Children.Add(visual);
        Viewport3D.ZoomExtents();
    }

    private void ResetCamera_Click(object sender, RoutedEventArgs e)
    {
        Viewport3D.ZoomExtents();
    }

    // ── Menu handlers ──────────────────────────────────────────────────────

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuToggleWorkflow_Click(object sender, RoutedEventArgs e)
    {
        bool show = MenuToggleWorkflow.IsChecked;
        LeftPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LeftColumn.Width     = show ? new GridLength(200) : new GridLength(0);
    }

    private void MenuToggleProperties_Click(object sender, RoutedEventArgs e)
    {
        bool show = MenuToggleProperties.IsChecked;
        RightPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightColumn.Width     = show ? new GridLength(250) : new GridLength(0);
    }
}
