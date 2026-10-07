using System.Globalization;

namespace Bookletter.Gui;

/// <summary>
/// The "Preview" tab's content: a sheet/side selector, a fixed-height status line, and
/// the rendered image with zoom. Pure view - it has no knowledge of BookletGenerator;
/// MainForm drives it by reading SheetNumber/ShowFront, calling SetStatus/ShowImage,
/// and listening for PreviewRequested when the user picks a different sheet or side.
/// </summary>
public sealed class PreviewPanel : UserControl
{
    private readonly NumericUpDown _sheetNum = UiHelpers.NewNumeric(1, 100000, 1, 1);
    private readonly ComboBox _sideCombo = UiHelpers.NewCombo("Front", "Back");
    private readonly ComboBox _zoomCombo = UiHelpers.NewCombo("Fit", "50%", "100%", "150%", "200%", "300%", "400%");
    private readonly Button _openImageBtn = new() { Text = "Open image", AutoSize = true, Enabled = false };
    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(4, 0, 4, 0),
        Text = ""
    };
    private readonly Panel _scrollPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 40) };
    private readonly PictureBox _imageBox = new()
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(60, 60, 60),
        BorderStyle = BorderStyle.FixedSingle
    };
    private byte[]? _lastPreviewPng;
    private bool _suppressEvent;

    /// <summary>Fired when the user picks a different sheet or side - not while a quiet, programmatic range update (SetSheetRangeQuietly) is in progress.</summary>
    public event EventHandler? PreviewRequested;

    public int SheetNumber => (int)_sheetNum.Value;
    public bool ShowFront => _sideCombo.SelectedIndex != 1;
    public int ViewportWidth => _scrollPanel.ClientSize.Width;

    /// <summary>
    /// Three stacked, independently-sized rows rather than one flowing toolbar: sheet
    /// selection (what to show), sheet information (a status line about it), and the
    /// rendered image. A single flowing row mixed the button, selectors, and the
    /// variable-length status label together, so a long status line could wrap the
    /// whole row and visibly shift the controls above the image on every settings
    /// change. Giving the status line its own fixed-height row (ellipsizing instead of
    /// wrapping) means the controls never move regardless of what it says.
    /// </summary>
    public PreviewPanel()
    {
        Dock = DockStyle.Fill;

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var controlsRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(4) };

        var sheetGroup = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        sheetGroup.Controls.Add(new Label { Text = "Sheet:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 7, 2, 3) });
        sheetGroup.Controls.Add(_sheetNum);
        sheetGroup.Controls.Add(_sideCombo);

        var viewGroup = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        viewGroup.Controls.Add(_zoomCombo);
        viewGroup.Controls.Add(_openImageBtn);

        controlsRow.Controls.Add(sheetGroup);
        controlsRow.Controls.Add(new Panel { Width = 1, Height = 24, Margin = new Padding(8, 2, 8, 2), BackColor = SystemColors.ControlDark });
        controlsRow.Controls.Add(viewGroup);

        _scrollPanel.Controls.Add(_imageBox);
        panel.Controls.Add(controlsRow, 0, 0);
        panel.Controls.Add(_statusLabel, 0, 1);
        panel.Controls.Add(_scrollPanel, 0, 2);
        Controls.Add(panel);

        _sideCombo.SelectedIndexChanged += (_, _) => PreviewRequested?.Invoke(this, EventArgs.Empty);
        _sheetNum.ValueChanged += (_, _) =>
        {
            if (!_suppressEvent)
                PreviewRequested?.Invoke(this, EventArgs.Empty);
        };
        _zoomCombo.SelectedIndexChanged += (_, _) => ApplyZoom();
        _openImageBtn.Click += (_, _) => OpenPreviewExternally();
        _imageBox.DoubleClick += (_, _) => OpenPreviewExternally();
        _scrollPanel.MouseWheel += OnPreviewMouseWheel;
    }

    public void SetStatus(string text) => _statusLabel.Text = text;

    public void SetSideEnabled(bool enabled) => _sideCombo.Enabled = enabled;
    // Deliberately NOT exposing a way to disable the sheet spinner itself: a disabled
    // control can't receive mouse wheel events at all, so disabling it while a render
    // is in flight would block further scrolling until that render finished - defeating
    // the point of being able to scroll quickly through sheets. MainForm's
    // pending-refresh coalescing already handles a value changing again mid-render.

    /// <summary>
    /// Sets the sheet spinner's Maximum to the real sheet count, so typing or
    /// scrolling past it is simply not possible. Lowering Maximum below the current
    /// Value makes NumericUpDown clamp Value automatically - that clamp would normally
    /// fire ValueChanged and re-trigger a preview render recursively, hence the
    /// suppress flag shared with the direct quiet case.
    /// </summary>
    public void SetSheetRangeQuietly(int totalSheets)
    {
        _suppressEvent = true;
        try { _sheetNum.Maximum = Math.Max(totalSheets, 1); }
        finally { _suppressEvent = false; }
    }

    public Size ShowImage(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var decoded = Image.FromStream(stream);
        var bitmap = new Bitmap(decoded); // deep copy - safe to keep after the stream closes

        _imageBox.Image?.Dispose();
        _imageBox.Image = bitmap;
        ApplyZoom();

        _lastPreviewPng = png;
        _openImageBtn.Enabled = true;
        return bitmap.Size;
    }

    private void OnPreviewMouseWheel(object? sender, MouseEventArgs e)
    {
        if (ModifierKeys != Keys.Control || _imageBox.Image is null)
            return;

        int idx = _zoomCombo.SelectedIndex;
        _zoomCombo.SelectedIndex = idx <= 0
            ? 2 // from "Fit", jump straight to 100% as a sensible anchor
            : Math.Clamp(idx + (e.Delta > 0 ? 1 : -1), 1, _zoomCombo.Items.Count - 1);

        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
    }

    private void ApplyZoom()
    {
        if (_imageBox.Image is null)
            return;

        string zoom = _zoomCombo.SelectedItem?.ToString() ?? "Fit";
        if (zoom == "Fit")
        {
            _imageBox.Dock = DockStyle.Fill;
        }
        else
        {
            double factor = double.Parse(zoom.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
            var img = _imageBox.Image;
            _imageBox.Dock = DockStyle.None;
            _imageBox.Location = Point.Empty;
            _imageBox.Size = new Size((int)(img.Width * factor), (int)(img.Height * factor));
        }
    }

    private void OpenPreviewExternally()
    {
        if (_lastPreviewPng is null)
        {
            MessageBox.Show(this, "Render a preview first.", "No preview yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string path = Path.Combine(Path.GetTempPath(), $"bookletter-preview-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, _lastPreviewPng);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open preview image", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
