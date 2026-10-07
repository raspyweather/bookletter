using Bookletter.Imposition;

namespace Bookletter.Gui;

/// <summary>
/// The "Signature &amp; pages" group: signature size, leading-page skip, page order,
/// and the two checkboxes that only affect which files get written, not the preview.
/// </summary>
public sealed class SignaturePagesPanel : UserControl
{
    private readonly NumericUpDown _signatureSizeNum = UiHelpers.NewNumeric(4, 128, 16, 4);
    private readonly NumericUpDown _skipPagesNum = UiHelpers.NewNumeric(0, 100000, 0, 1);
    private readonly CheckBox _splitSignaturesChk = new() { Text = "Split into separate booklets per signature", AutoSize = true };
    private readonly CheckBox _reverseBackChk = new() { Text = "Reverse page order within the back file", AutoSize = true };
    private readonly ComboBox _pageOrderCombo = UiHelpers.NewCombo("Standard", "Reversed");

    /// <summary>Fired for a setting that affects the rendered preview sheet (not Split/Reverse, which only affect which files get written).</summary>
    public event EventHandler? PreviewAffectingChanged;

    public int SignatureSize => (int)_signatureSizeNum.Value;
    public int SkipPages => (int)_skipPagesNum.Value;
    public bool SplitSignatures => _splitSignaturesChk.Checked;
    public bool ReverseBackOrder => _reverseBackChk.Checked;
    public PageOrderConvention PageOrder => _pageOrderCombo.SelectedIndex == 1 ? PageOrderConvention.Reversed : PageOrderConvention.Standard;

    public SignaturePagesPanel()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var group = UiHelpers.NewGroup("Signature && pages");
        var table = UiHelpers.NewTable(2);
        UiHelpers.AddRow(table, "Signature size (pages):", _signatureSizeNum);
        UiHelpers.AddRow(table, "Skip leading pages:", _skipPagesNum);
        UiHelpers.AddRow(table, "Page order:", _pageOrderCombo);
        UiHelpers.AddFullRow(table, _splitSignaturesChk);
        UiHelpers.AddFullRow(table, _reverseBackChk);
        UiHelpers.FinishGroup(group, table);
        Controls.Add(group);

        _signatureSizeNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _skipPagesNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _pageOrderCombo.SelectedIndexChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
    }
}
