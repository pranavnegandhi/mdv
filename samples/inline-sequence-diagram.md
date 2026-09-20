# The Architecture of `mdv`

`mdv` is a WPF Markdown viewer. Where `inline-class-diagram.md` shows the
static shape of its subsystems, this file shows them in motion: the actual
call sequences behind opening a file, rendering inline SVG and Mermaid
diagrams, auto-reloading on a file change, fetching the comic of the day,
and mirroring a live Claude Code session. Every sequence below is
transcribed from the current source under `src/mdv`.

## Opening a File

`App.OnStartup` hands a file path straight to `MainWindow.OpenFile`, which
first leaves follow mode (if it was active) so a queued watcher event can't
clobber the file the user just asked for. Loading always goes through
`MarkdownDocumentLoader`; once the `FlowDocument` comes back, the window
refreshes its outline, status bar, and recent-files list, then repoints
auto-reload at the newly opened file.

```mermaid
sequenceDiagram
    actor User
    participant App
    participant MainWindow
    participant MarkdownDocumentLoader

    User->>App: launch "mdv file.md"
    App->>MainWindow: new MainWindow()
    App->>MainWindow: OpenFile(path)
    MainWindow->>MainWindow: StopFollowing()
    MainWindow->>MarkdownDocumentLoader: Load(path)
    MarkdownDocumentLoader-->>MainWindow: FlowDocument
    MainWindow->>MainWindow: RefreshOutline(), UpdateStatusBar(), AddToRecentFiles()
    MainWindow->>MainWindow: StartAutoReload(path)
    MainWindow-->>User: renders document
```

## Inline SVG Rendering

Markdig's own raw-HTML rules can truncate a bare `<svg>` block, so
`SvgBlockParser` claims it at parse time instead, producing a whole
`SvgBlock`. At render time `SvgBlockRenderer` hands that block's markup to
`SvgRendering`, which drives SharpVectors to turn it into a `DrawingGroup`
and wraps the result as an `Image`. A parse failure never reaches the
document — it degrades to a placeholder instead.

```mermaid
sequenceDiagram
    participant Markdig
    participant SvgBlockParser
    participant SvgBlockRenderer
    participant SvgRendering
    participant SharpVectors

    Markdig->>SvgBlockParser: TryOpen() on a line starting with "<svg"
    SvgBlockParser-->>Markdig: SvgBlock
    Markdig->>SvgBlockRenderer: Write(renderer, SvgBlock)
    SvgBlockRenderer->>SvgRendering: Build(markup)
    SvgRendering->>SharpVectors: FileSvgReader.Read(markup)
    SharpVectors-->>SvgRendering: DrawingGroup
    SvgRendering-->>SvgBlockRenderer: BlockUIContainer with Image
    Note over SvgBlockRenderer: a parse failure is caught and swapped<br/>for SvgRendering.BuildPlaceholder instead
```

## Mermaid Diagram Rendering

`MermaidBlockRenderer` sits in front of Markdig.Wpf's own code-block
renderer and branches on the fence's language tag. A `mermaid` fence goes
through the Mermaider library to SVG, then `MermaidSvgTheming` flattens the
CSS custom properties Mermaider emits (SharpVectors can't parse them
as-is) before the result reaches the same `SvgRendering` path used by
inline SVG above. Any other language falls straight through to Markdig.Wpf's
default rendering, unmodified.

```mermaid
sequenceDiagram
    participant Markdig
    participant MermaidBlockRenderer
    participant MermaidRenderer
    participant MermaidSvgTheming
    participant SvgRendering

    Markdig->>MermaidBlockRenderer: Write(renderer, FencedCodeBlock)
    alt Info is "mermaid"
        MermaidBlockRenderer->>MermaidRenderer: RenderSvg(source)
        MermaidRenderer-->>MermaidBlockRenderer: svg with CSS custom properties
        MermaidBlockRenderer->>MermaidSvgTheming: ResolveCssVariables(svg)
        MermaidSvgTheming-->>MermaidBlockRenderer: flattened svg
        MermaidBlockRenderer->>SvgRendering: Build(flattened svg)
        SvgRendering-->>MermaidBlockRenderer: BlockUIContainer with Image
    else any other language
        MermaidBlockRenderer->>Markdig: WriteLeafRawLines(obj)
    end
    Note over MermaidBlockRenderer: a render failure falls back to<br/>SvgRendering.BuildPlaceholder instead
```

