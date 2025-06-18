using Cadmus.Core.Storage;
using Cadmus.Mongo;
using Fusi.Tools;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Cadmus.Export;

/// <summary>
/// Cadmus MongoDB item dumper.
/// </summary>
/// <remarks>This is used to dump items data into one or more JSON files.
/// Items are filtered according to specified criteria, with the state of data
/// determined at a specific timeframe when requested.
/// <para>The source Cadmus database contains collections for items, parts,
/// history_items, history_parts. Items have among other properties <c>_id</c>
/// (a GUID), <c>timeCreated</c>, <c>timeModified</c>; parts have <c>_id</c>
/// (a GUID), <c>timeCreated</c>, <c>timeModified</c>, and an <c>itemId</c>
/// working like a foreign key to link that part to a specific item.</para>
/// <para>History collections are used to store copies of items and parts,
/// whenever they get saved in the database during editing. When this happens,
/// a copy of the item/part it is stored in the corresponding history
/// collection: the entry has its own <c>_id</c> (a GUID), and the GUID of its
/// source item/part in <c>referenceId</c>. Also, there is a <c>status</c></para>
/// numeric field with values 0=created, 1=updated, 2=deleted. When an item/part
/// is first created, a copy of it is stored in history with status=created;
/// then, on each successive update, a copy of it is stored in history with
/// status=updated. If it gets deleted, the item/part is removed from its
/// collection, but a copy of it before deletion is stored in the corresponding
/// history part, with status=deleted.
/// <para>To get a snapshot of all data at a given time frame, the dumper focuses
/// on history collections which contain all versions of items and parts with
/// their timestamps and status.</para>
/// <para>A more effective way of returning the data state at any timeframe is
/// focusing on history collections only. Item and part collections just hold
/// the latest active version of each data, while their history counterparts
/// contain all versions with their timestamp (<c>timeModified</c>). So, the
/// approach to get a snapshot of all data at a given time frame is:</para>
/// <list type="number">
/// <item>set items to filtered history items. This filters by any criteria
/// including min and max modified time which provide the time frame when
/// specified. Otherwise the time frame is the whole dataset.</item>
/// <item>group items by <c>referenceId</c>. This means grouping history
/// entries by item. Each group is the full history of the item within the
/// boundaries defined by filter.</item>
/// <item>for each group, select the latest entry. This is the item we want,
/// it corresponds to the item which was active within the given timeframe.
/// </item>
/// <item>for each selected item collect item parts and add them to a new
/// <c>_parts</c> array property representing the item. Also set its <c>_id</c>
/// equal to <c>_referenceId</c> and remove <c>_referenceId</c>, renaming
/// <c>status</c> into <c>_status</c>, including deleted.
/// </item>
/// </list>
/// <para>As for get item parts, it would work in a similar way for parts:
/// filter history parts (including the item ID filter for the item being
/// processed), group by <c>referenceId</c>, select the latest entry from
/// each group, and return the part with an adjusted schema.</para>
/// </remarks>
public sealed class CadmusMongoItemDumper : MongoConsumerBase
{
    private readonly CadmusMongoItemDumperOptions _options;

