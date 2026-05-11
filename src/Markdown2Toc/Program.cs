using System.CommandLine;

var inOpt = new Option<string>("--input-file")
{
    Description = "Input markdown file(s) path",
    Required = true
};
var outOpt = new Option<string>("--output-file")
{
    Description = "Output TOC file or directory path",
    DefaultValueFactory = _ => "./"
};
var tocOpt = new Option<string?>("--toc-file")
{
    Description = "TOC file to merge"
};

var rootCommand = new RootCommand("Simple tool to generate table-of-content yaml files from Markdown files")
{
    inOpt,
    outOpt,
    tocOpt
};

rootCommand.SetAction(async (parseResult, _) =>
{
    await Md2TocGenerator.Generate(
        parseResult.GetValue(inOpt)!,
        parseResult.GetValue(outOpt)!,
        parseResult.GetValue(tocOpt) ?? string.Empty);
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();
