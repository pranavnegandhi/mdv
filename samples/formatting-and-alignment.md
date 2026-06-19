# Formatting & alignment

What Markdig.Wpf actually renders (via `UseSupportedExtensions()`), and the one form of
alignment it supports. Anything not listed here (image width/height attributes, paragraph
`text-align`, HTML) is **not** honored — it is ignored or shown as literal text.

## Alignment — via table columns (the only supported alignment)

Markdown has no paragraph-alignment syntax, and Markdig.Wpf ignores generic attributes.
The one place alignment works is **pipe-table column alignment**, which maps to the cell's
`TextAlignment`. Note the marker rows: `:---` left, `:--:` center, `--:` right.

| Left (`:---`) | Center (`:--:`) | Right (`--:`) |
|:--------------|:---------------:|--------------:|
| apple         | apple           | apple         |
| a longer cell | a longer cell   | a longer cell |
| 1             | 22              | 333           |

### Aligning an image with a table cell

Because cell content honors `TextAlignment`, dropping an image into a centered or
right-aligned column effectively aligns the image — the only way to do so in mdv today.

| Left-aligned | Centered | Right-aligned |
|:--|:-:|--:|
| ![left](assets/square-thumbnail-175x175.jpg) | ![center](assets/square-thumbnail-175x175.jpg) | ![right](assets/square-thumbnail-175x175.jpg) |

## Emphasis (EmphasisExtras)

- **bold** and *italic* and ***bold italic***
- ~~strikethrough~~
- H~2~O — subscript, and E = mc^2^ — superscript
- ==marked== (highlight) and ++inserted++ (underline-ish)
- `inline code` in a monospace face

## Headings

# H1
## H2
### H3
#### H4

## Lists

1. Ordered one
2. Ordered two
   - nested unordered
   - nested unordered

- [x] Completed task (read-only checkbox)
- [ ] Pending task

## Blockquote

> Rendered as a gray, left-bordered section.
>
> Second paragraph inside the quote.

## Fenced code block

```csharp
public static FlowDocument Load(string path)
{
    // monospace, gray background
    return Markdown.ToFlowDocument(File.ReadAllText(path), Pipeline);
}
```

## Thematic break

Text above the rule.

---

Text below the rule.

## Autolink

Bare URLs are linkified automatically: https://github.com/Kryptos-FR/markdig.wpf
