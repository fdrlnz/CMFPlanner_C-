using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CMFPlanner.Core.Models;
using CMFPlanner.Core.Session;
using CMFPlanner.Dicom;
using CMFPlanner.Plugins;
using CMFPlanner.Segmentation;
using CMFPlanner.Visualization;
using HelixToolkit.Wpf;

namespace CMFPlanner.UI;

public partial class MainWindow : Window
{
    private readonly PluginManager        _pluginManager;
    private readonly IDicomLoader         _dicomLoader;
    private readonly ISessionState        _sessionState;
    private readonly IVolumeBuilder       _volumeBuilder;
    private readonly ISegmentationService _segmentationService;

    private DicomVolume? _currentVolume;
    private VolumeData?  _volumeData;

    private readonly AdaptiveGridVisual3D _grid = new();

    // Segmentation thresholds (Hounsfield Units), updated by live sliders.
    private short _boneThreshold       = 400;
    private short _softTissueThreshold = -100;

    // Cancellation source for the 300 ms debounced segmentation updates.
    private CancellationTokenSource? _segmentationCts;

    // Named mesh visuals — kept so we can remove/replace them individually.
    private ModelVisual3D? _boneMeshVisual;
    private ModelVisual3D? _softTissueMeshVisual;

    // Set to true once the camera has been fitted to the model (first load only).
    private bool _hasZoomedToModel;

    // Stride for live-preview marching cubes (lower = faster, coarser mesh).
    private const int PreviewStride = 3;

    // Cancellation source for the final (full-resolution) segmentation pipeline.
    private CancellationTokenSource? _finalSegCts;

    // Stores the confirmed final meshes (set after "Apply Segmentation" completes).
    private MeshData? _finalBoneMesh;
    private MeshData? _finalSoftTissueMesh;

    // User-visible workflow steps (segmentation and segment ID run automatically).
    private static readonly (string Title, string Description)[] Steps =
    [
        ("Import DICOM",      "Load CT or CBCT series"),
        ("Dental Casts",      "Align STL arches to CT model"),
        ("NHP",               "Natural head position"),
        ("Osteotomies",       "Plan virtual bone cuts"),
        ("Segment Movement",  "Reposition bones in 3D"),
        ("Splint Generation", "Compute surgical splints"),
        ("Export",            "STL files and PDF report"),
    ];

    public MainWindow(
        PluginManager        pluginManager,
        IDicomLoader         dicomLoader,
        ISessionState        sessionState,
        IVolumeBuilder       volumeBuilder,
        ISegmentationService segmentationService)
    {
        _pluginManager       = pluginManager;
        _dicomLoader         = dicomLoader;
        _sessionState        = sessionState;
        _volumeBuilder       = volumeBuilder;
        _segmentationService = segmentationService;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Startup ────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TriplanarView.CursorHuChanged += hu =>
            StatusMessages.Text = $"HU: {hu}";

        Viewport3D.Children.Add(_grid);
        Viewport3D.CameraChanged += OnViewportCameraChanged;

        if (Viewport3D.Camera is PerspectiveCamera initCam)
            _grid.Update(initCam);

        PopulateWorkflowSteps();
        RefreshStatusBar();
    }

