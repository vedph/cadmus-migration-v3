using Cadmus.Core.Config;
using Cadmus.Core;
using Cadmus.Mongo;
using Fusi.Tools.Configuration;
using MongoDB.Driver;
using Proteus.Core.Regions;
using System;
using System.Threading.Tasks;

namespace Cadmus.Import.Proteus;

/// <summary>
/// Mongo-based Cadmus entry set exporter.
/// <para>Tag: <c>it.vedph.entry-set-exporter.cadmus.mongo</c>.</para>
/// </summary>
[Tag("it.vedph.entry-set-exporter.cadmus.mongo")]
public sealed class MongoEntrySetExporter : MongoConsumerBase, IEntrySetExporter,
    IConfigurable<MongoEntrySetExporterOptions>
{
    private MongoEntrySetExporterOptions? _options;
    private MongoCadmusRepository? _repository;

    /// <summary>
    /// Configures this exporter with the specified options.
    /// </summary>
    /// <param name="options">The options.</param>
    public void Configure(MongoEntrySetExporterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Opens the exporter output. Call this once from outside the pipeline,
    /// when you want to start exporting. This will initialize the database
    /// connection objects.
    /// </summary>
    /// <exception cref="InvalidOperationException">No connection string for
    /// MongoEntrySetExporter</exception>
    public Task OpenAsync()
    {
        if (string.IsNullOrEmpty(_options?.ConnectionString))
        {
            throw new InvalidOperationException("No connection string for "
                + nameof(MongoEntrySetExporter));
        }
        EnsureClientCreated(_options.ConnectionString);

        // we do not need to configure part types as we are just saving
        _repository = new(new StandardPartTypeProvider(new TagAttributeToTypeMap()),
            new StandardItemSortKeyBuilder());
        _repository.Configure(new MongoCadmusRepositoryOptions
        {
            ConnectionString = _options.ConnectionString
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Closes the exporter output. Call this once from outside the pipeline,
    /// when you want to end exporting. Here this method does nothing.
    /// </summary>
    public Task CloseAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Exports the specified entry set.
    /// </summary>
    /// <param name="entrySet">The entry set.</param>
    /// <param name="regionSet">The entry regions set.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="InvalidOperationException">
    /// No connection string for MongoEntrySetExporter or
    /// Mongo database DBNAME not open.
    /// </exception>
    public Task ExportAsync(EntrySet entrySet, EntryRegionSet regionSet)
    {
        ArgumentNullException.ThrowIfNull(entrySet);
        ArgumentNullException.ThrowIfNull(regionSet);

        if (string.IsNullOrEmpty(_options?.ConnectionString))
        {
            throw new InvalidOperationException("No connection string for "
                + nameof(MongoEntrySetExporter));
        }

        // get context
        CadmusEntrySetContext context = (CadmusEntrySetContext)entrySet.Context;
        if (context.Items.Count == 0) return Task.CompletedTask;

        foreach (IItem item in context.Items)
        {
            // write item without parts
            _repository!.AddItem(item, !_options.NoHistory);

            // write parts
            foreach (IPart part in item.Parts)
                _repository.AddPart(part, !_options.NoHistory);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Options for <see cref="MongoEntrySetExporter"/>.
/// </summary>
public class MongoEntrySetExporterOptions
{
    /// <summary>
    /// Gets or sets the connection string to the Cadmus database to write to.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether imported data should not be
    /// inserted in the history.
    /// </summary>
    public bool NoHistory { get; set; }
}
