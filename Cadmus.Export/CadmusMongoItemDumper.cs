using Cadmus.Core.Storage;
using Cadmus.Mongo;
using Fusi.Tools;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadmus.Export;

/// <summary>
/// Cadmus MongoDB item dumper.
/// </summary>
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
                        itemsCollection.Find(additionalFilter).Sort(sort).ToCursor();

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

    private IEnumerable<BsonDocument> GetDeletedItems(
        IMongoDatabase db, HashSet<string> returnedItemIds)
    {
        // collection for history items
        IMongoCollection<BsonDocument> historyItemsCollection =
            db.GetCollection<BsonDocument>(MongoHistoryItem.COLLECTION);

        FilterDefinitionBuilder<BsonDocument> filterBuilder =
            Builders<BsonDocument>.Filter;

        // first apply base filter criteria to history items
        FilterDefinition<BsonDocument> historyFilter =
            BuildItemFilter(filterBuilder);

        // then ensure we only retrieve deleted items (status = 2)
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

        // only return deleted items that haven't already been returned
        SortDefinition<BsonDocument> sort = Builders<BsonDocument>
            .Sort.Ascending("sortKey");

        using IAsyncCursor<BsonDocument> cursor = historyItemsCollection
            .Find(historyFilter)
            .Sort(sort)
            .ToCursor();

        // track history items by their referenceId to avoid duplicates
        // (we want the most recent deletion record for each item)
        Dictionary<string, BsonDocument> latestDeletedItems = [];

        while (cursor.MoveNext())
        {
            foreach (BsonDocument document in cursor.Current)
            {
                // check if this history item has a referenceId
                if (document.TryGetValue("referenceId",
                    out BsonValue? referenceId) && !referenceId.IsBsonNull)
                {
                    string refId = referenceId.AsString;

                    // skip if we already returned this item from the regular
                    // collection
                    if (returnedItemIds.Contains(refId)) continue;

                    // if we have multiple history records for the same item,
                    // keep only the most recent one based on timeModified
                    if (latestDeletedItems.TryGetValue(refId,
                        out BsonDocument? existingDoc))
                    {
                        // compare timeModified to keep the most recent one
                        if (document.TryGetValue("timeModified",
                                out BsonValue? currentTime) &&
                            existingDoc.TryGetValue("timeModified",
                                out BsonValue? existingTime) &&
                            currentTime.ToUniversalTime() >
                            existingTime.ToUniversalTime())
                        {
                            latestDeletedItems[refId] = document;
                        }
                    }
                    else
                    {
                        // first time seeing this referenceId
                        latestDeletedItems[refId] = document;
                    }
                }
            }
        }

        // return the most recent deleted item records
        foreach (BsonDocument document in latestDeletedItems.Values)
        {
            if (document.TryGetValue("referenceId", out BsonValue? referenceId))
            {
                // Change _id to referenceId as we want the original item ID
                BsonDocument modifiedDoc = (BsonDocument)document.DeepClone();
                modifiedDoc["_id"] = referenceId;

                yield return modifiedDoc;
            }
        }

        // add items found via history-parts collection if needed
        if (additionalItemIds.Count > 0)
        {
            List<string> notReturnedItemIds = [..
            additionalItemIds.Where(id =>
            !returnedItemIds.Contains(id) &&
            !latestDeletedItems.ContainsKey(id))];

            if (notReturnedItemIds.Count > 0)
            {
                // find history items by their referenceId
                FilterDefinition<BsonDocument> additionalFilter =
                    filterBuilder.And(
                        filterBuilder.In("referenceId", notReturnedItemIds),
                        filterBuilder.Eq("status", (int)EditStatus.Deleted)
                );

                // track additional items to get only the most recent deletion
                Dictionary<string, BsonDocument> additionalDeletedItems = [];

                using IAsyncCursor<BsonDocument> additionalCursor =
                    historyItemsCollection.Find(additionalFilter)
                    .Sort(sort)
                    .ToCursor();

                while (additionalCursor.MoveNext())
                {
                    foreach (BsonDocument? document in additionalCursor.Current)
                    {
                        if (document.TryGetValue("referenceId",
                            out BsonValue? referenceId) && !referenceId.IsBsonNull)
                        {
                            string refId = referenceId.AsString;

                            // if we have multiple history records for the
                            // same item, keep only the most recent one
                            if (additionalDeletedItems.TryGetValue(refId,
                                out BsonDocument? existingDoc))
                            {
                                // compare timeModified to keep
                                // the most recent one
                                if (document.TryGetValue("timeModified",
                                        out BsonValue? currentTime) &&
                                    existingDoc.TryGetValue("timeModified",
                                        out BsonValue? existingTime) &&
                                    currentTime.ToUniversalTime() >
                                        existingTime.ToUniversalTime())
                                {
                                    additionalDeletedItems[refId] = document;
                                }
                            }
                            else
                            {
                                // first time seeing this referenceId
                                additionalDeletedItems[refId] = document;
                            }
                        }
                    }
                }

                // return the additional items
                foreach (BsonDocument document in additionalDeletedItems.Values)
                {
                    if (document.TryGetValue("referenceId",
                        out BsonValue? referenceId))
                    {
                        // change _id to referenceId as we want
                        // the original item ID
                        BsonDocument modifiedDoc = (BsonDocument)
                            document.DeepClone();
                        modifiedDoc["_id"] = referenceId;

                        yield return modifiedDoc;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the items to export from the MongoDB database.
    /// </summary>
    /// <returns></returns>
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
            yield return document;

        // 2. history items collection (only when NoDeleted is false)
        if (!_options.NoDeleted)
        {
            foreach (BsonDocument document in GetDeletedItems(db, itemIds))
                yield return document;
        }
    }

    public async Task DumpAsync(CancellationToken cancel,
        Progress<ProgressReport>? progress = null)
    {
        EnsureClientCreated(string.Format(_options.ConnectionString,
            _options.DatabaseName));

        foreach (BsonDocument item in GetItems())
        {
            // TODO
        }
    }
}
