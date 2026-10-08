using Bookletter.Imposition;

namespace Bookletter.Gui;

/// <summary>
/// The "Signature &amp; pages" group: signature size, which source pages to use, page
/// order, and the two checkboxes that only affect which files get written, not the
/// preview.
/// </summary>
public sealed class SignaturePagesPanel : UserControl
{
    private readonly NumericUpDown _signatureSizeNum = UiHelpers.NewNumeric(4, 128, 16, 4);
    // A print dialog's "Pages" field, not a numeric spinner - see PageSelector for the
    // accepted syntax ("3-20,25,30-35"; blank = every page; "13-" = page 13 to the end,
    // which is what used to be a separate "skip leading pages" option).
    private readonly TextBox _pagesBox = new() { Width = 120, PlaceholderText = "e.g. 3-20,25" };
    private readonly CheckBox _splitSignaturesChk = new() { Text = "Split into separate booklets per signature", AutoSize = true };
    private readonly CheckBox _reverseBackChk = new() { Text = "Reverse page order within the back file", AutoSize = true };
    private readonly ComboBox _pageOrderCombo = UiHelpers.NewCombo("Standard", "Reversed");
    private readonly ToolTip _toolTip = new();

    private const string PagesHelpText =
        "Which pages to use, and in what order - like a print dialog's \"Pages\" field.\n" +
        "Comma-separated pages and ranges, e.g. 3-20,25,30-35\n" +
        "Open-ended: 13- (page 13 to the end), -20 (page 1 to 20)\n" +
        "Blank = every page";

    /// <summary>Fired for a setting that affects the rendered preview sheet (not Split/Reverse, which only affect which files get written).</summary>
    public event EventHandler? PreviewAffectingChanged;

    public int SignatureSize => (int)_signatureSizeNum.Value;
    public string Pages => _pagesBox.Text.Trim();
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
        UiHelpers.AddRow(table, "Pages:", _pagesBox);
        _pageOrderCombo.Width = 100; // "Standard"/"Reversed" don't fit at UiHelpers.NewCombo's default width
        UiHelpers.AddRow(table, "Page order:", _pageOrderCombo);
        UiHelpers.AddFullRow(table, _splitSignaturesChk);
        UiHelpers.AddFullRow(table, _reverseBackChk);
        UiHelpers.FinishGroup(group, table);
        Controls.Add(group);

        _toolTip.SetToolTip(_pagesBox, PagesHelpText);

        _signatureSizeNum.ValueChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _pagesBox.TextChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
        _pageOrderCombo.SelectedIndexChanged += (_, _) => PreviewAffectingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Marks the Pages field as currently valid or invalid - a light red fill plus,
    /// on hover, the specific reason instead of the usual syntax reminder. Driven by
    /// MainForm re-resolving the field against the real source page count on the same
    /// debounce as the live preview, not a messagebox: a field that's only briefly
    /// invalid mid-edit (e.g. the instant after deleting a comma) shouldn't interrupt
    /// typing to complain about it.
    /// </summary>
    public void SetPagesValidity(bool valid, string? errorMessage = null)
    {
        _pagesBox.BackColor = valid ? SystemColors.Window : Color.MistyRose;
        _toolTip.SetToolTip(_pagesBox, valid ? PagesHelpText : errorMessage ?? PagesHelpText);
    }
}
