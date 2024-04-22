using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

class Md2TocGenerator
{
    public static async Task Generate(string input, string output, string toc)
    {
        List<TocEntryLeaf> entries = new List<TocEntryLeaf>();
        if (Directory.Exists(input))
        {
            foreach (var i in Directory.EnumerateFiles(input, "*.md", SearchOption.AllDirectories)) 
                entries.AddRange(await ParseMarkdownFile(i));
        }
        else
        {
            var tocEntries = await ParseMarkdownFile(input);
            entries.AddRange(tocEntries);
        }

        if (!string.IsNullOrWhiteSpace(toc))
        {
            var serializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            var toMerge = serializer.Deserialize<List<TocEntryNode>>(await File.ReadAllTextAsync(toc));

            entries.AddRange(toMerge.Select(x=>x.Reduce()));
        }

        
        await GenerateTocFile(entries, output);
    }

    static async Task<List<TocEntryLeaf>> ParseMarkdownFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var markdown = await File.ReadAllTextAsync(filePath);
        var document = Markdown.Parse(markdown);
        var tocEntries = new List<TocEntryNode>();
        var stack = new Stack<(TocEntryNode entry, int level)>();

        foreach (var node in document.Descendants<HeadingBlock>())
        {
            var level = node.Level;
            var title = node.Inline.FirstChild.ToString();
            var entry = new TocEntryNode
            {
                Name = title,
                Href = $"{fileName}#{GenerateSlug(title)}"
            };

            while (stack.Any() && stack.Peek().level >= level) stack.Pop();

            if (stack.Any())
                stack.Peek().entry.Items.Add(entry);
            else
                tocEntries.Add(entry);

            stack.Push((entry, level));
        }

        return tocEntries.Select(x=>x.Reduce()).ToList();

    }

    static string GenerateSlug(string title)
    {
        string slug = title.ToLower();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", ""); // Remove invalid characters
        slug = Regex.Replace(slug, @"\s+", " ").Trim(); // Convert multiple spaces into one space
        slug = slug.Substring(0, slug.Length <= 64 ? slug.Length : 64).Trim(); // Cut and trim
        slug = Regex.Replace(slug, @"\s", "-"); // Replace spaces with hyphens
        return slug;
    }

    static async Task  GenerateTocFile(List<TocEntryLeaf> tocEntries, string filePath)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(tocEntries);

        if (filePath.EndsWith("/"))
            filePath = Path.Combine(filePath, "toc.yml");

        await File.WriteAllTextAsync(filePath, yaml);
    }

    
}