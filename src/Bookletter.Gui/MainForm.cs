using System.Threading;
using Bookletter.Cli;
using Bookletter.Output;
using SkiaSharp;

namespace Bookletter.Gui;

public sealed class MainForm : Form
{
    private const int SidebarContentWidth = 300;

    private readonly InputOutputPanel _inputOutputPanel = new();
    private readonly SignaturePagesPanel _signaturePagesPanel = new();
    private readonly SheetLayoutPanel _sheetLayoutPanel = new();
    private readonly CutMarksPanel _cutMarksPanel = new();
    private readonly OutputModePanel _outputModePanel = new();
    private readonly RunRowPanel _runRow = new();
    private readonly PreviewPanel _previewPanel = new();

    private readonly TextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };

    private readonly System.Windows.Forms.Timer _autoPreviewTimer = new() { Interval = 500 };
    private bool _previewRenderInProgress;
    private bool _previewRefreshPending;
    private CancellationTokenSource? _runCts;

    private readonly TabControl _previewTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _signatureMapTab = new("Signature map");
    private readonly TabPage _logTab = new("Log");
    private readonly SignatureMapPanel _signatureMapPanel;

    // Default Padding.Bottom is 0, which lets Windows 11's rounded bottom corners
    // clip the leftmost label's text - a few px of breathing room avoids that.
    // AutoSize has to go off for this to actually add space rather than steal it:
    // with AutoSize left on, adding Padding.Bottom shrinks the strip's overall
    // height back down (confirmed - it does not grow to compensate), which leaves
    // just as little room as before, only now squeezed between Padding.Top and
    // Padding.Bottom instead of sitting flush at the bottom. The un-padded strip
    // rendered its label at Height=16 against a 20px font line-height even before any
    // of this - close enough that it wasn't obviously clipped, but descenders ("g",
    // "sheet 1 of 500"'s "g" in "Generating") were being cut at the exact pixel this
    // padding now shifts into view, which is what made it visible. Height=36 with
    // Padding.Bottom=6 gives the label a full, comfortably-clear 24px (confirmed
    // against the rendered glyphs, not just the nominal font height) and leaves 8px
    // of clear space below it.
    private readonly StatusStrip _statusStrip = new()
    {
        ShowItemToolTips = true,
        AutoSize = false,
        Height = 36,
        Padding = new Padding(1, 3, 14, 3)
    };
    // Spring=true (the usual way to make a status label fill the remaining width) is
    // deliberately NOT used here - confirmed, with a minimal repro involving no other
    // Bookletter code, that a StatusStrip with its default Table layout style
    // mispositions its item - lands it well outside the strip's own bounds, entirely
    // invisible, no exception or other symptom - whenever there's only one VISIBLE
    // item. With two or more it's always correct; this was true across every
    // variation tried (resize, explicit PerformLayout, repeated recalculation).
    // AutoSize=false + a manually computed Width (see UpdateStatusLabelWidth)
    // replaces what Spring would normally do.
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = false, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    // Page/signature/sheet counts, broken out into their own labels rather than
    // folded into _statusLabel's text, specifically so they survive _statusLabel
    // being overwritten with transient messages ("Generating booklet... sheet 123 of
    // 500") during a run - they're set once a preview resolves and then left alone
    // until the next one does, all the way through a conversion.
    private readonly ToolStripStatusLabel _pagesLabel = new() { AutoSize = true, Margin = new Padding(12, 3, 0, 2) };
    private readonly ToolStripStatusLabel _signaturesLabel = new() { AutoSize = true, Margin = new Padding(12, 3, 0, 2) };
    private readonly ToolStripStatusLabel _sheetsLabel = new() { AutoSize = true, Margin = new Padding(12, 3, 0, 2) };
    // A permanent, always-visible 1px item that exists purely to keep the "two or
    // more visible items" rule satisfied (see _statusLabel's comment) regardless of
    // whether the real progress bar is currently shown - ToolStripProgressBar itself
    // can't serve double duty here, since it enforces a ~100px minimum width (its
    // DefaultSize) and refuses to shrink smaller, which would otherwise leave a
    // permanent gap at the right edge even while idle.
    private readonly ToolStripStatusLabel _statusBarSpacer = new() { Text = "", AutoSize = false, Width = 1 };
    // Added to/removed from _statusStrip.Items only while a run is actually in
    // progress - see BeginRunProgress/EndRunProgress. Safe to do (unlike before
    // _statusBarSpacer existed) because the spacer above keeps the visible-item count
    // at two or more either way.
    private readonly ToolStripProgressBar _statusProgressBar = new() { Width = 150, Margin = new Padding(12, 3, 0, 2) };

    // Created in OnLoad, not the constructor - it needs a real window handle.
    private TaskbarProgress? _taskbarProgress;

    public MainForm()
    {
        Text = "Bookletter";
        Width = 1040;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 560);
        Padding = new Padding(0, 12, 0, 0); // a little breathing room under the title bar before the content starts
        // Pulled from the running .exe's own icon (set via <ApplicationIcon> in the
        // csproj) rather than loading the .ico as a separate loose file, so there's
        // only one place that needs updating to change it.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;

        _signatureMapPanel = new SignatureMapPanel(() => BuildOptions(forPreview: true));

        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_pagesLabel);
        _statusStrip.Items.Add(_signaturesLabel);
        _statusStrip.Items.Add(_sheetsLabel);
        _statusStrip.Items.Add(_statusBarSpacer);
        SetIdleStatus();
        UpdateStatusLabelWidth();
        _statusStrip.Resize += (_, _) => UpdateStatusLabelWidth();

        // The options panel doesn't need to reflow when the window is resized - only
        // the tabbed preview/map/log side should grow or shrink.
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(BuildOptionsPanel());
        split.Panel2.Controls.Add(BuildRightPanel());
        Controls.Add(split);
        Controls.Add(_statusStrip);
        // SplitterDistance is set only after the control is parented and sized by
        // Dock=Fill - setting it in the object initializer gets silently clamped
        // against the SplitContainer's un-parented default width (~150px).
        split.SplitterDistance = 320;

        // Interactive window resizing was dragging the whole control tree through a
        // full layout pass on every pixel of movement - fine for most of the window,
        // but the signature map's per-signature card grid made this scale badly for
        // large documents. Scoped to just the signature map tab (not the whole form)
        // so everything else - the sidebar, Preview tab, Log tab - keeps reflowing
        // live during the drag instead of jumping all at once at the end.
        ResizeBegin += (_, _) =>
        {
            _signatureMapPanel.Visible = false;
            /* _signatureMapTab.SuspendLayout();
             _signatureMapPanel.SuspendLayout();*/
        };
        ResizeEnd += (_, _) =>
        {
            _signatureMapPanel.Visible = true;
            /*   _signatureMapPanel.ResumeLayout(true);
               _signatureMapTab.ResumeLayout(true);*/
        };

        WireEvents();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _taskbarProgress = new TaskbarProgress(Handle);

        // GroupBox.AutoSize cannot be trusted to size the box to fit its content (see
        // UiHelpers.FinishGroup), so group heights are corrected here instead, using
        // each group's real rendered content bounds from an actual layout pass - the
        // one measurement that's never wrong, unlike any prediction made before the
        // control tree has its final size. This only works once the form has a real
        // window handle (hence doing it here, in Load, rather than in the
        // constructor) - the sidebar's AutoScroll panel doesn't know whether it needs
        // a scrollbar until then. It also has to iterate rather than run once: making
        // groups taller can push the sidebar's total content past its visible height,
        // which brings in that scrollbar, which narrows the available width, which
        // can make a long checkbox label wrap onto an extra line - which changes how
        // tall that group needs to be. Repeating until a pass changes nothing settles
        // that feedback loop instead of leaving it mid-cycle. This recurses through
        // the sidebar's custom-control panels the same way it used to recurse through
        // plain GroupBoxes - Controls traversal doesn't care about UserControl
        // boundaries.
        PerformLayout();
        for (int i = 0; i < 5 && FixGroupBoxHeights(this); i++)
            PerformLayout();
    }

    private static bool FixGroupBoxHeights(Control root)
    {
        bool changedAny = false;
        foreach (Control child in root.Controls)
        {
            if (child is GroupBox group && group.Controls.Count > 0)
            {
                int needed = group.Controls[0].Bottom + group.Padding.Bottom;
                if (needed != group.Height)
                {
                    group.Height = needed;
                    changedAny = true;
                }
            }
            changedAny |= FixGroupBoxHeights(child);
        }
        return changedAny;
    }

    private Control BuildOptionsPanel()
    {
        var container = new Panel { Dock = DockStyle.Fill };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8), ForeColor = Color.Black };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = SidebarContentWidth,
        };
        stack.Controls.Add(_inputOutputPanel);
        stack.Controls.Add(_signaturePagesPanel);
        stack.Controls.Add(_sheetLayoutPanel);
        stack.Controls.Add(_cutMarksPanel);
        stack.Controls.Add(_outputModePanel);
        scroll.Controls.Add(stack);

        // Run/Open-output-folder/progress sit outside the scrollable area so they're
        // always visible without scrolling, regardless of how many options are shown.
        // A divider line plus a distinct background mark it as a separate "toolbar"
        // area rather than just trailing off the bottom of the scrollable content.
        _runRow.Dock = DockStyle.Bottom;

        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        container.Controls.Add(scroll);
        container.Controls.Add(divider);
        container.Controls.Add(_runRow);
        return container;
    }

    private Control BuildRightPanel() => BuildPreviewTabs();

    private Control BuildPreviewTabs()
    {
        var previewTab = new TabPage("Preview");
        previewTab.Controls.Add(_previewPanel);

        _signatureMapTab.Controls.Add(_signatureMapPanel);

        _logTab.Controls.Add(_logBox);
        _logTab.Padding = new Padding(4);

        _previewTabs.TabPages.Add(previewTab);
        _previewTabs.TabPages.Add(_signatureMapTab);
        _previewTabs.TabPages.Add(_logTab);
        _previewTabs.SelectedIndexChanged += async (_, _) =>
        {
            if (_previewTabs.SelectedTab == _signatureMapTab)
                await _signatureMapPanel.RefreshMapAsync();
        };

        return _previewTabs;
    }

    private void WireEvents()
    {
        _runRow.RunClicked += OnRunClick;
        _runRow.OpenOutputClicked += (_, _) => OpenOutputFolder();
        _runRow.CancelClicked += (_, _) =>
        {
            _runCts?.Cancel();
            SetStatus("Cancelling...");
        };

        _previewPanel.PreviewRequested += async (_, _) => await RunPreviewAsync();

        _autoPreviewTimer.Tick += async (_, _) =>
        {
            _autoPreviewTimer.Stop();
            await RunPreviewAsync(silent: true);
            if (_previewTabs.SelectedTab == _signatureMapTab)
                await _signatureMapPanel.RefreshMapAsync();
        };

        EnableInputPdfDrop(this);

        // Auto-refresh the preview (debounced) whenever a setting that affects the
        // rendered sheet changes. Settings that only affect which files get written
        // (output dir, split-signatures, reverse-back-order, mode, image format/quality)
        // deliberately don't trigger a re-render - see each panel's PreviewAffectingChanged.
        _inputOutputPanel.InputPathChanged += (_, _) =>
        {
            // Instant feedback - the file name needs no PDF reading at all, so it
            // doesn't have to wait for the debounced preview below to resolve (which
            // also needs every other setting to be valid first, not just the path).
            SetIdleStatus();
            // The counts are for whatever file was last resolved - clear them rather
            // than let them linger describing a now-different file until the new one
            // resolves.
            _pagesLabel.Text = "";
            _signaturesLabel.Text = "";
            _sheetsLabel.Text = "";
            UpdateStatusLabelWidth();
            ScheduleAutoPreview();
        };
        _signaturePagesPanel.PreviewAffectingChanged += (_, _) => ScheduleAutoPreview();
        _sheetLayoutPanel.PreviewAffectingChanged += (_, _) => ScheduleAutoPreview();
        _cutMarksPanel.PreviewAffectingChanged += (_, _) => ScheduleAutoPreview();
    }

    private void ScheduleAutoPreview()
    {
        _autoPreviewTimer.Stop();
        _autoPreviewTimer.Start();
    }

    private void OpenOutputFolder()
    {
        string dir = _inputOutputPanel.OutputDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(dir)) { UseShellExecute = true });
    }

    /// <summary>
    /// Lets the user drop a .pdf file (e.g. from Explorer) onto <paramref name="control"/>
    /// to set it as the input PDF. Wired onto the whole window (InputOutputPanel wires
    /// its own textbox/group separately), since a control only accepts drops if it
    /// individually opts in - it doesn't bubble up from children automatically.
    /// </summary>
    private void EnableInputPdfDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (sender, e) => e.Effect = UiHelpers.TryGetDroppedPdfPath(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (sender, e) => _inputOutputPanel.TryAcceptDrop(e);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>Fills the status label into whatever space the other permanent items aren't using - see the field comment on _statusLabel for why this is manual instead of Spring.</summary>
    private void UpdateStatusLabelWidth()
    {
        int otherWidth = _statusBarSpacer.Width + _statusBarSpacer.Margin.Horizontal
            + _pagesLabel.Width + _pagesLabel.Margin.Horizontal
            + _signaturesLabel.Width + _signaturesLabel.Margin.Horizontal
            + _sheetsLabel.Width + _sheetsLabel.Margin.Horizontal;
        if (_statusStrip.Items.Contains(_statusProgressBar))
            otherWidth += _statusProgressBar.Width + _statusProgressBar.Margin.Horizontal;
        _statusLabel.Width = Math.Max(_statusStrip.DisplayRectangle.Width - otherWidth - _statusLabel.Margin.Horizontal, 0);
    }

    /// <summary>
    /// Shows the file name (instant - no PDF reading needed) or "No PDF selected" -
    /// the status bar's resting state between transient messages. The page/signature/
    /// sheet counts live in their own labels (see RunPreviewAsync), not here, so they
    /// stay in place through a run instead of being overwritten by it.
    /// </summary>
    private void SetIdleStatus()
    {
        string input = _inputOutputPanel.InputPath;
        SetStatus(string.IsNullOrEmpty(input) ? "No PDF selected" : Path.GetFileName(input));
    }

    private void BeginRunProgress()
    {
        _statusProgressBar.Style = ProgressBarStyle.Blocks;
        _statusProgressBar.Value = 0;
        if (!_statusStrip.Items.Contains(_statusProgressBar))
            _statusStrip.Items.Insert(1, _statusProgressBar); // right after _statusLabel, left of the count labels
        UpdateStatusLabelWidth();
        _taskbarProgress?.SetProgress(0, 1);
    }

    private void EndRunProgress()
    {
        _statusStrip.Items.Remove(_statusProgressBar);
        _statusProgressBar.Style = ProgressBarStyle.Blocks;
        _statusProgressBar.Value = 0;
        UpdateStatusLabelWidth();
        _taskbarProgress?.Clear();
    }

    private void UpdateRunProgress(int completed, int total)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateRunProgress(completed, total));
            return;
        }

        _statusProgressBar.Maximum = Math.Max(total, 1);
        _statusProgressBar.Value = Math.Clamp(completed, 0, _statusProgressBar.Maximum);

        // Composing sheets is the measurable, trackable part; writing the output
        // file(s) afterward has no per-step granularity and can still take a few
        // seconds of its own, so switch to an indeterminate indicator for that tail
        // instead of leaving the bar sitting at a misleadingly "finished" 100% while
        // real work is still happening.
        if (completed >= total)
        {
            _statusProgressBar.Style = ProgressBarStyle.Marquee;
            _taskbarProgress?.SetIndeterminate();
            SetStatus("Generating booklet... writing output file(s)");
        }
        else
        {
            _taskbarProgress?.SetProgress(completed, total);
            SetStatus($"Generating booklet... sheet {completed} of {total}");
        }
    }

    private async void OnRunClick(object? sender, EventArgs e)
    {
        CliOptions options;
        try
        {
            options = BuildOptions();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid options", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _logBox.Clear();
        _previewTabs.SelectedTab = _logTab; // so progress is visible without having to go look for it
        _runRow.BeginRun();
        BeginRunProgress();
        SetStatus("Generating booklet...");

        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var writer = new TextBoxWriter(text => AppendLog(text));
        Console.SetOut(writer);
        Console.SetError(writer);

        _runCts = new CancellationTokenSource();

        bool succeeded = false;
        try
        {
            await Task.Run(() => new BookletGenerator(options).Run(UpdateRunProgress, _runCts.Token));
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            AppendLog("\r\nCancelled.\r\n");
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog($"\r\nError: {ex.Message}\r\n");
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            _runRow.EndRun(succeeded);
            EndRunProgress();
            _runCts.Dispose();
            _runCts = null;
        }

        if (succeeded)
        {
            SetStatus($"Done — output written to {options.OutputDir}");
            OpenOutputFolder();
        }
    }

    private async Task RunPreviewAsync(bool silent = false)
    {
        // Settings can keep changing while a render is in flight (PDFium serializes
        // internally anyway); rather than stacking up overlapping renders, just
        // remember that another one is wanted and run it once this one finishes.
        if (_previewRenderInProgress)
        {
            _previewRefreshPending = true;
            return;
        }

        CliOptions options;
        try
        {
            options = BuildOptions(forPreview: true);
        }
        catch (Exception ex)
        {
            if (silent)
                _previewPanel.SetStatus($"Preview paused: {ex.Message}");
            else
                MessageBox.Show(this, ex.Message, "Invalid options", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool front = _previewPanel.ShowFront;
        int sheetIndex = _previewPanel.SheetNumber - 1;
        var generator = new BookletGenerator(options);

        _previewRenderInProgress = true;
        _previewPanel.SetSideEnabled(false);
        _previewPanel.SetStatus("Rendering preview...");
        SetStatus("Rendering preview...");

        try
        {
            // A cheap lookup (just the source PDF's page count - no rasterization) to
            // know the valid sheet range and physical sheet size, so we can (a) clamp
            // the sheet selector and (b) work out a DPI that's "enough" for how big the
            // preview will actually be shown, rather than always using the real output
            // DPI (which can be far higher than any screen needs, e.g. 1200 for print).
            PreviewInfo? info = null;
            try
            {
                info = await Task.Run(generator.GetPreviewInfo);
            }
            catch
            {
                // Ignore - the accurate render attempt below will surface the real error.
            }

            int? fastDpi = null;
            if (info is not null)
            {
                // Keep the spinner's own range in sync with the real sheet count, so
                // typing or scrolling past it is simply not possible instead of being
                // allowed and then visibly snapped back after the fact.
                _previewPanel.SetSheetRangeQuietly(info.TotalSheets);
                sheetIndex = _previewPanel.SheetNumber - 1; // reflects any auto-clamp from the Maximum change above

                _pagesLabel.Text = $"Pages: {info.TotalPages}";
                _signaturesLabel.Text = $"Signatures: {info.SignatureCount}";
                _sheetsLabel.Text = $"Sheets: {info.TotalSheets}";
                UpdateStatusLabelWidth();

                double sheetWidthIn = info.SheetWidthPt / 72.0;
                int targetPx = Math.Max(_previewPanel.ViewportWidth, 300);
                int computed = (int)Math.Round(targetPx / Math.Max(sheetWidthIn, 0.1));
                computed = Math.Clamp(computed, 50, 150);
                if (computed <= options.Dpi - 30) // only worth a separate fast pass if it's meaningfully cheaper
                    fastDpi = computed;
            }

            if (fastDpi.HasValue)
            {
                try
                {
                    byte[] fastPng = await Task.Run(() => generator.RenderPreviewSheet(front, sheetIndex, fastDpi));
                    _previewPanel.ShowImage(fastPng);
                    _previewPanel.SetStatus("Refining preview...");
                }
                catch
                {
                    // Ignore - fall through to the accurate pass, which reports real errors.
                }
            }

            byte[] png = await Task.Run(() => generator.RenderPreviewSheet(front, sheetIndex));
            Size size = _previewPanel.ShowImage(png);

            string totalText = info is not null ? $" of {info.TotalSheets}" : "";
            _previewPanel.SetStatus($"Sheet {sheetIndex + 1}{totalText}, {(front ? "front" : "back")} — {size.Width}x{size.Height}px");
            SetIdleStatus();
        }
        catch (Exception ex)
        {
            _previewPanel.SetStatus(silent ? $"Preview failed: {ex.Message}" : "Preview failed.");
            SetStatus("Preview failed.");
            if (!silent)
                MessageBox.Show(this, ex.Message, "Preview failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _previewPanel.SetSideEnabled(true);
            _previewRenderInProgress = false;
        }

        if (_previewRefreshPending)
        {
            _previewRefreshPending = false;
            await RunPreviewAsync(silent: true);
        }
    }

    private void AppendLog(string text)
    {
        if (_logBox.InvokeRequired)
            _logBox.BeginInvoke(() => _logBox.AppendText(text));
        else
            _logBox.AppendText(text);
    }

    private CliOptions BuildOptions(bool forPreview = false)
    {
        string input = _inputOutputPanel.InputPath;
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Please choose an input PDF.");
        if (!File.Exists(input))
            throw new ArgumentException($"Input file not found: {input}");

        string output = _inputOutputPanel.OutputDir;
        if (!forPreview && string.IsNullOrEmpty(output))
            throw new ArgumentException("Please choose an output folder.");

        int signatureSize = _signaturePagesPanel.SignatureSize;
        if (signatureSize <= 0 || signatureSize % 4 != 0)
            throw new ArgumentException("Signature size must be a positive multiple of 4.");

        OutputMode mode = OutputMode.None;
        if (_outputModePanel.FrontBack) mode |= OutputMode.FrontBack;
        if (_outputModePanel.Duplex) mode |= OutputMode.Duplex;
        if (_outputModePanel.Images) mode |= OutputMode.Images;
        if (mode == OutputMode.None)
        {
            if (!forPreview)
                throw new ArgumentException("Select at least one output mode.");
            mode = OutputMode.Images; // irrelevant for a preview - it never writes files
        }

        Color background = _sheetLayoutPanel.BackgroundColor;
        Color cutMarkColor = _cutMarksPanel.MarkColor;

        return new CliOptions
        {
            InputPath = input,
            OutputDir = string.IsNullOrEmpty(output) ? "output" : output,
            SignatureSize = signatureSize,
            Mode = mode,
            PageOrder = _signaturePagesPanel.PageOrder,
            SheetSize = _sheetLayoutPanel.SheetSizeToken,
            Dpi = _sheetLayoutPanel.Dpi,
            MarginPt = _sheetLayoutPanel.MarginPt,
            GapPt = _sheetLayoutPanel.GapPt,
            Background = new SKColor(background.R, background.G, background.B),
            ImageFormat = _outputModePanel.ImageFormat,
            JpegQuality = _outputModePanel.JpegQuality,
            ReverseBackOrder = _signaturePagesPanel.ReverseBackOrder,
            SkipPages = _signaturePagesPanel.SkipPages,
            SplitSignatures = _signaturePagesPanel.SplitSignatures,
            AllowUpscale = _sheetLayoutPanel.AllowUpscale,
            CutMarks = _cutMarksPanel.CutMarksEnabled,
            CutMarkGapPt = _cutMarksPanel.GapPt,
            CutMarkLengthPt = _cutMarksPanel.LengthPt,
            CutMarkStrokePt = _cutMarksPanel.StrokePt,
            CutMarkColor = new SKColor(cutMarkColor.R, cutMarkColor.G, cutMarkColor.B),
        };
    }

    private sealed class TextBoxWriter(Action<string> append) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) => append(value.ToString());
        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
                append(value);
        }
    }
}
