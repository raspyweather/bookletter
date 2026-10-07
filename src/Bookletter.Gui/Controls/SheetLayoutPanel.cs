using System.Globalization;

namespace Bookletter.Gui;

/// <summary>
/// The "Sheet &amp; layout" group: physical sheet size, DPI, margins/gap, and
/// background color. Every setting here affects the rendered preview sheet.
/// </summary>
public sealed class SheetLayoutPanel : UserControl
{
    private readonly ComboBox _sheetSizeCombo = UiHelpers.NewCombo("auto", "A3", "A4", "A5", "Letter", "Legal", "Custom");
    private readonly NumericUpDown _customWidthNum = UiHelpers.NewNumeric(1, 2000, 210, 1, 1);
    private readonly NumericUpDown _customHeightNum = UiHelpers.NewNumeric(1, 2000, 297, 1, 1);
    private readonly NumericUpDown _dpiNum = UiHelpers.NewNumeric(72, 2400, 300, 50);
    private readonly NumericUpDown _marginNum = UiHelpers.NewNumeric(0, 200, 0, 1, 2);
    private readonly NumericUpDown _gapNum = UiHelpers.NewNumeric(0, 200, 0, 1, 2);
    private readonly CheckBox _allowUpscaleChk = new() { Text = "Allow upscaling pages to fill their cell", AutoSize = true };
    private readonly Button _backgroundColorBtn = UiHelpers.NewColorButton(Color.White);
    private Color _backgroundColor = Color.White;

    public event EventHandler? PreviewAffectingChanged;

    /// <summary>The CliOptions SheetSize token: a preset name, "auto", or a resolved "WxHmm" for Custom.</summary>
    public string SheetSizeToken
    {
        get
        {
            string selection = _sheetSizeCombo.SelectedItem?.ToString() ?? "auto";
            return selection == "Custom"
                ? $"{_customWidthNum.Value.ToString(CultureInfo.InvariantCulture)}x{_customHeightNum.Value.ToString(CultureInfo.InvariantCulture)}mm"
                : selection;
        }
    }

    public int Dpi => (int)_dpiNum.Value;
    public double MarginPt => UiHelpers.MmToPt(_marginNum.Value);
    public double GapPt => UiHelpers.MmToPt(_gapNum.Value);
    public Color BackgroundColor => _backgroundColor;
    public bool AllowUpscale => _allowUpscaleChk.Checked;

    public SheetLayoutPanel()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var group = UiHelpers.NewGroup("Sheet && layout");
        var table = UiHelpers.NewTable(2);
        UiHelpers.AddRow(table, "Sheet size:", _sheetSizeCombo);
        UiHelpers.AddRow(table, "Custom width (mm):", _customWidthNum);
        UiHelpers.AddRow(table, "Custom height (mm):", _customHeightNum);
        UiHelpers.AddRow(table, "DPI:", _dpiNum);
        UiHelpers.AddRow(table, "Margin (mm):", _marginNum);
        UiHelpers.AddRow(table, "Gap between pages (mm):", _gapNum);
        UiHelpers.AddRow(table, "Background color:", _backgroundColorBtn);
        UiHelpers.AddFullRow(table, _allowUpscaleChk);
        UiHelpers.FinishGroup(group, table);
        Controls.Add(group);

        UpdateEnabledStates();
        _sheetSizeCombo.SelectedIndexChanged += (_, _) => { UpdateEnabledStates(); PreviewAffectingChanged?.Invoke(this, EventArgs.Empty); };
        _customWidthNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _customHeightNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _dpiNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _marginNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _gapNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _allowUpscaleChk.CheckedChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _backgroundColorBtn.Click += (_, _) =>
        {
            if (UiHelpers.TryPickColor(this, _backgroundColor, out Color picked))
            {
                _backgroundColor = picked;
                _backgroundColorBtn.BackColor = picked;
                PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    private void UpdateEnabledStates()
    {
        bool custom = _sheetSizeCombo.SelectedItem?.ToString() == "Custom";
        _customWidthNum.Enabled = custom;
        _customHeightNum.Enabled = custom;
    }
}
