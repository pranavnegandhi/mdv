using System.IO;
using System.Windows.Documents;
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
        return document;
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
