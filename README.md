# ![mdv](docs/mdv.png) mdv

**A minimal Markdown viewer for Windows that reads along with your AI agent.**

`mdv` renders Markdown the way it should look — serif headings, a clean sans-serif
body, properly typeset code and tables — with no editor chrome, no preview pane, and
nothing to configure. Point it at a file and read.

Its headline trick: in **follow mode** it mirrors a live [Claude Code](https://claude.com/claude-code)
session, rendering each response the moment the agent finishes it. You get a calm,
readable, full-window transcript of the conversation on a second monitor while you
keep working in the terminal. *This very README was written with `mdv --follow`
displaying the conversation as it happened.*

It works just as well as a plain, fast, general-purpose Markdown reader.

---

## Features

### Document viewer

- Renders a substantial subset of CommonMark (via [Markdig](https://github.com/xoofx/markdig)):
  headings, emphasis, lists, task lists, blockquotes, tables, code blocks and links.
- Hand-tuned typography — serif headings (Cambria), sans-serif body (Segoe UI),
  fixed-width code — chosen for legibility and consistent rendering.
- Document outline to jump straight to any section.
- Find-in-document (`Ctrl+F`) with next/previous navigation.
- Status bar with file path, size and word count — click any field to copy it.
- Recent-files list, reload-on-demand (`F5`), and a distraction-free full-screen mode (`F11`).
- Links open in your default browser.

### Claude Code integration

- **Follow mode** mirrors a project's Claude Code session and renders each response live.
- New responses append below the last; the view sticks to the bottom as they arrive,
  but holds your place the moment you scroll up to read back.
- Responses are tagged with chat-style timestamps.
- Sessions are bucketed **per project**, so each window follows one project in isolation.

---

## Follow a Claude Code session

Follow mode is a small, self-contained pipeline:

```
Claude Code  ──Stop hook──▶  %LOCALAPPDATA%\mdv\sessions\<project-slug>\<session-id>.md
                                              │
                                   FileSystemWatcher (debounced)
                                              │
                                              ▼
                                  mdv --follow  ──▶  live render
```

1. A Claude Code **`Stop` hook** fires when each response completes. It pulls the
   visible response text out of the session transcript and *appends* it to a
   per-project Markdown file (thinking and tool calls are skipped).
2. `mdv --follow` watches that project's session folder and reloads the newest file
   as it grows — debounced so a burst of writes redraws once.

### 1. Install the `Stop` hook

Save the hook script (`stop.ps1`) to `%USERPROFILE%\.claude\hooks\stop.ps1`, then
register it in your Claude Code settings (`%USERPROFILE%\.claude\settings.json`):

```json
{
  "hooks": {
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "& \"$env:USERPROFILE\\.claude\\hooks\\stop.ps1\"",
            "shell": "powershell",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

The hook is a read-only side effect on the conversation: it always exits `0` and
swallows every error, so it can never block or interrupt a session.

> **Note:** Claude Code loads hooks at session startup. After adding the hook, start
> a new `claude` session for it to take effect.

### 2. Launch in follow mode

```powershell
# Follow the session for a specific project
mdv --follow D:\projects\mdv

# Or follow the current working directory
mdv --follow
```

You can also toggle following at any time from the running window with
**View → Follow Claude Session** (`Ctrl+L`).

While following, the title bar shows the project being mirrored and a `● following`
indicator (with `(waiting…)` until the first response arrives).

---

## Getting started

### Requirements

- Windows
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download)

### Build from source

```powershell
git clone https://github.com/pranavnegandhi/mdv.git
cd mdv
dotnet build src/mdv/mdv.csproj -c Release
```

### Run

```powershell
# Open a file
mdv sample.md

# Open from the viewer with File → Open (Ctrl+O)
mdv
```

---

## Keyboard shortcuts

| Shortcut | Action                          |
|----------|---------------------------------|
| `Ctrl+O` | Open a file                     |
| `Ctrl+W` | Close the current document      |
| `Ctrl+F` | Find in document                |
| `F5`     | Reload the current document     |
| `Ctrl+L` | Toggle Follow Claude Session    |
| `F11`    | Toggle distraction-free mode    |
| `Ctrl+Q` | Quit                            |

---

## Built with

- [.NET 10](https://dotnet.microsoft.com/) / WPF
- [Markdig.Wpf](https://github.com/xoofx/markdig) — Markdown parsing and `FlowDocument` rendering

## License

[MIT](LICENSE) © 2026 Pranav Negandhi