## Auto-Reload on File Change

File-system notifications land on a thread-pool thread and can burst while
a file is still being written, so `FileChangeWatcher` marshals each one
onto the UI thread and debounces them behind a 150ms timer. Only once
things go quiet does it report the picker's latest choice, which
`MainWindow` uses to reload the file in place without losing the reader's
scroll position.

```mermaid
sequenceDiagram
    participant FileSystemWatcher
    participant FileChangeWatcher
    participant MainWindow

    FileSystemWatcher-->>FileChangeWatcher: Changed (thread-pool thread)
    FileChangeWatcher->>FileChangeWatcher: BeginInvoke picker() on UI thread
    FileChangeWatcher->>FileChangeWatcher: restart 150ms debounce timer
    Note over FileChangeWatcher: further bursts keep resetting<br/>the timer while the file is written
    FileChangeWatcher->>MainWindow: Changed(path)
    MainWindow->>MainWindow: ReloadPreservingScroll(path)
    MainWindow->>MainWindow: OpenFile(path), restore scroll offset
```

## Comic of the Day

With no file argument, `MainWindow` asks `XkcdCache` for today's comic.
A cache hit returns immediately; a miss fetches the latest comic number,
runs it through the deterministic `XkcdComicSelector`, and downloads the
chosen comic and its image. Any failure along that path — offline, a bad
response, whatever — is swallowed, and the bundled `XkcdFallback` comic is
shown instead so the panel is never empty.

```mermaid
sequenceDiagram
    participant App
    participant MainWindow
    participant XkcdCache
    participant XkcdClient
    participant XkcdComicSelector
    participant XkcdFallback

    App->>MainWindow: ShowComicOfTheDay()
    MainWindow->>XkcdCache: GetForTodayAsync(today)
    alt cache hit
        XkcdCache-->>MainWindow: cached XkcdComic
    else cache miss, network available
        XkcdCache->>XkcdClient: GetLatestNumberAsync()
        XkcdClient-->>XkcdCache: latest comic number
        XkcdCache->>XkcdComicSelector: Select(today, latest)
        XkcdComicSelector-->>XkcdCache: comic id
        XkcdCache->>XkcdClient: GetComicAsync(id), DownloadImageAsync(url)
        XkcdClient-->>XkcdCache: XkcdComic and image bytes
        XkcdCache-->>MainWindow: XkcdComic
    else offline or request failed
        XkcdCache-->>MainWindow: null
        MainWindow->>XkcdFallback: Comic, LoadImage()
        XkcdFallback-->>MainWindow: embedded XkcdComic and image
    end
    MainWindow->>MainWindow: PopulateComicPanel(comic, image)
```

## Claude Code Follow Mode

`mdv --follow [project-path]` resolves the project's session folder through
`ClaudeSessions` and points a `FileChangeWatcher` at it, whose picker always
reports the newest `.md` file. If a session already exists, the view jumps
straight to it; otherwise the title shows a waiting state until the
companion `Stop` hook writes the first response, at which point the watcher
fires and the view follows along.

```mermaid
sequenceDiagram
    actor User
    participant App
    participant MainWindow
    participant ClaudeSessions
    participant FileChangeWatcher

    User->>App: mdv --follow [project-path]
    App->>MainWindow: EnableFollow(projectPath)
    MainWindow->>ClaudeSessions: ProjectDirectory(project)
    ClaudeSessions-->>MainWindow: sessionDir
    MainWindow->>FileChangeWatcher: new FileChangeWatcher(sessionDir, picker: Newest)
    MainWindow->>FileChangeWatcher: Start()
    FileChangeWatcher->>ClaudeSessions: Newest(sessionDir)
    ClaudeSessions-->>FileChangeWatcher: newest path or null
    FileChangeWatcher-->>MainWindow: newest path
    alt a session already exists
        MainWindow->>MainWindow: LoadFollowed(newest, forceBottom is true)
    else no session recorded yet
        MainWindow->>MainWindow: show a waiting title
    end
    Note over FileChangeWatcher: the Stop hook later writes<br/>a new response file
    FileChangeWatcher->>MainWindow: OnSessionChanged(path)
    MainWindow->>MainWindow: LoadFollowed(path, forceBottom is switching)
```
