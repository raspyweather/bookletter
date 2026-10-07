namespace Bookletter.Gui;

/// <summary>
/// The "Input / Output" group: the input PDF path and output folder, with Browse
/// buttons and drag-and-drop support for dropping a .pdf onto the group or the input
/// field itself (which highlights while a valid PDF is being dragged over it).
/// </summary>
public sealed class InputOutputPanel : UserControl
{
    private readonly TextBox _inputPathBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputDirBox = new() { Dock = DockStyle.Fill };

    /// <summary>Fired when the input PDF path changes - the only field here that affects the preview.</summary>
    public event EventHandler? InputPathChanged;

    public string InputPath => _inputPathBox.Text.Trim();

    public string OutputDir => _outputDirBox.Text.Trim();

    public InputOutputPanel()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var group = UiHelpers.NewGroup("Input / Output");
        var table = UiHelpers.NewTable(2);

        var browseInput = new Button { Text = "Browse...", AutoSize = true };
        browseInput.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                SetInputPath(dlg.FileName);
        };
        UiHelpers.AddRow(table, "Input PDF:", UiHelpers.WithButton(_inputPathBox, browseInput));

        var browseOutput = new Button { Text = "Browse...", AutoSize = true };
        browseOutput.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(_outputDirBox.Text) && Directory.Exists(_outputDirBox.Text))
                dlg.SelectedPath = _outputDirBox.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _outputDirBox.Text = dlg.SelectedPath;
        };
        UiHelpers.AddRow(table, "Output folder:", UiHelpers.WithButton(_outputDirBox, browseOutput));

        UiHelpers.FinishGroup(group, table);
        Controls.Add(group);

        EnableDrop(group, highlight: false);
        EnableDrop(_inputPathBox, highlight: true);

        _inputPathBox.TextChanged += (_, _) => InputPathChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Accepts a PDF dropped elsewhere in the window - MainForm wires the whole form as
    /// a drop target too, not just this panel - reusing the same
    /// set-input-and-default-the-output-folder logic as a local drop.
    /// </summary>
    public bool TryAcceptDrop(DragEventArgs e)
    {
        if (!UiHelpers.TryGetDroppedPdfPath(e, out string? path))
            return false;
        SetInputPath(path!);
        return true;
    }

    private void SetInputPath(string path)
    {
        _inputPathBox.Text = path;
        if (string.IsNullOrWhiteSpace(_outputDirBox.Text))
            _outputDirBox.Text = Path.Combine(Path.GetDirectoryName(path) ?? ".", "output");
    }

    private void EnableDrop(Control control, bool highlight)
    {
        control.AllowDrop = true;
        Color originalBack = control.BackColor;

        control.DragEnter += (sender, e) =>
        {
            e.Effect = UiHelpers.TryGetDroppedPdfPath(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
            if (highlight && e.Effect == DragDropEffects.Copy)
                control.BackColor = Color.LightYellow;
        };
        control.DragLeave += (_, _) =>
        {
            if (highlight)
                control.BackColor = originalBack;
        };
        control.DragDrop += (_, e) =>
        {
            if (highlight)
                control.BackColor = originalBack;
            TryAcceptDrop(e);
        };
    }
}
