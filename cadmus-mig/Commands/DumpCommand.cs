using Cadmus.Export;
using Cadmus.Migration.Cli.Services;
using Fusi.Tools;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver.Core.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cadmus.Migration.Cli.Commands;

internal sealed class DumpCommand : AsyncCommand<DumpCommandSettings>
{
    private static void ShowSettings(DumpCommandSettings settings)
    {
        AnsiConsole.MarkupLine("[green]DUMP[/]");
        AnsiConsole.WriteLine($"Database: {settings.DatabaseName}");
        AnsiConsole.WriteLine($"Output directory: {settings.OutputDirectory}");
        AnsiConsole.WriteLine($"Max items per file: {settings.MaxItemsPerFile}");
        AnsiConsole.WriteLine($"Incremental: {settings.IsIncremental}");
        AnsiConsole.WriteLine($"No part date: {settings.NoPartDate}");
        AnsiConsole.WriteLine($"No deleted: {settings.NoDeleted}");
        AnsiConsole.WriteLine($"No parts: {settings.NoParts}");
        AnsiConsole.WriteLine($"Indented: {settings.Indented}\n");
    }

    public override Task<int> ExecuteAsync(CommandContext context,
        DumpCommandSettings settings)
    {
        ShowSettings(settings);

        try
        {
            // get connection string
            string connectionString = string.Format(CultureInfo.InvariantCulture,
                ConfigurationService.Configuration!.GetConnectionString("Default")!,
                settings.DatabaseName);

            // create output directory if it does not exist
            if (!Directory.Exists(settings.OutputDirectory))
            {
                Directory.CreateDirectory(settings.OutputDirectory);
            }

            // create dumper
            CadmusMongoJsonDumper dumper = new(new CadmusJsonDumperOptions
            {
                ConnectionString = connectionString,
                DatabaseName = settings.DatabaseName,
                OutputDirectory = settings.OutputDirectory,
                MaxItemsPerFile = settings.MaxItemsPerFile,
                Indented = settings.Indented,
                IsIncremental = settings.IsIncremental,
                NoPartDate = settings.NoPartDate,
                NoDeleted = settings.NoDeleted,
                NoParts = settings.NoParts
            });

            // dump
            AnsiConsole.WriteLine("Dumping data...");
            dumper.Dump(
                new CadmusDumpFilter
                {
                    // TODO
                },
                CancellationToken.None,
                new Progress<ProgressReport>(p =>
                    AnsiConsole.MarkupLine($"  [yellow]{p.Count}[/]")));

            AnsiConsole.MarkupLine("[green]Done![/]");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return Task.FromResult(2);
        }
    }
}

public class DumpCommandSettings : CommandSettings
{
    /// <summary>
    /// Gets or sets the name of the database to read items from.
    /// </summary>
    [CommandArgument(0, "<DatabaseName>")]
    [Description("The name of the source Cadmus database.")]
    public required string DatabaseName { get; set; }

    [CommandOption("-i|--incremental")]
    [Description("Incremental dump, " +
        "including only items/parts changed in the timeframe.")]
    public bool IsIncremental { get; set; }

    [CommandOption("--no-part-date")]
    [Description("Do not consider parts' date " +
        "when filtering items by time-based parameters.")]
    public bool NoPartDate { get; set; }

    [CommandOption("--no-deleted")]
    [Description("Do not include deleted items.")]
    public bool NoDeleted { get; set; }

    [CommandOption("--no-parts")]
    [Description("Do not include _parts in exported items.")]
    public bool NoParts { get; set; }

    [CommandOption("--output-dir")]
    [Description("The output directory where dump files will be saved")]
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"));

    [CommandOption("--max-file-items")]
    [Description("The maximum number of items to export. If not specified (0), " +
        "all items will be exported in a single file.")]
    [DefaultValue(0)]
    public int MaxItemsPerFile { get; set; }

    [CommandOption("--indented")]
    [Description("If set, the output will be indented.")]
    public bool Indented { get; set; }
}