    /// <summary>
    /// Create a new instance of <see cref="CadmusMongoItemDumper"/>.
    /// </summary>
    /// <param name="options">Options.</param>
    /// <exception cref="ArgumentNullException">options</exception>
    public CadmusMongoItemDumper(CadmusMongoItemDumperOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Builds item filter without time-based constraints.
    /// </summary>
    /// <param name="builder">Filter builder.</param>
    /// <returns>Filter definition.</returns>
    private FilterDefinition<BsonDocument> BuildBaseItemFilter(
        FilterDefinitionBuilder<BsonDocument> builder)
    {
        if (_options.IsEmpty) return builder.Empty;

        List<FilterDefinition<BsonDocument>> filters = [];

        if (!string.IsNullOrEmpty(_options.Title))
        {
            filters.Add(builder.Regex("title",
                new BsonRegularExpression(_options.Title, "i")));
        }

        if (!string.IsNullOrEmpty(_options.Description))
        {
            filters.Add(builder.Regex("description",
                new BsonRegularExpression(_options.Description, "i")));
        }

        if (!string.IsNullOrEmpty(_options.FacetId))
        {
            filters.Add(builder.Eq("facetId", _options.FacetId));
        }

        if (!string.IsNullOrEmpty(_options.GroupId))
        {
            filters.Add(builder.Eq("groupId", _options.GroupId));
        }

        if (!string.IsNullOrEmpty(_options.UserId))
        {
            filters.Add(builder.Eq("userId", _options.UserId));
        }

        // flags filter with matching mode
        if (_options.Flags.HasValue)
        {
            switch (_options.FlagMatching)
            {
                case FlagMatching.BitsAllSet:
                    filters.Add(builder.BitsAllSet("flags",
                        _options.Flags.Value));
                    break;
                case FlagMatching.BitsAnySet:
                    filters.Add(builder.BitsAnySet("flags",
                        _options.Flags.Value));
                    break;
                case FlagMatching.BitsAllClear:
                    filters.Add(builder.BitsAllClear("flags",
                        _options.Flags.Value));
                    break;
            }
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Builds the filter for items to export getting all parameters from
    /// the option's filter, including time-based constraints.
    /// </summary>
    /// <param name="builder">Filter definition builder.</param>
    /// <returns>Filter.</returns>
    private FilterDefinition<BsonDocument> BuildItemFilter(
        FilterDefinitionBuilder<BsonDocument> builder)
    {
        // start with the base filter (non-time based constraints)
        FilterDefinition<BsonDocument> filter = BuildBaseItemFilter(builder);

        // if no filter or no time constraints, return the base filter
        if (_options.IsEmpty ||
            (!_options.MinModified.HasValue &&
             !_options.MaxModified.HasValue))
        {
            return filter;
        }

        // add time-based constraints
        List<FilterDefinition<BsonDocument>> filters = [];

        // start with the base filter if it's not empty
        if (filter != builder.Empty) filters.Add(filter);

        // add date range filter for items
        if (_options.MinModified.HasValue)
        {
            filters.Add(builder.Gte("timeModified",
                _options.MinModified.Value));
        }

        if (_options.MaxModified.HasValue)
        {
            filters.Add(builder.Lte("timeModified",
                _options.MaxModified.Value));
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Builds a filter for a part type key.
    /// </summary>
    /// <param name="builder">Filter definition builder.</param>
    /// <param name="partTypeKey">Part type key with form <c>typeId[:roleId]</c>.
    /// </param>
    /// <returns>Filter.</returns>
    private static FilterDefinition<BsonDocument> BuildPartTypeKeyFilter(
        FilterDefinitionBuilder<BsonDocument> builder, string partTypeKey)
    {
        // parse the key: typeId[:roleId]
        string[] parts = partTypeKey.Split(':', 2);
        string typeId = parts[0];
        string? roleId = parts.Length > 1 ? parts[1] : null;

        if (roleId == null)
        {
            // match parts with the specified typeId and null roleId
            return builder.And(
                builder.Eq("typeId", typeId),
                builder.Or(
                    builder.Not(builder.Exists("roleId")),
                    builder.Eq("roleId", BsonNull.Value)
                )
            );
        }
        else
        {
            // match parts with the specified typeId and roleId
            return builder.And(
                builder.Eq("typeId", typeId),
                builder.Eq("roleId", roleId)
            );
        }
    }

    /// <summary>
    /// Build the part type filters based on the options whitelist and blacklist.
    /// </summary>
    /// <param name="builder">The filter definition builder.</param>
    /// <returns>Filter.</returns>
    private FilterDefinition<BsonDocument> BuildPartTypeFilters(
        FilterDefinitionBuilder<BsonDocument> builder)
    {
        // create filters for part type keys
        List<FilterDefinition<BsonDocument>> filters = [];

        // apply whitelist if specified
        if (_options.WhitePartTypeKeys?.Count > 0)
        {
            List<FilterDefinition<BsonDocument>> whiteList = [];
            foreach (string key in _options.WhitePartTypeKeys)
                whiteList.Add(BuildPartTypeKeyFilter(builder, key));

            filters.Add(builder.Or(whiteList));
        }

        // apply blacklist if specified
        if (_options.BlackPartTypeKeys?.Count > 0)
        {
            List<FilterDefinition<BsonDocument>> blackList = [];
            foreach (string key in _options.BlackPartTypeKeys)
                blackList.Add(BuildPartTypeKeyFilter(builder, key));

            filters.Add(builder.Not(builder.Or(blackList)));
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Gets the parts for a specific item using the history_parts collection.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The list of parts for the item.</returns>
    private List<BsonDocument> GetItemParts(IMongoDatabase db, string itemId)
    {
        // Get history_parts collection
        IMongoCollection<BsonDocument> historyPartsCollection =
            db.GetCollection<BsonDocument>(MongoHistoryPart.COLLECTION);

        // create filter builder
        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;

        // parts must be for this item
        FilterDefinition<BsonDocument> filter = filterBuilder.Eq("itemId", itemId);

        // add time constraints if they exist
        if (_options.MinModified.HasValue)
        {
            filter = filterBuilder.And(
                filter,
                filterBuilder.Lte("timeModified",
                    _options.MinModified.Value)
            );
        }

        if (_options.MaxModified.HasValue)
        {
            filter = filterBuilder.And(
                filter,
                filterBuilder.Lte("timeModified",
                    _options.MaxModified.Value)
            );
        }

        // add part type filters if specified
        if (_options.WhitePartTypeKeys?.Count > 0 ||
            _options.BlackPartTypeKeys?.Count > 0)
        {
            filter = filterBuilder.And(filter,
                BuildPartTypeFilters(filterBuilder));
        }

        // aggregate to get the latest version of each part:
        // 1. Match the filter
        // 2. Sort by timeModified descending to get latest versions first
        // 3. Group by referenceId, keeping the first (latest) document
        BsonDocument[] pipeline =
        [
            new BsonDocument("$match", filter.ToBsonDocument()),
            new BsonDocument("$sort", new BsonDocument("timeModified", -1)),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$referenceId" },
                { "doc", new BsonDocument("$first", "$$ROOT") }
            }),
            new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc"))
        ];

        // execute the aggregation
        List<BsonDocument> parts = historyPartsCollection
            .Aggregate<BsonDocument>(pipeline).ToList();

        // process each part to make it suitable for export
        foreach (BsonDocument? part in parts)
        {
            // set _id to referenceId and remove referenceId
            part["_id"] = part["referenceId"];
            part.Remove("referenceId");

            // set _status based on status
            if (part.Contains("status"))
            {
                part["_status"] = part["status"];
                part.Remove("status");
            }
        }

        return parts;
    }

    /// <summary>
    /// Gets the items from the MongoDB database, using the history items
    /// collection to determine their state at the specified timeframe.
    /// </summary>
    /// <returns>Items.</returns>
    public IEnumerable<BsonDocument> GetItems()
    {
        // get the MongoDB client and database
        EnsureClientCreated(string.Format(_options.ConnectionString,
            _options.DatabaseName));
        IMongoDatabase db = Client!.GetDatabase(_options.DatabaseName);

        // get history_items collection
        IMongoCollection<BsonDocument> historyItemsCollection =
            db.GetCollection<BsonDocument>(MongoHistoryItem.COLLECTION);

        // create filter builder
        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;

        // apply base filter and time constraints
        FilterDefinition<BsonDocument> filter = BuildItemFilter(filterBuilder);

        // if we don't want deleted items, exclude them
        if (_options.NoDeleted)
        {
            filter = filterBuilder.And(filter,
                filterBuilder.Ne("status", (int)EditStatus.Deleted));
        }

        // create aggregation pipeline
        List<BsonDocument> pipelineDefinitions =
        [
            // match the filter - use ToBsonDocument() instead of Render()
            new BsonDocument("$match", filter.ToBsonDocument()),
            // sort by timeModified descending
            new BsonDocument("$sort", new BsonDocument("timeModified", -1)),
            // group by referenceId to get latest version of each item
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$referenceId" },
                { "doc", new BsonDocument("$first", "$$ROOT") }
            }),
            // replace root to work with the actual document
            new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc")),
            // sort by sortKey for consistent output
            new BsonDocument("$sort", new BsonDocument("sortKey", 1))
        ];

        // add pagination if requested
        if (_options.PageSize > 0)
        {
            pipelineDefinitions.Add(new BsonDocument("$skip",
                (_options.PageNumber - 1) * _options.PageSize));
            pipelineDefinitions.Add(new BsonDocument("$limit",
                _options.PageSize));
        }

        // execute the aggregation
        using IAsyncCursor<BsonDocument> cursor = historyItemsCollection
            .Aggregate<BsonDocument>(pipelineDefinitions);

        // process each item
        foreach (BsonDocument? item in cursor.ToEnumerable())
        {
            // set _id to referenceId and remove referenceId
            item["_id"] = item["referenceId"];
            item.Remove("referenceId");

            // set _status based on status
            if (item.Contains("status"))
            {
                item["_status"] = item["status"];
                item.Remove("status");
            }

            // add parts if requested
            if (!_options.NoParts)
            {
                List<BsonDocument> parts = GetItemParts(db, item["_id"].AsString);
                item["_parts"] = new BsonArray(parts);
            }

            yield return item;
        }
    }

    private string BuildFileName(int nr)
    {
        string fileName = $"{_options.DatabaseName}";
        if (nr > 0) fileName += $"_{nr:000}";
        fileName += ".json";
        return Path.Combine(_options.OutputDirectory, fileName);
    }

    private void WriteHead(StreamWriter writer, int fileNr,
        JsonWriterSettings jsonSettings)
    {
        // open root object
        writer.WriteLine("{");

        // write time of dump
        writer.WriteLine($"  \"time\": \"{DateTime.UtcNow:O}\",");

        // write chunk if needed
        if (_options.MaxItemsPerFile > 0)
        {
            writer.WriteLine($"  \"chunk\": {fileNr},");
        }

        // write options JSON object
        writer.WriteLine("  \"options\": ");
        writer.WriteLine(_options.ToJson(jsonSettings));
        writer.WriteLine("  }");

        // open items array
        writer.WriteLine("  \"items\": [");
    }

    private static void WriteTail(StreamWriter writer)
    {
        // close items array and root object
        writer.WriteLine("]");
        writer.WriteLine("}");
    }

    /// <summary>
    /// Dump the items to JSON files.
    /// </summary>
    /// <param name="cancel">The cancellation token.</param>
    /// <param name="progress">The optional progress reporter.</param>
    /// <returns>Count of items dumped.</returns>
    public int Dump(CancellationToken cancel,
        IProgress<ProgressReport>? progress = null)
    {
        EnsureClientCreated(string.Format(_options.ConnectionString,
            _options.DatabaseName));
        ProgressReport? report = progress is null ? null : new ProgressReport();

        int count = 0, fileNr = 0;
        StreamWriter? writer = null;
        JsonWriterSettings jsonSettings = new()
        {
            Indent = _options.Indented,
        };

        // get items
        foreach (var item in GetItems())
        {
            // create new file for this chunk if needed
            if (writer == null || (_options.MaxItemsPerFile > 0
                && count >= _options.MaxItemsPerFile))
            {
                if (writer != null) WriteTail(writer);
                writer?.Flush();
                writer?.Close();

                string path = BuildFileName(++fileNr);
                writer = new StreamWriter(path, false, Encoding.UTF8);
                WriteHead(writer, fileNr, jsonSettings);
                count = 0;
            }

            // write the item as JSON
            string json = item.ToJson(jsonSettings);
            writer.WriteLine(json);

            if (cancel.IsCancellationRequested)
            {
                // if cancelled, close the file and return
                writer.Flush();
                writer.Close();
                return count;
            }

            // report progress periodically
            if (progress != null && count % 100 == 0)
            {
                report!.Count = count;
                progress.Report(report);
            }
        }

        if (writer != null) WriteTail(writer);
        writer?.Flush();
        writer?.Close();

        if (progress != null)
        {
            report!.Count = count;
            progress.Report(report);
        }

        return count;
    }
}
