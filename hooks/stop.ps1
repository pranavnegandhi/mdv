# Claude Code "Stop" hook — mirror the latest assistant response into a per-session
# Markdown file that mdv watches and renders live.
#
# Reads the hook event JSON from stdin (session_id + transcript_path + cwd), pulls the
# text of the response that just completed out of the transcript, and APPENDS it
# (never overwrites) to %LOCALAPPDATA%\mdv\sessions\<project-slug>\<session_id>.md,
# bucketed per project so mdv can follow one project's sessions in isolation.
#
# This is a read-only side effect on the conversation: it always exits 0 and
# swallows every error, so it can never block or interrupt the session.
#
# Written to run under Windows PowerShell 5.1 (shell: "powershell") as well as pwsh.

$ErrorActionPreference = 'Stop'

try {
    $payload = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($payload)) { exit 0 }

    $hook = $payload | ConvertFrom-Json
    $sessionId = $hook.session_id
    $transcriptPath = $hook.transcript_path
    if ([string]::IsNullOrWhiteSpace($sessionId) -or [string]::IsNullOrWhiteSpace($transcriptPath)) { exit 0 }
    if (-not (Test-Path -LiteralPath $transcriptPath)) { exit 0 }

    # Read the transcript with shared access — Claude may still hold it open for writing.
    $fs = [System.IO.File]::Open(
        $transcriptPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object System.IO.StreamReader($fs)
        $raw = $reader.ReadToEnd()
        $reader.Dispose()
    }
    finally {
        $fs.Dispose()
    }

    # The transcript is JSONL: one JSON object per line. Each line has a top-level
    # `type` ("user"/"assistant"/...) and carries the API message under `.message`.
    # Parse line by line, skipping anything malformed.
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($line in ($raw -split "`n")) {
        $t = $line.Trim()
        if ($t.Length -eq 0) { continue }
        try { $entries.Add(($t | ConvertFrom-Json)) } catch { }
    }
    if ($entries.Count -eq 0) { exit 0 }

    # A "genuine" user prompt is a user entry whose content is real text (a string,
    # or an array containing a text block) — as opposed to a tool_result that Claude
    # records as a user-role entry mid-turn. The latest response turn is every
    # assistant entry that comes after the last genuine user prompt.
    function Test-UserPrompt($e) {
        if ($e.type -ne 'user') { return $false }
        $c = $e.message.content
        if ($null -eq $c) { return $false }
        if ($c -is [string]) { return $true }
        foreach ($b in $c) { if ($b.type -eq 'text') { return $true } }
        return $false
    }

    $lastUser = -1
    for ($i = 0; $i -lt $entries.Count; $i++) {
        if (Test-UserPrompt $entries[$i]) { $lastUser = $i }
    }

    # Collect the text blocks of every assistant entry after that prompt, in order.
    # Skip thinking / tool_use blocks — only the visible response text is mirrored.
    $parts = New-Object System.Collections.Generic.List[string]
    for ($i = $lastUser + 1; $i -lt $entries.Count; $i++) {
        $e = $entries[$i]
        if ($e.type -ne 'assistant') { continue }
        $c = $e.message.content
        if ($null -eq $c) { continue }
        if ($c -is [string]) {
            if ($c.Trim().Length -gt 0) { $parts.Add($c) }
            continue
        }
        foreach ($b in $c) {
            if ($b.type -eq 'text' -and $b.text -and $b.text.Trim().Length -gt 0) {
                $parts.Add([string]$b.text)
            }
        }
    }

    $text = ($parts -join "`n`n").Trim()
    if ($text.Length -eq 0) { exit 0 }

    # Compose the message: response text, a blank line, then an italic locale-aware
    # timestamp at the bottom (chat-message style). ToShortTimeString() respects the
    # machine's culture (e.g. "11:23 AM" en-US, "23:54" 24-hour locales).
    $time = (Get-Date).ToShortTimeString()
    $chunk = "$text`n`n_${time}_"

    # Sessions are bucketed per project under %LOCALAPPDATA%\mdv\sessions\<slug>, where
    # <slug> is derived from the session's working directory. The slug rule MUST stay in
    # lockstep with mdv's ClaudeSessionWatcher.ProjectDirectory: full path, trailing
    # separators trimmed, lower-cased, with ':' '\' '/' all replaced by '-'
    # (e.g. "D:\projects\mdv" -> "d--projects-mdv").
    $sessionsRoot = Join-Path $env:LOCALAPPDATA 'mdv\sessions'
    $cwd = $hook.cwd
    if ([string]::IsNullOrWhiteSpace($cwd)) {
        $slug = '_no-project'
    }
    else {
        $full = [System.IO.Path]::GetFullPath($cwd).TrimEnd('\', '/').ToLowerInvariant()
        $slug = ($full -replace '[:\\/]', '-')
    }
    $dir = Join-Path $sessionsRoot $slug
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $md = Join-Path $dir "$sessionId.md"

    # Every message except the first in a file gets a true horizontal rule above it.
    # The blank line before '---' is required: "text\n---" renders the text as an H2
    # setext heading, whereas "text\n\n---\n\n" renders a proper thematic break.
    $hasContent = (Test-Path -LiteralPath $md) -and ((Get-Item -LiteralPath $md).Length -gt 0)
    if ($hasContent) { $chunk = "`n---`n`n$chunk" }
    $chunk = "$chunk`n"

    # Append as UTF-8 without a BOM so repeated appends never inject a BOM mid-file.
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::AppendAllText($md, $chunk, $utf8)
}
catch {
    # A mirroring hook must never disrupt the session — swallow everything.
}

exit 0
