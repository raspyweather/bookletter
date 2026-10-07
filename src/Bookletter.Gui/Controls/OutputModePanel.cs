namespace Bookletter.Gui;

/// <summary>
/// The "Output mode" group: which file(s) get written. None of these settings affect
/// the rendered preview sheet, so this panel has no PreviewAffectingChanged event.
/// </summary>
public sealed class OutputModePanel : UserControl
{
    private readonly CheckBox _modeFrontBackChk = new() { Text = "Front/back PDFs (manual duplex)", AutoSize = true, Checked = true };
    private readonly CheckBox _modeDuplexChk = new() { Text = "Duplex PDF (auto-duplex printers)", AutoSize = true };
    private readonly CheckBox _modeImagesChk = new() { Text = "Loose image files", AutoSize = true };
    private readonly ComboBox _imageFormatCombo = UiHelpers.NewCombo("png", "jpg");
    private readonly NumericUpDown _jpegQualityNum = UiHelpers.NewNumeric(1, 100, 90, 5);

    public bool FrontBack => _modeFrontBackChk.Checked;
    public bool Duplex => _modeDuplexChk.Checked;
    public bool Images => _modeImagesChk.Checked;
    public string ImageFormat => _imageFormatCombo.SelectedItem?.ToString() ?? "png";
    public int JpegQuality => (int)_jpegQualityNum.Value;

    public OutputModePanel()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var group = UiHelpers.NewGroup("Output mode");
        var table = UiHelpers.NewTable(2);
        UiHelpers.AddFullRow(table, _modeFrontBackChk);
        UiHelpers.AddFullRow(table, _modeDuplexChk);
        UiHelpers.AddFullRow(table, _modeImagesChk);
        UiHelpers.AddRow(table, "Image format:", _imageFormatCombo);
        UiHelpers.AddRow(table, "JPEG quality:", _jpegQualityNum);
        UiHelpers.FinishGroup(group, table);
        Controls.Add(group);

        UpdateEnabledStates();
        _modeImagesChk.CheckedChanged += (_, _) => UpdateEnabledStates();
        _imageFormatCombo.SelectedIndexChanged += (_, _) => UpdateEnabledStates();
    }

    private void UpdateEnabledStates()
    {
        bool images = _modeImagesChk.Checked;
        _imageFormatCombo.Enabled = images;
        _jpegQualityNum.Enabled = images && _imageFormatCombo.SelectedItem?.ToString() == "jpg";
    }
}
