using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using mdv.Services;
using Microsoft.Win32;

namespace mdv;

/// <summary>
/// Main application window: a menu plus a continuous, scrollable, read-only view of
/// the rendered Markdown document.
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedUICommand CloseCommand =
        new("Close", nameof(CloseCommand), typeof(MainWindow));

    public static readonly RoutedUICommand ExitCommand =
        new("Exit", nameof(ExitCommand), typeof(MainWindow));

    public static readonly RoutedUICommand ReloadCommand =
        new("Reload", nameof(ReloadCommand), typeof(MainWindow));

    public static readonly RoutedUICommand DistractionFreeCommand =
        new("Distraction Free", nameof(DistractionFreeCommand), typeof(MainWindow));

    public static readonly RoutedUICommand FollowClaudeCommand =
        new("Follow Claude Session", nameof(FollowClaudeCommand), typeof(MainWindow));

    public static readonly RoutedUICommand AboutCommand =
        new("About", nameof(AboutCommand), typeof(MainWindow));

    // Highlight painted on the current search match.
    private static readonly Brush MatchBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));

    // Fraction of the viewable width the reading column occupies in distraction-free mode.
    private const double ReadingWidthFraction = 0.35;

    private string? _currentPath;

    // Live "Follow Claude" mode: a watcher over one project's session directory, the
    // flag tracking whether we are currently mirroring, and the project's display name.
    private ClaudeSessionWatcher? _sessionWatcher;

    private bool _following;
    private string? _followLabel;

    private bool _distractionFree;
    private WindowStyle _savedWindowStyle;
    private WindowState _savedWindowState;
    private ResizeMode _savedResizeMode;

    // The MRU list (newest first) and the menu controls currently rendering it.
    private readonly List<string> _recentFiles = new();

    private readonly List<Control> _recentMenuItems = new();

    // Last width of the outline sidebar, remembered across collapses.
    private GridLength _outlineWidth = new(220);

    // Formatted status-bar values, retained so a click copies exactly what is shown.
    private string _sizeText = string.Empty;

    private string _wordsText = string.Empty;

    // Search state for the find bar. The index is rebuilt lazily per document.
    private DocumentSearch? _search;

    private IReadOnlyList<TextRange> _matches = Array.Empty<TextRange>();
    private int _matchIndex = -1;
    private TextRange? _highlighted;

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public MainWindow()
    {
        InitializeComponent();

        _recentFiles.AddRange(RecentFilesService.Load());
        RefreshRecentFilesMenu();

        // No document is open at startup, so the outline starts collapsed.
        SetOutlineVisible(false);
    }

    /// <summary>
    /// Loads, renders and displays the Markdown file at <paramref name="path"/>.
    /// Surfaces any failure as a message box rather than crashing.
    /// </summary>
    public bool OpenFile(string path) => OpenFile(path, addToRecent: true, postProcess: null);

    /// <summary>
    /// Core load path. <paramref name="addToRecent"/> is suppressed for live session
    /// reloads so the MRU is not churned, and <paramref name="postProcess"/> lets a
    /// caller transform the rendered document (e.g. chat-timestamp styling) without the
    /// loader knowing anything about the document's structure.
    /// </summary>
    private bool OpenFile(string path, bool addToRecent, Action<FlowDocument>? postProcess)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var document = MarkdownDocumentLoader.Load(full);
            postProcess?.Invoke(document);
            Viewer.Document = document;
            _currentPath = full;
            Title = $"{Path.GetFileName(full)} — mdv";
            ApplyReadingWidth();
            RefreshOutline();
            UpdateStatusBar();
            ResetSearch();
            if (addToRecent)
                AddToRecentFiles(full);
            return true;
        }
        catch (FileNotFoundException)
        {
            ShowError($"File not found:\n{full}");
        }
        catch (Exception ex)
        {
            ShowError($"Could not open the file:\n{full}\n\n{ex.Message}");
        }

        return false;
    }

    private void OnOpen(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Markdown file",
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
            OpenFile(dialog.FileName);
    }

    private void OnCloseFile(object sender, ExecutedRoutedEventArgs e)
    {
        Viewer.Document = null;
        _currentPath = null;
        Title = "mdv";
        RefreshOutline();
        UpdateStatusBar();
        CloseFindBar();
        ResetSearch();
    }

    private void OnCanCloseFile(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = Viewer?.Document is not null;
    }

    private void OnExit(object sender, ExecutedRoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _sessionWatcher?.Dispose();
        _sessionWatcher = null;
        base.OnClosed(e);
    }

    private void OnCanReload(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _currentPath is not null;
    }

    /// <summary>
    /// Re-reads the current file in place, preserving the scroll position. Because a
    /// reload renders essentially the same content, the saved pixel offset maps back
    /// to the same place; <see cref="ScrollViewer.ScrollToVerticalOffset"/> clamps it
    /// if the document got shorter.
    /// </summary>
    private void OnReload(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentPath is null)
            return;

        var scroller = FindScrollViewer(Viewer);
        var offset = scroller?.VerticalOffset ?? 0;

        if (!OpenFile(_currentPath))
            return;

        // Restore once the new document has been laid out and its extent is known.
        Dispatcher.BeginInvoke(
            () => FindScrollViewer(Viewer)?.ScrollToVerticalOffset(offset),
            DispatcherPriority.Loaded);
    }

    /// <summary>Finds the inner <see cref="ScrollViewer"/> within a control's template.</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found)
            return found;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var result = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (result is not null)
                return result;
        }

        return null;
    }

    /// <summary>Toggles a borderless, full-screen, centered reading mode.</summary>
    private void OnToggleDistractionFree(object sender, ExecutedRoutedEventArgs e) =>
        SetDistractionFree(!_distractionFree);

    private void SetDistractionFree(bool on)
    {
        if (on == _distractionFree)
            return;

        if (on)
        {
            // Remember the current chrome so we can restore it exactly.
            _savedWindowStyle = WindowStyle;
            _savedWindowState = WindowState;
            _savedResizeMode = ResizeMode;

            // Borderless + maximized fills the whole screen (covers the taskbar).
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            MainMenu.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowStyle = _savedWindowStyle;
            ResizeMode = _savedResizeMode;
            WindowState = _savedWindowState;
            MainMenu.Visibility = Visibility.Visible;
        }

        _distractionFree = on;
        DistractionFreeMenuItem.IsChecked = on;
        ApplyReadingWidth();
    }

    /// <summary>
    /// In distraction-free mode the document text is centered in the middle
    /// <see cref="ReadingWidthFraction"/> of the viewport via large side padding.
    /// In normal mode the padding falls back to the value from the document style.
    /// </summary>
    private void ApplyReadingWidth()
    {
        if (Viewer.Document is not FlowDocument doc)
            return;

        if (_distractionFree)
        {
            var width = Viewer.ActualWidth;
            var side = Math.Max(0, (width - width * ReadingWidthFraction) / 2);
            doc.PagePadding = new Thickness(side, 24, side, 24);
        }
        else
        {
            // Revert to the PagePadding defined in MarkdownStyles.xaml.
            doc.ClearValue(FlowDocument.PagePaddingProperty);
        }
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_distractionFree && e.WidthChanged)
            ApplyReadingWidth();
    }

    // ----- Follow Claude session ----------------------------------------------

    /// <summary>
    /// Enables live-follow mode programmatically (e.g. from the <c>--follow</c> launch flag).
    /// <paramref name="projectPath"/> selects which project's sessions to mirror; when null the
    /// current working directory is used.
    /// </summary>
    public void EnableFollow(string? projectPath = null) => SetFollowing(true, projectPath);

    private void OnToggleFollowClaude(object sender, ExecutedRoutedEventArgs e) =>
        SetFollowing(!_following, null);

    /// <summary>
    /// Turns live mirroring on or off. While on, mdv watches one project's session folder
    /// (<c>%LOCALAPPDATA%\mdv\sessions\&lt;slug&gt;</c>) and reloads the newest session file as
    /// each response is appended. The project is <paramref name="projectPath"/>, or the current
    /// working directory when that is null.
    /// </summary>
    private void SetFollowing(bool on, string? projectPath)
    {
        if (on == _following)
            return;

        if (on)
        {
            var project = string.IsNullOrWhiteSpace(projectPath)
                ? Environment.CurrentDirectory
                : projectPath;
            _followLabel = Path.GetFileName(project.TrimEnd('\\', '/'));

            _sessionWatcher = new ClaudeSessionWatcher(
                Dispatcher, ClaudeSessionWatcher.ProjectDirectory(project));
            _sessionWatcher.Changed += OnSessionChanged;

            var newest = _sessionWatcher.Start();
            _following = true;
            FollowClaudeMenuItem.IsChecked = true;

            // Jump to the foot of whatever already exists; new responses land below.
            // If no session has been recorded for this project yet, show a waiting state
            // until the first response arrives and the watcher fires.
            if (newest is not null)
                LoadFollowed(newest, forceBottom: true);
            else
                Title = $"Claude session [{_followLabel}] — mdv  ● following (waiting…)";
        }
        else
        {
            if (_sessionWatcher is not null)
            {
                _sessionWatcher.Changed -= OnSessionChanged;
                _sessionWatcher.Dispose();
                _sessionWatcher = null;
            }

            _following = false;
            _followLabel = null;
            FollowClaudeMenuItem.IsChecked = false;
        }
    }

    /// <summary>
    /// Handles a watcher notification: switch to a newer session file, or reload the
    /// current one in place when it has merely grown.
    /// </summary>
    private void OnSessionChanged(string path)
    {
        var switching = !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase);
        LoadFollowed(path, forceBottom: switching);
    }

    /// <summary>
    /// Loads a session file with chat-timestamp styling and sticky-bottom scrolling:
    /// if the user is already at the bottom (or we are switching files) the view jumps
    /// to the newest response; otherwise their scroll position is preserved so reading
    /// back through history is not disturbed.
    /// </summary>
    private void LoadFollowed(string path, bool forceBottom)
    {
        var scroller = FindScrollViewer(Viewer);
        var offset = scroller?.VerticalOffset ?? 0;

        // Within ~2px of the end counts as "at the bottom" (offsets are fractional).
        var wasAtBottom = scroller is null
                          || scroller.VerticalOffset >= scroller.ScrollableHeight - 2;

        if (!OpenFile(path, addToRecent: false, postProcess: ChatTimestampStyler.Apply))
            return;

        // A friendlier title than the GUID-named session file, tagged with the project.
        Title = $"Claude session [{_followLabel}] — mdv  ● following";

        // Restore once the new document has been laid out and its extent is known.
        Dispatcher.BeginInvoke(
            () =>
            {
                var sv = FindScrollViewer(Viewer);
                if (sv is null)
                    return;

                if (forceBottom || wasAtBottom)
                    sv.ScrollToBottom();
                else
                    sv.ScrollToVerticalOffset(offset);
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Rebuilds the outline from the current document and shows or hides the sidebar
    /// depending on whether the document has any headings.
    /// </summary>
    private void RefreshOutline()
    {
        var items = OutlineBuilder.Build(Viewer.Document as FlowDocument);
        OutlineList.ItemsSource = items;
        SetOutlineVisible(items.Count > 0);
    }

    /// <summary>
    /// Collapses or restores the outline column. Collapsing remembers the last width so
    /// a user-resized sidebar comes back the same size.
    /// </summary>
    private void SetOutlineVisible(bool visible)
    {
        if (visible)
        {
            OutlineColumn.Width = _outlineWidth;
            OutlineColumn.MinWidth = 120;
            OutlineList.Visibility = Visibility.Visible;
            OutlineSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            if (OutlineColumn.Width.IsAbsolute && OutlineColumn.Width.Value > 0)
                _outlineWidth = OutlineColumn.Width;
            OutlineColumn.MinWidth = 0;
            OutlineColumn.Width = new GridLength(0);
            OutlineList.Visibility = Visibility.Collapsed;
            OutlineSplitter.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Scrolls the document so the clicked heading is brought into view.</summary>
    private void OnOutlineItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (OutlineList.SelectedItem is OutlineItem item)
            item.Target.BringIntoView();

        // Clear selection so clicking the same heading again re-navigates.
        OutlineList.SelectedItem = null;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Escape dismisses the find bar first, then leaves distraction-free mode.
            if (FindBar.Visibility == Visibility.Visible)
            {
                CloseFindBar();
                e.Handled = true;
            }
            else if (_distractionFree)
            {
                SetDistractionFree(false);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && FindBar.Visibility == Visibility.Visible)
        {
            // While the find bar is open, Enter steps to the next match (Shift+Enter to
            // the previous). Handled here at the window level so it works regardless of
            // whether focus is in the find box or has drifted to the document.
            StepMatch(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : +1);
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    // ----- Status bar ---------------------------------------------------------

    /// <summary>
    /// Refreshes the bottom status bar from the current document, or hides it when no
    /// file is open. The file size and word count are formatted for the current locale;
    /// the path is middle-truncated to fit by <see cref="UpdatePathLabel"/>.
    /// </summary>
    private void UpdateStatusBar()
    {
        if (_currentPath is null)
        {
            StatusBar.Visibility = Visibility.Collapsed;
            _sizeText = _wordsText = string.Empty;
            PathLabel.Text = string.Empty;
            return;
        }

        var culture = CultureInfo.CurrentCulture;

        // File length in kilobytes. "#,##0.#" shows a decimal only when needed
        // (1.5 / 1,5) and groups thousands per the locale (2,048 / 2 048).
        var kilobytes = new FileInfo(_currentPath).Length / 1024.0;
        _sizeText = $"{kilobytes.ToString("#,##0.#", culture)} KB";

        // Word count from the rendered document text (excludes Markdown syntax).
        var words = Viewer.Document is FlowDocument doc
            ? CountWords(new TextRange(doc.ContentStart, doc.ContentEnd).Text)
            : 0;
        _wordsText = $"{words.ToString("#,##0", culture)} {(words == 1 ? "word" : "words")}";

        SizeLabel.Text = _sizeText;
        WordsLabel.Text = _wordsText;
        PathLabel.ToolTip = _currentPath;
        StatusBar.Visibility = Visibility.Visible;

        // Defer until the numeric labels have measured, so their widths are known when
        // the path's available space is computed.
        Dispatcher.BeginInvoke(UpdatePathLabel, DispatcherPriority.Loaded);
    }

    /// <summary>Counts whitespace-delimited runs (words) in <paramref name="text"/>.</summary>
    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Fits the full path into the space left of the numeric fields, dropping characters
    /// from the middle when it would overflow. A <see cref="FrameworkElement.MaxWidth"/>
    /// cap stops the label from pushing the layout wider than the bar (which would
    /// otherwise defeat the star column and let the text overflow off-screen).
    /// </summary>
    private void UpdatePathLabel()
    {
        if (_currentPath is null)
            return;

        // The bar's own width is reliable (it stretches to the window); the path's share
        // is what remains after the bar padding, the two numeric labels and their margins.
        var available = StatusBar.ActualWidth
                        - StatusBar.Padding.Left - StatusBar.Padding.Right
                        - SizeLabel.ActualWidth - SizeLabel.Margin.Left
                        - WordsLabel.ActualWidth - WordsLabel.Margin.Left;

        if (available <= 0)
        {
            // Not laid out yet; a later size pass will compute the real width.
            PathLabel.Text = _currentPath;
            return;
        }

        PathLabel.MaxWidth = available;
        PathLabel.Text = MiddleEllipsis.Truncate(_currentPath, available, MeasurePathText);
    }

    private void OnStatusBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            UpdatePathLabel();
    }

    /// <summary>Measures the rendered width of <paramref name="text"/> in the path label's font.</summary>
    private double MeasurePathText(string text)
    {
        var typeface = new Typeface(
            PathLabel.FontFamily, PathLabel.FontStyle, PathLabel.FontWeight, PathLabel.FontStretch);

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            PathLabel.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return formatted.Width;
    }

    private void OnCopyPath(object sender, MouseButtonEventArgs e) => CopyToClipboard(_currentPath);

    private void OnCopySize(object sender, MouseButtonEventArgs e) => CopyToClipboard(_sizeText);

    private void OnCopyWords(object sender, MouseButtonEventArgs e) => CopyToClipboard(_wordsText);

    /// <summary>Copies <paramref name="value"/> to the clipboard, ignoring transient lock failures.</summary>
    private static void CopyToClipboard(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        try
        {
            Clipboard.SetText(value);
        }
        catch (Exception)
        {
            // The clipboard is briefly owned by another process; a copy is not worth crashing over.
        }
    }

    // ----- Find ---------------------------------------------------------------

    private void OnCanFind(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Viewer?.Document is not null;

    /// <summary>Opens (or re-focuses) the find bar and selects any existing query text.</summary>
    private void OnFind(object sender, ExecutedRoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Visible;
        FindInput.Focus();
        FindInput.SelectAll();

        if (!string.IsNullOrEmpty(FindInput.Text))
            RunSearch();
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e) => RunSearch();

    private void OnFindNext(object sender, RoutedEventArgs e) => StepMatch(+1);

    private void OnFindPrevious(object sender, RoutedEventArgs e) => StepMatch(-1);

    private void OnFindClose(object sender, RoutedEventArgs e) => CloseFindBar();

    /// <summary>Recomputes matches for the current query and jumps to the first one.</summary>
    private void RunSearch()
    {
        ClearHighlight();

        var query = FindInput.Text;

        _search ??= Viewer.Document is FlowDocument doc ? new DocumentSearch(doc) : null;
        _matches = _search?.FindAll(query) ?? Array.Empty<TextRange>();

        if (_matches.Count == 0)
        {
            _matchIndex = -1;
            FindStatus.Text = string.IsNullOrEmpty(query) ? string.Empty : "No results";
            return;
        }

        _matchIndex = -1;
        StepMatch(+1);
    }

    /// <summary>Moves to the match <paramref name="direction"/> away (wrapping) and reveals it.</summary>
    private void StepMatch(int direction)
    {
        if (_matches.Count == 0)
            return;

        _matchIndex = ((_matchIndex + direction) % _matches.Count + _matches.Count) % _matches.Count;
        var match = _matches[_matchIndex];

        ClearHighlight();
        match.ApplyPropertyValue(TextElement.BackgroundProperty, MatchBrush);
        _highlighted = match;

        ScrollToMatch(match.Start);
        FindStatus.Text = $"{_matchIndex + 1} of {_matches.Count}";
    }

    /// <summary>
    /// Scrolls the viewer so the match at <paramref name="position"/> is visible.
    /// <see cref="FrameworkContentElement.BringIntoView"/> on an inline run is unreliable
    /// for off-screen content, so this scrolls the inner <see cref="ScrollViewer"/> directly
    /// using the match's on-screen rectangle (which is relative to the viewport).
    /// </summary>
    private void ScrollToMatch(TextPointer position)
    {
        var scroller = FindScrollViewer(Viewer);
        if (scroller is null)
        {
            (position.Paragraph as FrameworkContentElement)?.BringIntoView();
            return;
        }

        var rect = position.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
            return;

        // Only scroll when the match sits outside a comfortable band, so an already-visible
        // match doesn't jump. rect.Top/Bottom are viewport-relative (negative above, past
        // ViewportHeight below); add the current offset to convert to a document offset.
        const double margin = 48;
        if (rect.Top < margin || rect.Bottom > scroller.ViewportHeight - margin)
            scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + rect.Top - margin));
    }

    private void CloseFindBar()
    {
        ClearHighlight();
        FindBar.Visibility = Visibility.Collapsed;
        FindStatus.Text = string.Empty;
        _matches = Array.Empty<TextRange>();
        _matchIndex = -1;
        if (Viewer.Document is not null)
            Viewer.Focus();
    }

    /// <summary>Drops the cached index and matches; the next search rebuilds against the new document.</summary>
    private void ResetSearch()
    {
        _search = null;
        _matches = Array.Empty<TextRange>();
        _matchIndex = -1;
        _highlighted = null;

        if (FindBar.Visibility == Visibility.Visible)
            RunSearch();
    }

    /// <summary>Removes the current-match background, tolerating a pointer left over from a replaced document.</summary>
    private void ClearHighlight()
    {
        if (_highlighted is null)
            return;

        try
        {
            _highlighted.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        }
        catch (Exception)
        {
            // The range belonged to a document that has since been replaced; nothing to clear.
        }

        _highlighted = null;
    }

    private void OnAbout(object sender, ExecutedRoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? string.Empty : version.ToString(3);

        MessageBox.Show(
            this,
            $"mdv {versionText}\n\n" +
            "A view-only Markdown reader for Windows.",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnHyperlink(object sender, ExecutedRoutedEventArgs e)
    {
        var url = e.Parameter?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open the link:\n{url}\n\n{ex.Message}");
        }
    }

    /// <summary>Promotes <paramref name="path"/> to the top of the MRU list and persists it.</summary>
    private void AddToRecentFiles(string path)
    {
        _recentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentFiles.Insert(0, path);

        while (_recentFiles.Count > RecentFilesService.MaxItems)
            _recentFiles.RemoveAt(_recentFiles.Count - 1);

        RecentFilesService.Save(_recentFiles);
        RefreshRecentFilesMenu();
    }

    /// <summary>
    /// Rebuilds the numbered MRU menu items directly under the File menu, between
    /// <c>RecentSeparator</c> and the Exit item. The leading digit in each header acts
    /// as the access key, so pressing 1-5 with the File menu open opens that file.
    /// </summary>
    private void RefreshRecentFilesMenu()
    {
        foreach (var item in _recentMenuItems)
            FileMenu.Items.Remove(item);
        _recentMenuItems.Clear();

        if (_recentFiles.Count == 0)
            return;

        var insertAt = FileMenu.Items.IndexOf(RecentSeparator) + 1;

        for (var i = 0; i < _recentFiles.Count; i++)
        {
            var path = _recentFiles[i];
            var item = new MenuItem
            {
                // Double underscores escape literal '_' in the path so they are not
                // mistaken for access keys; the leading "_N " supplies the accelerator.
                Header = $"_{i + 1} {path.Replace("_", "__")}",
                Tag = path,
                ToolTip = path,
            };
            item.Click += OnRecentFileClick;

            FileMenu.Items.Insert(insertAt++, item);
            _recentMenuItems.Add(item);
        }

        var trailing = new Separator();
        FileMenu.Items.Insert(insertAt, trailing);
        _recentMenuItems.Add(trailing);
    }

    private void OnRecentFileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path })
            return;

        if (!File.Exists(path))
        {
            ShowError($"File no longer exists:\n{path}");
            _recentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            RecentFilesService.Save(_recentFiles);
            RefreshRecentFilesMenu();
            return;
        }

        OpenFile(path);
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "mdv", MessageBoxButton.OK, MessageBoxImage.Warning);
}
