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
        var inArg = new Argument<string>("--input-file");
        inArg.Description = "Input markdown file(s) path";
        var outArg = new Argument<string>("--output-file");
        outArg.DefaultValueFactory = (r) => "./";
        outArg.Description= "Output TOC file or directory path";
        var toc = new Option<string>("--toc-file");
        toc.Description="TOC file to merge";
        var rootCommand = new RootCommand();
        rootCommand.Description = "Simple tool to generate table-of-content yaml files from Markdown files";
        
        rootCommand.Add(inArg);
        rootCommand.Add(outArg);
        rootCommand.Add(toc);
        rootCommand.SetAction(p => Md2TocGenerator.Generate(p.GetValue(inArg), p.GetValue(outArg), p.GetValue(toc)));

        // Parse the incoming args and invoke the handler
        //return await rootCommand.InvokeAsync(args);
        throw new ArgumentException("WTF");
    }

    
}
