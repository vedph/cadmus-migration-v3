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
/// Items are sorted by their sort key, and filtered according to these
/// criteria:
/// <list type="bullet">
/// <item>
///   the items must match all the filters specified for them.
/// </item>
/// <item>
///   additionally, when an item does match all the filters specified
///   except for the time-based filters (i.e. last modified), it can be included
///   when any of its parts, once filtered with their own filter, match the same
///   time-based filters for their last modified property. This means that
///   an item will be included as changed even when its last-modified property
///   is not in the filter time frame, but the last-modified property of any
///   of its parts is. So, changing an item's part will be enough to include
///   that item among those which were changed.
/// </item>
/// <item>
///   deleted items are included too, unless the <c>NoDeleted</c> option is true,
///   provided that they match the same filters as the normal items.
/// </item>
/// </list>
/// <para>Also, this dumper can be used for both full or incremental dumps.
/// When you specify a timeframe in filters (via min/max modified), items and
/// parts states will be calculated relative to that timeframe.</para>
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
/// history part, with status=deleted.</remarks>
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
        if (_options.Filter == null) return builder.Empty;

        List<FilterDefinition<BsonDocument>> filters = [];

        if (!string.IsNullOrEmpty(_options.Filter.Title))
        {
            filters.Add(builder.Regex("title",
                new BsonRegularExpression(_options.Filter.Title, "i")));
        }

        if (!string.IsNullOrEmpty(_options.Filter.Description))
        {
            filters.Add(builder.Regex("description",
                new BsonRegularExpression(_options.Filter.Description, "i")));
        }

        if (!string.IsNullOrEmpty(_options.Filter.FacetId))
        {
            filters.Add(builder.Eq("facetId", _options.Filter.FacetId));
        }

        if (!string.IsNullOrEmpty(_options.Filter.GroupId))
        {
            filters.Add(builder.Eq("groupId", _options.Filter.GroupId));
        }

        if (!string.IsNullOrEmpty(_options.Filter.UserId))
        {
            filters.Add(builder.Eq("userId", _options.Filter.UserId));
        }

        // flags filter with matching mode
        if (_options.Filter.Flags.HasValue)
        {
            switch (_options.Filter.FlagMatching)
            {
                case FlagMatching.BitsAllSet:
                    filters.Add(builder.BitsAllSet("flags",
                        _options.Filter.Flags.Value));
                    break;
                case FlagMatching.BitsAnySet:
                    filters.Add(builder.BitsAnySet("flags",
                        _options.Filter.Flags.Value));
                    break;
                case FlagMatching.BitsAllClear:
                    filters.Add(builder.BitsAllClear("flags",
                        _options.Filter.Flags.Value));
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
        if (_options.Filter == null ||
            (!_options.Filter.MinModified.HasValue &&
             !_options.Filter.MaxModified.HasValue))
        {
            return filter;
        }

        // add time-based constraints
        List<FilterDefinition<BsonDocument>> filters = [];

        // start with the base filter if it's not empty
        if (filter != builder.Empty) filters.Add(filter);

        // add date range filter for items
        if (_options.Filter.MinModified.HasValue)
        {
            filters.Add(builder.Gte("timeModified",
                _options.Filter.MinModified.Value));
        }

        if (_options.Filter.MaxModified.HasValue)
        {
            filters.Add(builder.Lte("timeModified",
                _options.Filter.MaxModified.Value));
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
            {
                whiteList.Add(BuildPartTypeKeyFilter(builder, key));
            }
            filters.Add(builder.Or(whiteList));
        }

        // apply blacklist if specified
        if (_options.BlackPartTypeKeys?.Count > 0)
        {
            List<FilterDefinition<BsonDocument>> blackList = [];
            foreach (string key in _options.BlackPartTypeKeys)
            {
                blackList.Add(BuildPartTypeKeyFilter(builder, key));
            }
            filters.Add(builder.Not(builder.Or(blackList)));
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Get the item IDs from the specified collection that match the
    /// time-based filters for their parts. This is used to find items which
    /// would not be included in the export because of a non-matching time-based
    /// filter: here, the match is extended to the item's parts, so that
    /// the item is included in the export if at least one of its (possibly
    /// filtered) parts matches the time-based filter.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <returns>Matching IDs.</returns>
    private HashSet<string> GetItemsWithMatchingParts(IMongoDatabase db,
        string collectionName)
    {
        // get the parts/history-parts collection
        IMongoCollection<BsonDocument> partsCollection =
            db.GetCollection<BsonDocument>(collectionName);

        // build the filter for parts based on the options
        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;
        List<FilterDefinition<BsonDocument>> filters = [];

        // date range filters for the parts
        if (_options.Filter?.MinModified != null)
        {
            filters.Add(filterBuilder.Gte("timeModified",
                _options.Filter.MinModified.Value));
        }

        if (_options.Filter?.MaxModified != null)
        {
            filters.Add(filterBuilder.Lte("timeModified",
                _options.Filter.MaxModified.Value));
        }

        // apply whitelist/blacklist if specified
        if (_options.WhitePartTypeKeys?.Count > 0 ||
            _options.BlackPartTypeKeys?.Count > 0)
        {
            filters.Add(BuildPartTypeFilters(filterBuilder));
        }

        // if targeting history parts, add status filter for deleted parts
        if (collectionName == MongoHistoryPart.COLLECTION)
        {
            filters.Add(filterBuilder.Eq("status", (int)EditStatus.Deleted));
        }

        // compose the final filter for parts
        FilterDefinition<BsonDocument> filter = filters.Count > 0
            ? filterBuilder.And(filters) : filterBuilder.Empty;

        ProjectionDefinition<BsonDocument> projection =
            Builders<BsonDocument>.Projection.Include("itemId");

        // get the list of item IDs from parts that match the filter
        // by projecting only the itemId field from results
        HashSet<string> itemIds = [];
        using IAsyncCursor<BsonDocument> cursor = partsCollection.Find(filter)
            .Project(projection).ToCursor();

        while (cursor.MoveNext())
        {
            foreach (BsonDocument document in cursor.Current)
            {
                if (document.TryGetValue("itemId", out BsonValue? itemId) &&
                    !itemId.IsBsonNull)
                {
                    itemIds.Add(itemId.AsString);
                }
            }
        }

        return itemIds;
    }

    /// <summary>
    /// Gets items from the MongoDB database items collection.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="itemIds">The returned items IDs.</param>
    /// <returns>Items.</returns>
    private IEnumerable<BsonDocument> GetNormalItems(
        IMongoDatabase db, HashSet<string> itemIds)
    {
        // get items collection
        IMongoCollection<BsonDocument> itemsCollection =
            db.GetCollection<BsonDocument>(MongoItem.COLLECTION);

        // build the filter for items
        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;

        // create base item filter without time constraints
        FilterDefinition<BsonDocument> baseItemFilter =
            BuildBaseItemFilter(filterBuilder);

        // create the full filter with time constraints
        FilterDefinition<BsonDocument> fullItemFilter =
            BuildItemFilter(filterBuilder);

        // get cursor from filtered and sorted collection
        SortDefinition<BsonDocument> sort =
            Builders<BsonDocument>.Sort.Ascending("sortKey");
        using IAsyncCursor<BsonDocument> cursor = itemsCollection
            .Find(fullItemFilter)
            .Sort(sort)
            .ToCursor();

        // return items from the cursor, tracking their IDs
        while (cursor.MoveNext())
        {
            foreach (BsonDocument document in cursor.Current)
            {
                string itemId = document["_id"].AsString;
                itemIds.Add(itemId);
                yield return document;
            }
        }

        // only check for additional items if we have time-based filters
        if (!_options.NoPartDate &&
            (_options.Filter?.MinModified != null ||
             _options.Filter?.MaxModified != null))
        {
            // get IDs of items with parts matching the time filters
            HashSet<string> additionalItemIds =
                GetItemsWithMatchingParts(db, MongoPart.COLLECTION);

            // if we have additional items, filter out those already returned
            if (additionalItemIds.Count > 0)
            {
                // filter out already returned items
                List<string> notReturnedItemIds = [..
                    additionalItemIds.Where(id => !itemIds.Contains(id))];

                // if there additional items to return, return them while
                // tracking their IDs
                if (notReturnedItemIds.Count > 0)
                {
                    // apply the base item filters (without time constraints)
                    // along with the ID filter for these additional items
                    FilterDefinition<BsonDocument> additionalFilter =
                        filterBuilder.And(baseItemFilter,
                            filterBuilder.In("_id", notReturnedItemIds)
                    );

                    // get additional items from the items collection
                    using IAsyncCursor<BsonDocument> additionalCursor =
                        itemsCollection.Find(additionalFilter)
                                       .Sort(sort)
                                       .ToCursor();

                    // return additional items while tracking their IDs
                    while (additionalCursor.MoveNext())
                    {
                        foreach (BsonDocument document in additionalCursor.Current)
                        {
                            string itemId = document["_id"].AsString;
                            itemIds.Add(itemId);
                            yield return document;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get the deleted items from the MongoDB database history items.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="itemIds">The collected items IDs.</param>
    /// <returns>Items.</returns>
    private IEnumerable<BsonDocument> GetDeletedItems(
        IMongoDatabase db, HashSet<string> itemIds)
    {
        // collection for history items
        IMongoCollection<BsonDocument> historyItemsCollection =
            db.GetCollection<BsonDocument>(MongoHistoryItem.COLLECTION);

        // build the filter for history items
        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;

        // create base item filter with time constraints
        FilterDefinition<BsonDocument> historyFilter =
            BuildItemFilter(filterBuilder);

        // ensure we only retrieve deleted items (status = 2)
        historyFilter = filterBuilder.And(
            historyFilter,
            filterBuilder.Eq("status", (int)EditStatus.Deleted)
        );

        // get additional items based on history part dates if required
        HashSet<string> additionalItemIds = [];
        if (!_options.NoPartDate &&
            (_options.Filter?.MinModified != null ||
             _options.Filter?.MaxModified != null))
        {
            additionalItemIds = GetItemsWithMatchingParts(db,
                MongoHistoryPart.COLLECTION);
        }

        // get cursor from filtered and sorted history items
        SortDefinition<BsonDocument> sort = Builders<BsonDocument>
            .Sort.Ascending("sortKey");

        using IAsyncCursor<BsonDocument> cursor = historyItemsCollection
            .Find(historyFilter)
            .Sort(sort)
            .ToCursor();

        // return deleted items that haven't already been returned
        while (cursor.MoveNext())
        {
            foreach (BsonDocument document in cursor.Current)
            {
                // get the referenceId (guaranteed to exist)
                string refId = document["referenceId"].AsString;

                // skip if we already returned this item from the items collection
                if (itemIds.Contains(refId)) continue;

                itemIds.Add(refId);

                // modify document: set _id to referenceId and
                // remove referenceId property
                BsonDocument modifiedDoc = (BsonDocument)document.DeepClone();
                modifiedDoc["_id"] = document["referenceId"];
                modifiedDoc.Remove("referenceId");
                // keep the status property to distinguish deleted items

                yield return modifiedDoc;
            }
        }

        // add items found via history-parts collection if needed
        if (additionalItemIds.Count > 0)
        {
            // filter out already returned items
            List<string> notReturnedItemIds = [..
                additionalItemIds.Where(id => !itemIds.Contains(id))];

            if (notReturnedItemIds.Count > 0)
            {
                // find history items by their referenceId
                FilterDefinition<BsonDocument> additionalFilter =
                    filterBuilder.And(
                        filterBuilder.In("referenceId", notReturnedItemIds),
                        filterBuilder.Eq("status", (int)EditStatus.Deleted));

                using IAsyncCursor<BsonDocument> additionalCursor =
                    historyItemsCollection.Find(additionalFilter)
                    .Sort(sort)
                    .ToCursor();

                while (additionalCursor.MoveNext())
                {
                    foreach (BsonDocument document in additionalCursor.Current)
                    {
                        // get the referenceId (guaranteed to exist)
                        string refId = document["referenceId"].AsString;

                        // skip if already processed
                        if (itemIds.Contains(refId)) continue;

                        // mark as processed
                        itemIds.Add(refId);

                        // modify document: set _id to referenceId
                        // and remove referenceId property
                        BsonDocument modifiedDoc = (BsonDocument)
                            document.DeepClone();
                        modifiedDoc["_id"] = document["referenceId"];
                        modifiedDoc.Remove("referenceId");
                        // keep the status property to distinguish deleted items

                        yield return modifiedDoc;
                    }
                }
            }
        }
    }

    private void AddItemStatus(BsonDocument item)
    {
        // for deleted items from history (they already have a status)
        if (item.Contains("status"))
        {
            item["_status"] = item["status"];
            // remove status property to avoid confusion with _status
            item.Remove("status");
            return;
        }

        // for active items, we need to determine status based on the timeframe
        DateTime timeCreated = item["timeCreated"].ToUniversalTime();
        DateTime timeModified = item["timeModified"].ToUniversalTime();

        // if we don't have a time filter (full dump), use simple logic
        if (_options.Filter == null ||
            (!_options.Filter.MinModified.HasValue &&
             !_options.Filter.MaxModified.HasValue))
        {
            // if timeCreated equals timeModified, it's a newly created item
            if (timeCreated == timeModified)
            {
                item["_status"] = new BsonInt32((int)EditStatus.Created);
            }
            else
            {
                // otherwise, it's an updated item
                item["_status"] = new BsonInt32((int)EditStatus.Updated);
            }
            return;
        }

        // for incremental dumps, determine status relative to the timeframe
        DateTime minTime = _options.Filter.MinModified ?? DateTime.MinValue;

        // if item was created within the timeframe, mark as created
        if (timeCreated >= minTime)
        {
            item["_status"] = new BsonInt32((int)EditStatus.Created);
        }
        // if item was modified within the timeframe, mark as updated
        else if (timeModified >= minTime)
        {
            item["_status"] = new BsonInt32((int)EditStatus.Updated);
        }
        // this shouldn't happen as our filters would exclude it, but just in case
        else
        {
            item["_status"] = new BsonInt32((int)EditStatus.Updated);
        }
    }

    private void AddPartStatus(BsonDocument part)
    {
        // if this part already has a status property (from history collections),
        // use it
        if (part.Contains("status"))
        {
            part["_status"] = part["status"];
            // remove status property to avoid confusion with _status
            part.Remove("status");
            return;
        }

        // for active parts, we need to determine status based on the timeframe
        DateTime timeCreated = part["timeCreated"].ToUniversalTime();
        DateTime timeModified = part["timeModified"].ToUniversalTime();

        // if we don't have a time filter (full dump), use simple logic
        if (_options.Filter == null ||
            (!_options.Filter.MinModified.HasValue && !_options.Filter.MaxModified.HasValue))
        {
            // if timeCreated equals timeModified, it's a newly created part
            if (timeCreated == timeModified)
            {
                part["_status"] = new BsonInt32((int)EditStatus.Created);
            }
            else
            {
                // otherwise, it's an updated part
                part["_status"] = new BsonInt32((int)EditStatus.Updated);
            }
            return;
        }

        // for incremental dumps, determine status relative to the timeframe
        DateTime minTime = _options.Filter.MinModified ?? DateTime.MinValue;

        // if part was created within the timeframe, mark as Created
        if (timeCreated >= minTime)
        {
            part["_status"] = new BsonInt32((int)EditStatus.Created);
        }
        // if part was modified within the timeframe, mark as Updated
        else if (timeModified >= minTime)
        {
            part["_status"] = new BsonInt32((int)EditStatus.Updated);
        }
        // this shouldn't happen as our filters would exclude it, but just in case
        else
        {
            part["_status"] = new BsonInt32((int)EditStatus.Updated);
        }
    }

    private void AddItemParts(BsonDocument item)
    {
        // get the parts collection
        IMongoDatabase db = Client!.GetDatabase(_options.DatabaseName);
        IMongoCollection<BsonDocument> partsCollection =
            db.GetCollection<BsonDocument>(MongoPart.COLLECTION);

        // get the item ID
        string itemId = item["_id"].AsString;

        // find parts for this item
        FilterDefinition<BsonDocument> filter =
            Builders<BsonDocument>.Filter.Eq("itemId", itemId);
        List<BsonDocument> parts = partsCollection.Find(filter).ToList();

        // add _status to each part before adding them to the item
        foreach (BsonDocument part in parts)
        {
            AddPartStatus(part);
        }

        // add _parts property with the found parts
        item["_parts"] = new BsonArray(parts);
    }

    /// <summary>
    /// Get the items from the MongoDB database, including parts if requested.
    /// </summary>
    /// <returns>Items.</returns>
    public IEnumerable<BsonDocument> GetItems()
    {
        // get the MongoDB client and database
        EnsureClientCreated(string.Format(_options.ConnectionString,
            _options.DatabaseName));
        IMongoDatabase db = Client!.GetDatabase(_options.DatabaseName);

        // track item IDs to avoid duplicates
        HashSet<string> itemIds = [];

        // 1. regular items collection (always included)
        foreach (BsonDocument document in GetNormalItems(db, itemIds))
        {
            // add _status to the item
            AddItemStatus(document);

            // add parts if requested
            if (!_options.NoParts) AddItemParts(document);

            yield return document;
        }

        // 2. history items collection (only when NoDeleted is false)
        if (!_options.NoDeleted)
        {
            foreach (BsonDocument document in GetDeletedItems(db, itemIds))
            {
                // items from GetDeletedItems already have status property
                // just add _status based on it
                AddItemStatus(document);

                yield return document;
            }
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
        ProgressReport? report = progress is null? null : new ProgressReport();

        int count = 0, fileNr = 0;
        StreamWriter? writer = null;
        JsonWriterSettings jsonSettings = new()
        {
            Indent = _options.Indented,
        };

        // for each matching item
        foreach (BsonDocument item in GetItems())
        {
            count++;

            // create a new file if needed
            if (writer == null || (count > _options.MaxItemsPerFile &&
                _options.MaxItemsPerFile > 0))
            {
                if (writer != null) WriteTail(writer);
                writer?.Flush();
                writer?.Close();

                string path = BuildFileName(++fileNr);
                writer = new StreamWriter(path, false, Encoding.UTF8);
                WriteHead(writer, fileNr, jsonSettings);
                count = 1;
            }

            // write the item to the file as JSON
            string json = item.ToJson(jsonSettings);
            writer.WriteLine(json);

            // check for cancellation
            if (cancel.IsCancellationRequested) break;

            // report progress if needed
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
