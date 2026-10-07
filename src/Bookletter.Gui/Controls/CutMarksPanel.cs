namespace Bookletter.Gui;

/// <summary>
/// The "Cut marks" group: whether to draw cut marks + overshoot fill, and their
/// styling. All sub-fields dim together when the checkbox is off.
/// </summary>
public sealed class CutMarksPanel : UserControl
{
    private readonly CheckBox _cutMarksChk = new() { Text = "Enable cut marks + overshoot fill", AutoSize = true };
    private readonly NumericUpDown _cutMarkGapNum = UiHelpers.NewNumeric(0, 50, 3, 1, 2);
    private readonly NumericUpDown _cutMarkLengthNum = UiHelpers.NewNumeric(0, 50, 5, 1, 2);
    private readonly NumericUpDown _cutMarkStrokeNum = UiHelpers.NewNumeric(0, 10, 0.25m, 0.05m, 2);
    private readonly Button _cutMarkColorBtn = UiHelpers.NewColorButton(Color.Black);
    private Color _cutMarkColor = Color.Black;

    public event EventHandler? PreviewAffectingChanged;

    public bool CutMarksEnabled => _cutMarksChk.Checked;
    public double GapPt => UiHelpers.MmToPt(_cutMarkGapNum.Value);
    public double LengthPt => UiHelpers.MmToPt(_cutMarkLengthNum.Value);
    public double StrokePt => (double)_cutMarkStrokeNum.Value;
    public Color MarkColor => _cutMarkColor;

    public CutMarksPanel()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var group = UiHelpers.NewGroup("Cut marks");
        var table = UiHelpers.NewTable(2);
        UiHelpers.AddFullRow(table, _cutMarksChk);
        UiHelpers.AddRow(table, "Mark gap (mm):", _cutMarkGapNum);
        UiHelpers.AddRow(table, "Mark length (mm):", _cutMarkLengthNum);
        UiHelpers.AddRow(table, "Mark stroke (pt):", _cutMarkStrokeNum);
        UiHelpers.AddRow(table, "Mark color:", _cutMarkColorBtn);
        UiHelpers.FinishGroup(group, table);
        Controls.Add(group);

        UpdateEnabledStates();
        _cutMarksChk.CheckedChanged += (_, _) => { UpdateEnabledStates(); PreviewAffectingChanged?.Invoke(this, EventArgs.Empty); };
        _cutMarkGapNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _cutMarkLengthNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _cutMarkStrokeNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _cutMarkColorBtn.Click += (_, _) =>
        {
            if (UiHelpers.TryPickColor(this, _cutMarkColor, out Color picked))
            {
                _cutMarkColor = picked;
                _cutMarkColorBtn.BackColor = picked;
                PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    private void UpdateEnabledStates()
    {
        bool enabled = _cutMarksChk.Checked;
        _cutMarkGapNum.Enabled = enabled;
        _cutMarkLengthNum.Enabled = enabled;
        _cutMarkStrokeNum.Enabled = enabled;
        _cutMarkColorBtn.Enabled = enabled;
    }
}
