namespace Bookletter.Gui;

/// <summary>
/// The Run / Open-output-folder row at the bottom of the sidebar. The two main
/// buttons split the row into equal halves and each fill their own half, rather than
/// Run having a fixed width and the other slot just taking whatever's left - so they
/// render the same size whenever both happen to be visible at once (after a
/// successful run, before the next one starts). Progress itself is shown in the
/// window's status bar, not here - see MainForm's status progress bar.
/// </summary>
public sealed class RunRowPanel : UserControl
{
    private const int RowHeight = 32;

    // Open-output-folder and Cancel are mutually exclusive (the left slot is either
    // "nothing to do yet", "a run just finished", or "a run is in progress") - sharing
    // one Dock=Fill slot via Visible, rather than each getting their own column, so
    // neither one needs a second empty cell dragging the row's width down when it's
    // hidden.
    private readonly Panel _leftSlot = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0) };
    private readonly Button _openOutputBtn = new() { Text = "Open output folder", Dock = DockStyle.Fill, Visible = false };
    private readonly Button _cancelBtn = new() { Text = "Cancel", Dock = DockStyle.Fill, Visible = false };
    private readonly Button _runBtn = new()
    {
        Text = "Run",
        Dock = DockStyle.Fill,
        Margin = new Padding(4, 0, 0, 0)
    };

    public event EventHandler? RunClicked;
    public event EventHandler? OpenOutputClicked;
    public event EventHandler? CancelClicked;

    public RunRowPanel()
    {
        // Padding is set here, not by the caller, so Height can account for it: insetting
        // the content area with Padding alone (leaving Height = RowHeight) would squeeze
        // the Dock=Fill children down by Padding.Vertical - e.g. 32px minus a 20px
        // top+bottom padding leaves only a 12px-tall row.
        Padding = new Padding(8, 10, 8, 10);
        Height = RowHeight + Padding.Vertical;
        Margin = Padding.Empty;

        _leftSlot.Controls.Add(_openOutputBtn);
        _leftSlot.Controls.Add(_cancelBtn);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(_leftSlot, 0, 0);
        table.Controls.Add(_runBtn, 1, 0);
        Controls.Add(table);

        _runBtn.Click += (s, e) => RunClicked?.Invoke(this, e);
        _openOutputBtn.Click += (s, e) => OpenOutputClicked?.Invoke(this, e);
        _cancelBtn.Click += (s, e) =>
        {
            // Cancellation isn't instant (BookletGenerator only checks between
            // sheets), so disable immediately rather than leaving a window where a
            // second click could fire again before the first takes effect.
            _cancelBtn.Enabled = false;
            CancelClicked?.Invoke(this, e);
        };
    }

    public void BeginRun()
    {
        _runBtn.Enabled = false;
        _openOutputBtn.Visible = false;
        _cancelBtn.Visible = true;
        _cancelBtn.Enabled = true;
    }

    public void EndRun(bool succeeded)
    {
        _runBtn.Enabled = true;
        _cancelBtn.Visible = false;
        _openOutputBtn.Visible = succeeded;
    }
}
