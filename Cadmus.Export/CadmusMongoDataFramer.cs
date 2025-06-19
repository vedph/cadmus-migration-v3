using Cadmus.Core.Storage;
using Cadmus.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadmus.Export;

/// <summary>
/// Cadmus MongoDB data framer.
/// </summary>
/// <remarks>This is used to collect Cadmus MongoDB data from a frame specified
/// by filters, typically a time frame, and return it as a sequence of items
/// with their parts, ready to be exported or otherwise processed.
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
public sealed class CadmusMongoDataFramer : MongoConsumerBase
{
    private readonly CadmusMongoDataFramerOptions _options;

    /// <summary>
    /// Create a new instance of <see cref="CadmusMongoDataFramer"/>.
    /// </summary>
    /// <param name="options">Options.</param>
    /// <exception cref="ArgumentNullException">options</exception>
    public CadmusMongoDataFramer(CadmusMongoDataFramerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Builds item filter without time-based constraints.
    /// </summary>
    /// <param name="filter">The source filter.</param>
    /// <param name="builder">Filter builder.</param>
    /// <returns>Filter definition.</returns>
    private static FilterDefinition<BsonDocument> BuildBaseItemFilter(
        CadmusDumpFilter filter,
        FilterDefinitionBuilder<BsonDocument> builder)
    {
        if (filter.IsEmpty) return builder.Empty;

        List<FilterDefinition<BsonDocument>> filters = [];

        if (!string.IsNullOrEmpty(filter.Title))
        {
            filters.Add(builder.Regex("title",
                new BsonRegularExpression(filter.Title, "i")));
        }

        if (!string.IsNullOrEmpty(filter.Description))
        {
            filters.Add(builder.Regex("description",
                new BsonRegularExpression(filter.Description, "i")));
        }

        if (!string.IsNullOrEmpty(filter.FacetId))
        {
            filters.Add(builder.Eq("facetId", filter.FacetId));
        }

        if (!string.IsNullOrEmpty(filter.GroupId))
        {
            filters.Add(builder.Eq("groupId", filter.GroupId));
        }

        if (!string.IsNullOrEmpty(filter.UserId))
        {
            filters.Add(builder.Eq("userId", filter.UserId));
        }

        // flags filter with matching mode
        if (filter.Flags.HasValue)
        {
            switch (filter.FlagMatching)
            {
                case FlagMatching.BitsAllSet:
                    filters.Add(builder.BitsAllSet("flags",
                        filter.Flags.Value));
                    break;
                case FlagMatching.BitsAnySet:
                    filters.Add(builder.BitsAnySet("flags",
                        filter.Flags.Value));
                    break;
                case FlagMatching.BitsAllClear:
                    filters.Add(builder.BitsAllClear("flags",
                        filter.Flags.Value));
                    break;
            }
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Builds the MongoDB filter for items getting all parameters
    /// <paramref name="filter"/>, including time-based constraints.
    /// </summary>
    /// <param name="filter">The source filter.</param>
    /// <param name="builder">Filter definition builder.</param>
    /// <returns>Filter.</returns>
    private FilterDefinition<BsonDocument> BuildItemFilter(
        CadmusDumpFilter filter,
        FilterDefinitionBuilder<BsonDocument> builder)
    {
        // start with the base filter (non-time based constraints)
        FilterDefinition<BsonDocument> builtFilter =
            BuildBaseItemFilter(filter, builder);

        // if no filter or no time constraints, return the base filter
        if (filter.IsEmpty ||
            (!filter.MinModified.HasValue && !filter.MaxModified.HasValue))
        {
            return builtFilter;
        }

        // start with empty filters and add the simple one if any
        List<FilterDefinition<BsonDocument>> filters = [];
        if (builtFilter != builder.Empty) filters.Add(builtFilter);

        // if incremental mode, we want only those items which *changed*
        // (=were updated, created, or deleted - all these affect timeModified)
        // in the specified time frame
        if (_options.IsIncremental)
        {
            if (filter.MinModified.HasValue)
                filters.Add(builder.Gte("timeModified", filter.MinModified.Value));

            if (filter.MaxModified.HasValue)
                filters.Add(builder.Lte("timeModified", filter.MaxModified.Value));
        }
        // else we want all items active or deleted up to MaxModified if any,
        // or just all items if no MaxModified
        else
        {
            if (filter.MaxModified.HasValue)
                filters.Add(builder.Lte("timeModified", filter.MaxModified.Value));
        }

        // combine and return the filters
        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Builds a filter for a part type key.
    /// </summary>
    /// <param name="builder">Filter definition builder.</param>
    /// <param name="partTypeKey">Part type key with form
    /// <c>typeId[:roleId]</c>.</param>
    /// <param name="isBlacklist">True if this is a blacklist key, false if
    /// it's a whitelist key.</param>
    /// <returns>Filter.</returns>
    private static FilterDefinition<BsonDocument> BuildPartTypeKeyFilter(
        FilterDefinitionBuilder<BsonDocument> builder, string partTypeKey,
        bool isBlacklist = false)
    {
        // parse the key: typeId[:roleId]
        string[] parts = partTypeKey.Split(':', 2);
        string typeId = parts[0];
        string? roleId = parts.Length > 1 ? parts[1] : null;

        if (isBlacklist)
        {
            // for blacklist, exclude all parts with the specified typeId,
            // regardless of roleId
            return builder.Eq("typeId", typeId);
        }

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
    /// <param name="filter">The source filter.</param>
    /// <param name="builder">The filter definition builder.</param>
    /// <returns>Filter.</returns>
    private static FilterDefinition<BsonDocument> BuildPartTypeFilters(
        CadmusDumpFilter filter,
        FilterDefinitionBuilder<BsonDocument> builder)
    {
        // filters for part type keys
        List<FilterDefinition<BsonDocument>> filters = [];

        // apply whitelist if specified: all keys are combined with OR
        if (filter.WhitePartTypeKeys?.Count > 0)
        {
            List<FilterDefinition<BsonDocument>> whiteList = [];
            foreach (string key in filter.WhitePartTypeKeys)
                whiteList.Add(BuildPartTypeKeyFilter(builder, key));

            filters.Add(builder.Or(whiteList));
        }

        // apply blacklist if specified: all keys are combined with OR NOT
        if (filter.BlackPartTypeKeys?.Count > 0)
        {
            List<FilterDefinition<BsonDocument>> blackList = [];
            foreach (string key in filter.BlackPartTypeKeys)
            {
                blackList.Add(BuildPartTypeKeyFilter(builder, key,
                    isBlacklist: true));
            }

            filters.Add(builder.Not(builder.Or(blackList)));
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    /// <summary>
    /// Renders the specified filter to a BsonDocument.
    /// </summary>
    /// <param name="filter">Filter.</param>
    /// <returns>BsonDocument.</returns>
    private static BsonDocument RenderFilter(FilterDefinition<BsonDocument>
        filter)
    {
        IBsonSerializer<BsonDocument> serializer =
            BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>();
        RenderArgs<BsonDocument> renderArgs = new(
            serializer,
            BsonSerializer.SerializerRegistry
        );
        return filter.Render(renderArgs);
    }

    /// <summary>
    /// Gets the parts for a specific item using the <c>history-parts</c>
    /// collection.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="filter">The source filter.</param>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The list of parts for the item.</returns>
    private List<BsonDocument> GetItemParts(IMongoDatabase db,
        CadmusDumpFilter filter, string itemId)
    {
        // get collection
        IMongoCollection<BsonDocument> historyPartsCollection =
            db.GetCollection<BsonDocument>(MongoHistoryPart.COLLECTION);

        // filter by itemId
        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;
        FilterDefinition<BsonDocument> builtFilter =
            filterBuilder.Eq("itemId", itemId);
        List<FilterDefinition<BsonDocument>> filters = [builtFilter];

        // add time filter if specified
        if (_options.IsIncremental)
        {
            if (filter.MinModified.HasValue)
            {
                filters.Add(filterBuilder.Gte("timeModified",
                    filter.MinModified.Value));
            }
            if (filter.MaxModified.HasValue)
            {
                filters.Add(filterBuilder.Lte("timeModified",
                    filter.MaxModified.Value));
            }
        }
        else
        {
            if (filter.MaxModified.HasValue)
            {
                filters.Add(filterBuilder.Lte("timeModified",
                    filter.MaxModified.Value));
            }
        }

        // add part type filters if specified
        if (filter.WhitePartTypeKeys?.Count > 0 ||
            filter.BlackPartTypeKeys?.Count > 0)
        {
            filters.Add(BuildPartTypeFilters(filter, filterBuilder));
        }

        builtFilter = filters.Count > 1 ? filterBuilder.And(filters) : filters[0];

        // render the filter to a BsonDocument
        BsonDocument renderedFilter = RenderFilter(builtFilter);

        // build the pipeline to get the latest version of each part
        BsonDocument[] pipeline =
        [
            // match the filter
            new BsonDocument("$match", renderedFilter),
            // sort by timeModified descending to get the latest first
            new BsonDocument("$sort", new BsonDocument("timeModified", -1)),
            // group by referenceId (itemId), keeping the first document
            // which is the latest version of the part for that item
            new BsonDocument("$group", new BsonDocument
            {
                // group by referenceId, which is the itemId in this case
                { "_id", "$referenceId" },
                // capture the entire document for each group as "doc"
                { "doc", new BsonDocument("$first", "$$ROOT") }
            }),
            // replace the root with the doc field, unwrapping it
            new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc"))
        ];

        // aggregate the parts collection
        List<BsonDocument> parts = historyPartsCollection
            .Aggregate<BsonDocument>(pipeline).ToList();

        // adjust the parts schema before returning
        foreach (BsonDocument? part in parts)
        {
            part["_id"] = part["referenceId"];
            part.Remove("referenceId");

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
    /// <param name="filter">The filter to use.</param>
    /// <returns>Items.</returns>
    public IEnumerable<BsonDocument> GetItems(CadmusDumpFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        EnsureClientCreated(string.Format(_options.ConnectionString,
            _options.DatabaseName));
        IMongoDatabase db = Client!.GetDatabase(_options.DatabaseName);

        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;

        IMongoCollection<BsonDocument> historyItems =
            db.GetCollection<BsonDocument>(MongoHistoryItem.COLLECTION);

        if (_options.IsIncremental)
        {
            // 1. items changed in window
            FilterDefinition<BsonDocument> itemFilter =
                BuildItemFilter(filter, filterBuilder);

            // 2. items with parts changed in window (apply part type filters)
            List<FilterDefinition<BsonDocument>> partFilters = [];

            if (filter.MinModified.HasValue)
            {
                partFilters.Add(filterBuilder.Gte("timeModified",
                    filter.MinModified.Value));
            }

            if (filter.MaxModified.HasValue)
            {
                partFilters.Add(filterBuilder.Lte("timeModified",
                    filter.MaxModified.Value));
            }

            if ((filter.WhitePartTypeKeys?.Count ?? 0) > 0 ||
                (filter.BlackPartTypeKeys?.Count ?? 0) > 0)
            {
                partFilters.Add(BuildPartTypeFilters(filter, filterBuilder));
            }

            FilterDefinition<BsonDocument> partFilter = partFilters.Count > 0
                ? filterBuilder.And(partFilters) : filterBuilder.Empty;

            IMongoCollection<BsonDocument> historyParts =
                db.GetCollection<BsonDocument>(MongoHistoryPart.COLLECTION);
            HashSet<string> itemIdsFromParts = [.. historyParts
                .Find(partFilter)
                .Project("{itemId: 1}")
                .ToList()
                .Select(p => p["itemId"].AsString)];

            // 3. union with items changed in window
            HashSet<string> itemsInWindow = [.. historyItems
                .Find(itemFilter)
                .ToList()
                .Select(i => i["referenceId"].AsString)];

            List<string> allItemIds = [.. itemsInWindow.Union(itemIdsFromParts)];

            if (allItemIds.Count == 0) yield break;

            // 4. fetch latest version of each item in the union set
            FilterDefinition<BsonDocument> finalFilter =
                filterBuilder.In("referenceId", allItemIds);
            List<BsonDocument> pipeline =
            [
                new BsonDocument("$match", RenderFilter(finalFilter)),
                new BsonDocument("$sort", new BsonDocument("timeModified", -1)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$referenceId" },
                    { "doc", new BsonDocument("$first", "$$ROOT") }
                }),
                new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc")),
                new BsonDocument("$sort", new BsonDocument("sortKey", 1))
            ];

            IAsyncCursor<BsonDocument> cursor =
                historyItems.Aggregate<BsonDocument>(pipeline);
            foreach (BsonDocument? item in cursor.ToEnumerable())
            {
                item["_id"] = item["referenceId"];
                item.Remove("referenceId");
                if (item.Contains("status"))
                {
                    item["_status"] = item["status"];
                    item.Remove("status");
                }
                // exclude deleted items if NoDeleted is set
                if (_options.NoDeleted && (EditStatus)item["_status"].AsInt32
                    == EditStatus.Deleted)
                {
                    continue;
                }

                if (!_options.NoParts)
                {
                    List<BsonDocument> parts = GetItemParts(db,
                        filter, item["_id"].AsString);
                    item["_parts"] = new BsonArray(parts);
                }
                yield return item;
            }
        }
        else
        {
            FilterDefinition<BsonDocument> builtFilter =
                BuildItemFilter(filter, filterBuilder);

            if (_options.NoDeleted)
            {
                builtFilter = filterBuilder.And(builtFilter,
                    filterBuilder.Ne("status", (int)EditStatus.Deleted));
            }

            List<BsonDocument> pipeline =
            [
                new BsonDocument("$match", builtFilter == filterBuilder.Empty
                    ? new BsonDocument()
                    : RenderFilter(builtFilter)),
                new BsonDocument("$sort", new BsonDocument("timeModified", -1)),
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$referenceId" },
                    { "doc", new BsonDocument("$first", "$$ROOT") }
                }),
                new BsonDocument("$replaceRoot",
                    new BsonDocument("newRoot", "$doc")),
                new BsonDocument("$sort", new BsonDocument("sortKey", 1))
            ];

            if (filter.PageSize > 0)
            {
                pipeline.Add(new BsonDocument("$skip",
                    (filter.PageNumber - 1) * filter.PageSize));
                pipeline.Add(new BsonDocument("$limit", filter.PageSize));
            }

            using IAsyncCursor<BsonDocument> cursor =
                historyItems.Aggregate<BsonDocument>(pipeline);

            foreach (BsonDocument? item in cursor.ToEnumerable())
            {
                item["_id"] = item["referenceId"];
                item.Remove("referenceId");
                if (item.Contains("status"))
                {
                    item["_status"] = item["status"];
                    item.Remove("status");
                }
                if (!_options.NoParts)
                {
                    List<BsonDocument> parts = GetItemParts(
                        db, filter, item["_id"].AsString);
                    item["_parts"] = new BsonArray(parts);
                }
                yield return item;
            }
        }
    }
}
