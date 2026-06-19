using System.IO;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Wpf;

namespace mdv.Services;

/// <summary>
/// Reads a Markdown file from disk and converts it to a WPF <see cref="FlowDocument"/>
/// using Markdig.Wpf. Kept free of UI dependencies so it can be unit-tested in isolation.
/// Renders any Markdown verbatim — it is not coupled to any particular document layout.
/// </summary>
public static class MarkdownDocumentLoader
{
    // All supported Markdig.Wpf extensions: emphasis extras, pipe/grid tables,
    // task lists, auto-links, etc. Built once and reused.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Build();

    /// <summary>
    /// Reads <paramref name="path"/> and renders it to a <see cref="FlowDocument"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static FlowDocument Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Markdown file not found.", path);

        var text = ReadAllTextShared(path);
        var document = Markdig.Wpf.Markdown.ToFlowDocument(text, Pipeline);
        document.IsHyphenationEnabled = false;

        // Markdig.Wpf has no base-URI hook, so a relative image src (e.g. docs/mdv.png) is
        // left for WPF to resolve against the process's current working directory — blank
        // unless mdv happens to be launched from the document's folder. Re-anchor relative
        // sources to the document's own directory so images render regardless of CWD.
        ResolveImageSources(document, Path.GetDirectoryName(Path.GetFullPath(path))!);

        return document;
    }

    /// <summary>
    /// Replaces local-file Markdown images with ones loaded from an absolute path resolved
    /// against <paramref name="baseDirectory"/> (the document's directory).
    /// </summary>
    /// <remarks>
    /// Markdig.Wpf renders each image as a <see cref="Button"/> (inside an
    /// <see cref="InlineUIContainer"/>) whose templated <see cref="Image"/> binds a
    /// <see cref="BitmapImage"/> built from the raw src, with the src itself carried on
    /// <see cref="System.Windows.Controls.Primitives.ButtonBase.CommandParameter"/>. We read
    /// that src, resolve it to an absolute path, and swap in a plain decoded <see cref="Image"/>.
    /// mdv binds no command for image clicks, so dropping the Button loses no behaviour.
    /// Remote (http/https/data/ftp) images are left untouched for Markdig to fetch.
    /// </remarks>
    private static void ResolveImageSources(FlowDocument document, string baseDirectory)
    {
        // Materialise first: replacing a container's Child mutates the inline collections
        // that FindImageContainers walks, which would invalidate a live enumerator.
        foreach (var container in FindImageContainers(document.Blocks).ToList())
        {
            if (container.Child is not Button { CommandParameter: string src } || string.IsNullOrWhiteSpace(src))
                continue;

            // Leave network/data images to Markdig — only local files need re-anchoring.
            var isAbsolute = Uri.TryCreate(src, UriKind.Absolute, out var uri);
            if (isAbsolute && uri!.Scheme is "http" or "https" or "data" or "ftp")
                continue;

            // file:// → its local path; everything else (relative or a bare drive path) is
            // treated as a filesystem path: rooted as-is, otherwise relative to the document.
            var raw = isAbsolute && uri!.Scheme == "file" ? uri.LocalPath : src;
            var fullPath = Path.IsPathRooted(raw)
                ? Path.GetFullPath(raw)
                : Path.GetFullPath(Path.Combine(baseDirectory, raw));

            if (File.Exists(fullPath))
                container.Child = CreateImage(fullPath);
        }
    }

    /// <summary>
    /// Builds an <see cref="Image"/> from an absolute file path, capped at the picture's
    /// native size so it is never upscaled (mirroring Markdig.Wpf's image style).
    /// </summary>
    private static Image CreateImage(string filePath)
    {
        var bitmap = LoadBitmap(filePath);
        return new Image
        {
            Source = bitmap,
            MaxWidth = bitmap.PixelWidth,
            MaxHeight = bitmap.PixelHeight,
        };
    }

    /// <summary>
    /// Loads an image from an absolute file path, fully decoding it on load so the file
    /// handle is released immediately (a default <see cref="BitmapImage"/> keeps the file
    /// open). The result is frozen for cheap, thread-safe reuse.
    /// </summary>
    private static BitmapImage LoadBitmap(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // Markdig.Wpf hosts each Markdown image in an InlineUIContainer. WPF's LogicalTreeHelper
    // does not enumerate a FlowDocument's inline content, so the strongly-typed content model
    // is walked directly, covering every container block/inline Markdig can emit.

    private static IEnumerable<InlineUIContainer> FindImageContainers(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var c in FindImageContainers(paragraph.Inlines)) yield return c;
                    break;
                case Section section:
                    foreach (var c in FindImageContainers(section.Blocks)) yield return c;
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                        foreach (var c in FindImageContainers(item.Blocks)) yield return c;
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                            foreach (var cell in row.Cells)
                                foreach (var c in FindImageContainers(cell.Blocks)) yield return c;
                    break;
            }
        }
    }

    private static IEnumerable<InlineUIContainer> FindImageContainers(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer container:
                    yield return container;
                    break;
                case Span span: // Bold, Italic, Underline and Hyperlink all derive from Span.
                    foreach (var c in FindImageContainers(span.Inlines)) yield return c;
                    break;
                case Figure figure:
                    foreach (var c in FindImageContainers(figure.Blocks)) yield return c;
                    break;
                case Floater floater:
                    foreach (var c in FindImageContainers(floater.Blocks)) yield return c;
                    break;
            }
        }
    }

    /// <summary>
    /// Reads the whole file allowing concurrent writers (<see cref="FileShare.ReadWrite"/>).
    /// A live Claude session file is appended to by an external hook while mdv is
    /// reloading it; a plain <see cref="File.ReadAllText(string)"/> would intermittently
    /// throw a sharing violation against that writer.
    /// </summary>
    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
