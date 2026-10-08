using Bookletter.Cli;
using Bookletter.Imposition;
using Bookletter.Rendering;

namespace Bookletter.Gui;

/// <summary>
/// A panel showing, for the current settings, every sheet in the document as a small
/// schematic card (front/back page-number pairs, grouped and tinted by signature).
/// Purely derived from <see cref="SignatureCalculator"/> - no PDF content is
/// rendered, so it stays instant even for long documents.
/// </summary>
public sealed class SignatureMapPanel : UserControl
{
    private static readonly Color[] SignaturePalette =
    [
        Color.FromArgb(255, 244, 230),
        Color.FromArgb(230, 244, 255),
        Color.FromArgb(235, 255, 230),
        Color.FromArgb(250, 235, 250),
        Color.FromArgb(255, 250, 220),
        Color.FromArgb(225, 250, 250),
    ];

    private readonly Func<CliOptions> _buildOptions;
    private readonly Label _summaryLabel = new() { AutoSize = true, Margin = new Padding(8, 8, 8, 4) };

    // Each signature (header + its sheet cards) is one self-contained box with a
    // fixed, non-wrapping internal layout; this single flat panel just decides how
    // many whole boxes fit per row. One flow item per signature (not per card) keeps
    // this cheap even for documents with hundreds of signatures.
    private readonly FlowLayoutPanel _listPanel = new()
    {
        Dock = DockStyle.Top,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink
    };
    private bool _refreshInProgress;
    private bool _refreshPending;

    public SignatureMapPanel(Func<CliOptions> buildOptions)
    {
        _buildOptions = buildOptions;
        Dock = DockStyle.Fill;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
        var refreshBtn = new Button { Text = "Refresh", AutoSize = true };
        refreshBtn.Click += async (_, _) => await RefreshMapAsync();
        toolbar.Controls.Add(refreshBtn);

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        scrollHost.Controls.Add(_listPanel);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_summaryLabel, 0, 1);
        root.Controls.Add(scrollHost, 0, 2);
        Controls.Add(root);

