using System;

namespace Cadmus.Export;

/// <summary>
/// Options for <see cref="CadmusMongoItemDumper"/>.
/// </summary>
public class CadmusMongoItemDumperOptions
{
    /// <summary>
    /// The MongoDB connection string template, having a <c>{0}</c> placeholder
    /// for the database name.
    /// </summary>
    public required string ConnectionString { get; set; } =
        "mongodb://localhost:27017/{0}";

    /// <summary>
    /// The name of the database to connect to.
    /// </summary>
    public required string DatabaseName { get; set; }

    /// <summary>
    /// The output directory where the exported items will be saved.
    /// </summary>
    public string OutputDirectory { get; set; } =
        Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDesktopDirectory);

    /// <summary>
    /// The maximum number of items to export. If not specified (0), all items
    /// will be exported in a single file.
    /// </summary>
    public int MaxItemsPerFile { get; set; }

    /// <summary>
    /// True to not include parts' date in the export filters when time-based
    /// parameters are specified. When this is true, the export will consider
    /// only the modified time of the items, ignoring that of each item's parts.
    /// </summary>
    public bool NoPartDate { get; set; }

    /// <summary>
    /// True to not include deleted items in the export.
    /// </summary>
    public bool NoDeleted { get; set; }

    /// <summary>
    /// True to not include parts in the export. When this is false, parts
    /// are added to each non-deleted exported item in a <c>_parts</c> array
    /// property.
    /// </summary>
    public bool NoParts { get; set; }

    /// <summary>
    /// True to indent the output JSON files.
    /// </summary>
    public bool Indented { get; set; }
 }