    private void OnViewportCameraChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport3D.Camera is PerspectiveCamera cam)
            _grid.Update(cam);
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

            // ── Phase 2: build HU buffer ───────────────────────────────────

            LoadingProgress.IsIndeterminate = true;
            LoadingText.Text = "Building HU volume…";

            var phase2 = new Progress<(int Done, int Total)>(p =>
            {
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Value = p.Total > 0 ? (double)p.Done / p.Total * 100.0 : 0;
                LoadingText.Text = $"Processing slice {p.Done} of {p.Total}…";
            });

            _volumeData = await _volumeBuilder.BuildAsync(_currentVolume, phase2);
            _sessionState.VolumeData = _volumeData;

            // ── Phase 3: show triplanar viewer (immediate) ─────────────────

            LoadingOverlay.Visibility = Visibility.Collapsed;

            string patientLabel =
                $"{_currentVolume.PatientName}  ·  {FormatStudyDate(_currentVolume.StudyDate)}  ·  {_currentVolume.Modality}";
            TriplanarView.SetVolume(_volumeData, patientLabel);
            ShowTriplanarView();

            StatusActiveTool.Text   = "2D Viewer";
            StatusMeasurements.Text = patientLabel;
            StatusMessages.Text =
                $"{_currentVolume.SliceCount} slices  " +
                $"{_currentVolume.Rows}×{_currentVolume.Columns}  " +
                $"{_currentVolume.PixelSpacingX:F3}×{_currentVolume.PixelSpacingY:F3}×{_currentVolume.SpacingZ:F3} mm";

            // ── Phase 4: segmentation in background (non-blocking) ─────────

            _hasZoomedToModel = false;
            BtnView3D.IsEnabled = false;
            NoToolText.Visibility       = Visibility.Collapsed;
            SegmentationPanel.Visibility = Visibility.Visible;
            _ = RunInitialSegmentationAsync();
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

    private async Task RunInitialSegmentationAsync()
    {
        // Give the initial segmentation its own CTS so slider events can cancel it
        // cleanly, preventing a concurrent uncancellable extraction from racing with
        // the debounced previews triggered by the slider.
        _segmentationCts?.Cancel();
        var oldCts = _segmentationCts;
        _segmentationCts = new CancellationTokenSource();
        var ct = _segmentationCts.Token;
        oldCts?.Dispose(); // null on first load — safe to dispose immediately

        try
        {
            await UpdateSegmentationPreviewAsync(ct);
            BtnView3D.IsEnabled   = true;
            StatusActiveTool.Text = "Ready — 3D View available";
        }
        catch (OperationCanceledException)
        {
            // Slider was moved during initial load — the debounced update will
            // run shortly with the new threshold. Enable the button so the user
            // can switch to 3D once that update completes.
            BtnView3D.IsEnabled = true;
        }
        catch (Exception ex)
        {
            BtnView3D.IsEnabled   = true;
            StatusActiveTool.Text = "Segmentation failed";
            StatusMessages.Text   = ex.Message;
        }
    }

    private static string FormatStudyDate(string raw)
    {
        if (raw.Length == 8 && raw.All(char.IsDigit))
            return $"{raw[..4]}-{raw[4..6]}-{raw[6..]}";
        return raw;
    }

    // ── Segmentation slider handlers ───────────────────────────────────────

    private void BoneThresholdSlider_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _boneThreshold = (short)Math.Clamp((int)e.NewValue, short.MinValue, short.MaxValue);
        // Guard: event fires during InitializeComponent before named fields are assigned.
        if (BoneThresholdLabel is not null)
            BoneThresholdLabel.Text = $"{_boneThreshold} HU";
        ScheduleSegmentationUpdate();
    }

    private void SoftTissueThresholdSlider_ValueChanged(
        object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _softTissueThreshold = (short)Math.Clamp((int)e.NewValue, short.MinValue, short.MaxValue);
        // Guard: same initialization-order issue as above.
        if (SoftTissueThresholdLabel is not null)
            SoftTissueThresholdLabel.Text = $"{_softTissueThreshold} HU";
        ScheduleSegmentationUpdate();
    }

    private void ScheduleSegmentationUpdate()
    {
        if (_volumeData == null) return;

        // Cancel the previous operation first, then capture it so we can defer
        // disposal until the new task's finally block runs.  Disposing the old CTS
        // immediately after Cancel() risks an ObjectDisposedException on the
        // background thread that is still reading the token mid-extraction.
        _segmentationCts?.Cancel();
        var oldCts = _segmentationCts;
        _segmentationCts = new CancellationTokenSource();
        _ = RunDebouncedSegmentationAsync(_segmentationCts.Token, oldCts);
    }

    private async Task RunDebouncedSegmentationAsync(
        CancellationToken ct,
        CancellationTokenSource? oldCts = null)
    {
        try
        {
            await Task.Delay(300, ct);
            await UpdateSegmentationPreviewAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Normal: user moved slider again or a newer request superseded this one.
            // Not an error — the next debounced run will take over.
        }
        catch (Exception ex)
        {
            StatusActiveTool.Text = "Segmentation error";
            StatusMessages.Text   = ex.Message;
        }
        finally
        {
            // Safe to dispose here: the old task has either finished or observed
            // its cancellation before this finally block runs.
            oldCts?.Dispose();
        }
    }

    // ── Apply Segmentation (final, full-resolution) ────────────────────────

    private async void BtnApplySegmentation_Click(object sender, RoutedEventArgs e)
    {
        if (_volumeData == null) return;

        // Cancel any in-progress final segmentation before starting a new one.
        _finalSegCts?.Cancel();
        var oldFinalCts = _finalSegCts;
        _finalSegCts = new CancellationTokenSource();
        var ct = _finalSegCts.Token;
        // Button is disabled during processing so no concurrent clicks can occur;
        // disposing the old CTS here (after cancel) is safe in this path.
        oldFinalCts?.Dispose();

        await RunFinalSegmentationAsync(ct);
    }

    private async Task RunFinalSegmentationAsync(CancellationToken ct)
    {
        if (_volumeData == null) return;

        BtnApplySegmentation.IsEnabled  = false;
        LoadingOverlay.Visibility        = Visibility.Visible;
        LoadingProgress.IsIndeterminate  = true;
        StatusActiveTool.Text            = "Applying segmentation…";

        var stepProgress = new Progress<string>(msg =>
        {
            LoadingText.Text      = msg;
            StatusActiveTool.Text = msg;
        });

        try
        {
            var (boneMesh, softMesh) = await _segmentationService.ApplyFinalSegmentationAsync(
                _volumeData,
                _boneThreshold,
                _softTissueThreshold,
                stepProgress,
                ct);

            _finalBoneMesh        = boneMesh;
            _finalSoftTissueMesh  = softMesh;

            // Replace preview meshes with the final high-quality meshes.
            DisplayBoneMesh(boneMesh);
            DisplaySoftTissueMesh(softMesh);

            if (!_hasZoomedToModel)
            {
                Viewport3D.ZoomExtents();
                _hasZoomedToModel = true;
            }

            ShowViewport3D();

            SegmentationStatusText.Text =
                $"Final — Bone {boneMesh.TriangleCount:N0} tri · Skin {softMesh.TriangleCount:N0} tri";
            StatusActiveTool.Text = "Final segmentation applied";
        }
        catch (OperationCanceledException)
        {
            StatusActiveTool.Text = "Segmentation cancelled";
        }
        catch (Exception ex)
        {
            StatusActiveTool.Text = "Segmentation failed";
            StatusMessages.Text   = ex.Message;
            MessageBox.Show(ex.Message, "Segmentation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingOverlay.Visibility       = Visibility.Collapsed;
            BtnApplySegmentation.IsEnabled  = true;
        }
    }

    private async Task UpdateSegmentationPreviewAsync(CancellationToken ct)
    {
        if (_volumeData == null) return;

        SegmentationStatusText.Text = "Updating…";

        // Run both extractions in parallel on background threads.
        var boneTask = _segmentationService.ExtractBoneSurfaceAsync(
            _volumeData, _boneThreshold, PreviewStride, ct: ct);
        var softTask = _segmentationService.ExtractSoftTissueSurfaceAsync(
            _volumeData, _softTissueThreshold, PreviewStride, ct: ct);

        MeshData[] results = await Task.WhenAll(boneTask, softTask);
        ct.ThrowIfCancellationRequested();

        // Opaque bone first, then semi-transparent skin (correct WPF 3D blending order).
        DisplayBoneMesh(results[0]);
        DisplaySoftTissueMesh(results[1]);

        if (!_hasZoomedToModel)
        {
            Viewport3D.ZoomExtents();
            _hasZoomedToModel = true;
        }

        SegmentationStatusText.Text =
            $"Bone {results[0].VertexCount:N0} verts · Skin {results[1].VertexCount:N0} verts";
    }

    // ── View toggle ────────────────────────────────────────────────────────

    private void BtnView2D_Click(object sender, RoutedEventArgs e) => ShowTriplanarView();
    private void BtnView3D_Click(object sender, RoutedEventArgs e) => ShowViewport3D();

    private void ShowTriplanarView()
    {
        TriplanarView.Visibility  = Visibility.Visible;
        Viewport3D.Visibility     = Visibility.Collapsed;
        BtnView2D.IsChecked       = true;
        BtnView3D.IsChecked       = false;
        BtnToggleGrid.Visibility  = Visibility.Collapsed;
        StatusMessages.Text       = string.Empty;
    }

    private void ShowViewport3D()
    {
        TriplanarView.Visibility  = Visibility.Collapsed;
        Viewport3D.Visibility     = Visibility.Visible;
        BtnView2D.IsChecked       = false;
        BtnView3D.IsChecked       = true;
        BtnToggleGrid.Visibility  = Visibility.Visible;
    }

    private void BtnToggleGrid_Click(object sender, RoutedEventArgs e)
    {
        if (BtnToggleGrid.IsChecked == true)
            Viewport3D.Children.Add(_grid);
        else
            Viewport3D.Children.Remove(_grid);
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

    // ── 3D mesh display ────────────────────────────────────────────────────

    /// <summary>
    /// Updates the bone mesh in the viewport.
    /// Bone is inserted before the soft-tissue mesh so it renders first
    /// (opaque objects before transparent ones for correct WPF 3D alpha blending).
    /// </summary>
    private void DisplayBoneMesh(MeshData data)
    {
        if (_boneMeshVisual != null)
        {
            Viewport3D.Children.Remove(_boneMeshVisual);
            _boneMeshVisual = null;
        }
        if (data.VertexCount == 0) return;

        var geometry = BuildGeometry(data);
        var color    = Color.FromArgb(210, 230, 220, 200); // warm white, mostly opaque
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        var model    = new GeometryModel3D(geometry, material) { BackMaterial = material };
        _boneMeshVisual = new ModelVisual3D { Content = model };

        var children = (IList<Visual3D>)Viewport3D.Children;
        int stIdx = _softTissueMeshVisual != null
            ? children.IndexOf(_softTissueMeshVisual)
            : -1;

        if (stIdx >= 0)
            children.Insert(stIdx, _boneMeshVisual); // before soft tissue
        else
            Viewport3D.Children.Add(_boneMeshVisual);
    }

    /// <summary>
    /// Updates the soft-tissue mesh in the viewport.
    /// Always appended last so the semi-transparent skin renders over the opaque bone.
    /// </summary>
    private void DisplaySoftTissueMesh(MeshData data)
    {
        if (_softTissueMeshVisual != null)
        {
            Viewport3D.Children.Remove(_softTissueMeshVisual);
            _softTissueMeshVisual = null;
        }
        if (data.VertexCount == 0) return;

        var geometry = BuildGeometry(data);
        var color    = Color.FromArgb(60, 255, 200, 170); // skin tone, semi-transparent
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        var model    = new GeometryModel3D(geometry, material) { BackMaterial = material };
        _softTissueMeshVisual = new ModelVisual3D { Content = model };
        Viewport3D.Children.Add(_softTissueMeshVisual); // transparent objects last
    }

    private static MeshGeometry3D BuildGeometry(MeshData data)
    {
        var geometry  = new MeshGeometry3D();
        var positions = geometry.Positions       = new Point3DCollection(data.VertexCount);
        var normals   = geometry.Normals          = new Vector3DCollection(data.VertexCount);
        var indices   = geometry.TriangleIndices  = new Int32Collection(data.Indices.Length);

        float[] p = data.Positions;
        float[] n = data.Normals;
        int     count = data.VertexCount;

        for (int i = 0; i < count; i++)
        {
            int o = i * 3;
            positions.Add(new Point3D(p[o], p[o + 1], p[o + 2]));
            normals.Add(new Vector3D(n[o], n[o + 1], n[o + 2]));
        }
        foreach (int idx in data.Indices)
            indices.Add(idx);

        return geometry;
    }

    private void ResetCamera_Click(object sender, RoutedEventArgs e)
        => Viewport3D.ZoomExtents();

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
