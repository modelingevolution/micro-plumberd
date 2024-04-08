using System;
using System.Collections;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;


class Program
{
    static async Task<int> Main(string[] args)
    {
        var inArg = new Argument<string>("--input-file", description: "Input markdown file(s) path");
        var outArg = new Argument<string>("--output-file", getDefaultValue: () => "./", description: "Output TOC file or directory path");
        var toc = new Option<string>("--toc-file", description: "TOC file to merge");
        var rootCommand = new RootCommand();
        rootCommand.Description = "Simple tool to generate table-of-content yaml files from Markdown files";
        rootCommand.AddArgument(inArg);
        rootCommand.AddArgument(outArg);
        rootCommand.AddOption(toc);
        rootCommand.SetHandler(Md2TocGenerator.Generate, inArg, outArg, toc);

        // Parse the incoming args and invoke the handler
        return await rootCommand.InvokeAsync(args);
    }

    
}