        _ = RefreshMapAsync();
    }

    /// <summary>
    /// Recomputes the map for the current settings. Reading the source PDF's page
    /// count happens on a background thread - for a very large PDF that can take a
    /// moment, and this is wired into the same live auto-refresh as the image
    /// preview, so it must never block the UI thread.
    /// </summary>
    public async Task RefreshMapAsync()
    {
        if (_refreshInProgress)
        {
            _refreshPending = true;
            return;
        }

        _refreshInProgress = true;
        try
        {
            CliOptions options;
            try
            {
                options = _buildOptions();
            }
            catch (Exception ex)
            {
                ShowMessage($"Fix the settings above first: {ex.Message}");
                return;
            }

            int sourcePageCount;
            try
            {
                string inputPath = options.InputPath;
                sourcePageCount = await Task.Run(() => new PdfRasterizer(inputPath).PageCount);
            }
            catch (Exception ex)
            {
                ShowMessage($"Could not read the input PDF: {ex.Message}");
                return;
            }

            IReadOnlyList<int> selectedPages;
            try
            {
                selectedPages = PageSelector.Parse(options.Pages, sourcePageCount);
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message);
                return;
            }

            var spreads = SignatureCalculator.Calculate(selectedPages.Count, options.SignatureSize, options.PageOrder);
            int numSignatures = (int)Math.Ceiling(selectedPages.Count / (double)options.SignatureSize);

            _listPanel.SuspendLayout();
            _listPanel.Controls.Clear();

            _summaryLabel.Text = !string.IsNullOrWhiteSpace(options.Pages)
                ? $"{selectedPages.Count} selected page(s) ({sourcePageCount} in source) — " +
                  $"{numSignatures} signature(s) of {options.SignatureSize}, {spreads.Count} sheet(s) total. Page numbers below are real source page numbers."
                : $"{selectedPages.Count} pages — {numSignatures} signature(s) of {options.SignatureSize}, {spreads.Count} sheet(s) total.";

            foreach (var group in spreads.GroupBy(s => s.SignatureIndex))
            {
                var box = BuildSignatureBox(group.Key, numSignatures, group, selectedPages);
                box.Margin = new Padding(0, 0, 10, 10);
                _listPanel.Controls.Add(box);
            }

            _listPanel.ResumeLayout();
        }
        finally
        {
            _refreshInProgress = false;
        }

        if (_refreshPending)
        {
            _refreshPending = false;
            await RefreshMapAsync();
        }
    }

    private void ShowMessage(string text)
    {
        _listPanel.SuspendLayout();
        _listPanel.Controls.Clear();
        _summaryLabel.Text = text;
        _listPanel.ResumeLayout();
    }

    /// <summary>
    /// One signature: a tinted box with a header and its sheet cards stacked in a
    /// single column - no wrapping inside the box, by design, so its own layout never
    /// needs to be redone once built. Only the outer <see cref="_listPanel"/> decides
    /// how many of these boxes sit side by side.
    /// </summary>
    private static Control BuildSignatureBox(int signatureIndex, int numSignatures, IEnumerable<SheetSpread> sheets, IReadOnlyList<int> selectedPages)
    {
        var tint = SignaturePalette[signatureIndex % SignaturePalette.Length];
        var box = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = tint,
            Padding = new Padding(8)
            // No explicit Width here: AutoSizeMode.GrowAndShrink recomputes both
            // dimensions from content on every layout pass anyway, so a preset Width
            // would just be silently overwritten the first time that ran (confirmed -
            // it was, this box has never actually rendered at a fixed width).
        };

        box.Controls.Add(new Label
        {
            Text = $"Signature {signatureIndex + 1} of {numSignatures}",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        });

        foreach (var spread in sheets)
        {
            var card = BuildSheetCard(spread, selectedPages);
            card.Margin = new Padding(0, 0, 0, 6);
            box.Controls.Add(card);
        }

        // Freeze the box at its natural size, then turn AutoSize off. With AutoSize
        // left on, _listPanel's own wrap recalculation - which runs on every resize
        // tick - has to call GetPreferredSize() on every box to decide where lines
        // wrap, and because this box is itself a tree of AutoSize containers (cards,
        // side rows, labels), that cascades into remeasuring every label in every
        // card, every tick, even though none of it ever changes after the box is
        // built.
        //
        // This has to happen here, before the box is parented to _listPanel, not
        // after (tried that: freezing right after _listPanel.Controls.Add + a real
        // ResumeLayout() pass, which sounds like it should be the more "correct"
        // moment to measure from). GetPreferredSize() gave a wrong, too-small answer
        // immediately after being parented - clipping every box to ~100px regardless
        // of its real content - and only started returning the right answer once the
        // message loop had processed something else first. Measuring here, while the
        // box is still a self-contained, freshly-built tree with nothing else going
        // on, sidesteps that timing dependency entirely.
        box.Size = box.GetPreferredSize(Size.Empty);
        box.AutoSize = false;

        return box;
    }

    private static Control BuildSheetCard(SheetSpread spread, IReadOnlyList<int> selectedPages)
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(6)
        };

        card.Controls.Add(new Label
        {
            Text = $"Sheet {spread.SheetIndexInSignature + 1}  (global {spread.GlobalSheetIndex + 1})",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        });

        card.Controls.Add(BuildSideRow("Front", spread.FrontLeftPage, spread.FrontRightPage, selectedPages));
        card.Controls.Add(BuildSideRow("Back", spread.BackLeftPage, spread.BackRightPage, selectedPages));

        return card;
    }

    // A plain 4-column TableLayoutPanel rather than a wrapping FlowLayoutPanel - it's
    // always exactly these 4 cells, so there's no wrap computation to redo on resize.
    private static Control BuildSideRow(string label, int? left, int? right, IReadOnlyList<int> selectedPages)
    {
        var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
        for (int i = 0; i < 4; i++)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        row.Controls.Add(new Label
        {
            Text = label + ":",
            Width = 44,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 4, 0)
        }, 0, 0);
        row.Controls.Add(PageBox(left, selectedPages), 1, 0);
        row.Controls.Add(new Label { Text = "|", Width = 14, Height = 32, TextAlign = ContentAlignment.MiddleCenter }, 2, 0);
        row.Controls.Add(PageBox(right, selectedPages), 3, 0);
        return row;
    }

    // "page" is the 1-based position within the selected-pages list (what
    // SignatureCalculator works in); look up the real 1-based source page number it
    // refers to, since that's what's actually useful to see here.
    private static Label PageBox(int? page, IReadOnlyList<int> selectedPages)
    {
        bool blank = !page.HasValue;
        return new Label
        {
            Text = blank ? "blank" : (selectedPages[page!.Value - 1] + 1).ToString(),
            Width = 56,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = blank ? Color.Gainsboro : Color.White,
            Font = new Font(FontFamily.GenericSansSerif, 10f, blank ? FontStyle.Italic : FontStyle.Bold)
        };
    }
}
