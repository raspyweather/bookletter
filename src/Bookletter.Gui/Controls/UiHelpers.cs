namespace Bookletter.Gui;

/// <summary>
/// Shared WinForms-building helpers used across the sidebar's group panels, kept in
/// one place so every group looks and behaves consistently (label/control spacing,
/// numeric mouse-wheel stepping, drag-and-drop PDF detection, color-picker plumbing)
/// without each panel reinventing it.
/// </summary>
internal static class UiHelpers
{
    public static NumericUpDown NewNumeric(decimal min, decimal max, decimal value, decimal increment, int decimals = 0) => new StepNumericUpDown
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Increment = increment,
        DecimalPlaces = decimals,
        Width = 90
    };

    public static ComboBox NewCombo(params string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width  = 60 };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    public static Button NewColorButton(Color initial) => new() { Text = "", Width = 60, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = initial };

    public static GroupBox NewGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        Margin = new Padding(0, 0, 0, 8),
        BackColor = Control.DefaultBackColor
        // AutoSize is deliberately left off - GroupBox.AutoSize ignores its own
        // caption-bar height when sizing from its child (see MainForm.FixGroupBoxHeights).
    };

    /// <summary>
    /// Finishes a group box by adding its (Dock=Top, AutoSize) content. The actual
    /// height correction for the group's own caption bar happens later, in
    /// MainForm.FixGroupBoxHeights, once a real layout pass exists to measure from.
    /// </summary>
    public static GroupBox FinishGroup(GroupBox group, Control content)
    {
        group.Controls.Add(content);
        return group;
    }

    public static TableLayoutPanel NewTable(int columns)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        return table;
    }

    public static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
        control.Margin = new Padding(3);
        control.Anchor = AnchorStyles.Left;
        table.Controls.Add(lbl, 0, row);
        table.Controls.Add(control, 1, row);
    }

    public static void AddFullRow(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3);
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    public static Control WithButton(Control main, Control button)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        main.Dock = DockStyle.Fill;
        main.Margin = new Padding(0);
        button.Margin = new Padding(4, 0, 0, 0);
        panel.Controls.Add(main, 0, 0);
        panel.Controls.Add(button, 1, 0);
        return panel;
    }

    public static double MmToPt(decimal mm) => (double)mm / 25.4 * 72.0;

    public static bool TryGetDroppedPdfPath(DragEventArgs e, out string? path)
    {
        path = null;
        if (e.Data is null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return false;

        path = files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        return path is not null;
    }

    public static bool TryPickColor(IWin32Window owner, Color current, out Color picked)
    {
        using var dlg = new ColorDialog { Color = current, FullOpen = true };
        if (dlg.ShowDialog(owner) == DialogResult.OK)
        {
            picked = dlg.Color;
            return true;
        }
        picked = current;
        return false;
    }

    /// <summary>
    /// A NumericUpDown whose mouse wheel always steps by exactly one Increment per
    /// notch. The base control instead multiplies Increment by the OS's "lines to
    /// scroll" setting (commonly 3 on Windows), which makes e.g. the preview sheet
    /// selector jump by 3 sheets per notch instead of 1.
    /// </summary>
    public sealed class StepNumericUpDown : NumericUpDown
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            decimal step = e.Delta > 0 ? Increment : -Increment;
            Value = Math.Max(Minimum, Math.Min(Maximum, Value + step));
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
        }
    }
}
