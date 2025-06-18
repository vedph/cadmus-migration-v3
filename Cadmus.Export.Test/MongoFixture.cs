using Cadmus.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cadmus.Export.Test;

public sealed class MongoFixture : IDisposable
{
    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public MongoFixture()
    {
        // setup MongoDB client with connection to test database
        Client = new MongoClient("mongodb://localhost:27017");
        Database = Client.GetDatabase("test-db");
    }

    public void ClearDatabase()
    {
        // drop all collections
        Database.DropCollection(MongoItem.COLLECTION);
        Database.DropCollection(MongoPart.COLLECTION);
        Database.DropCollection(MongoHistoryItem.COLLECTION);
        Database.DropCollection(MongoHistoryPart.COLLECTION);
    }

    public void LoadDataFromCsv(Stream csvStream)
    {
        using StreamReader reader = new(csvStream);
        string? line;
        string currentCollection = "";
        List<BsonDocument> documents = [];

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith('#'))
            {
                // process previous collection if any
                if (documents.Count > 0 &&
                    !string.IsNullOrEmpty(currentCollection))
                {
                    InsertDocuments(currentCollection, documents);
                    documents.Clear();
                }

                // new collection marker
                currentCollection = line.Substring(1);
                continue;
            }

            // parse CSV line into BsonDocument
            BsonDocument? doc = ParseCsvLine(line, currentCollection);
            if (doc != null) documents.Add(doc);
        }

        // insert any remaining documents
        if (documents.Count > 0 && !string.IsNullOrEmpty(currentCollection))
            InsertDocuments(currentCollection, documents);
    }

    private static void PopulatePartDocument(BsonDocument doc, string[] values)
    {
        // based on the CSV structure for parts
        // #parts
        // _id,itemId,typeId,roleId,timeCreated,creatorId,timeModified,userId,content
        doc["_id"] = values[0];
        doc["itemId"] = values[1];
        doc["typeId"] = values[2];

        // roleId might be empty
        if (!string.IsNullOrEmpty(values[3])) doc["roleId"] = values[3];

        doc["timeCreated"] = DateTime.Parse(values[4]).ToUniversalTime();
        doc["creatorId"] = values[5];
        doc["timeModified"] = DateTime.Parse(values[6]).ToUniversalTime();
        doc["userId"] = values[7];

        // content is stored as JSON
        if (values.Length > 8 && !string.IsNullOrEmpty(values[8]))
            doc["content"] = BsonDocument.Parse(values[8]);
        else
            doc["content"] = new BsonDocument();
    }

    private static void PopulateHistoryPartDocument(BsonDocument doc,
        string[] values)
    {
        // first populate the base part fields
        // #history_parts
        // _id,itemId,typeId,roleId,timeCreated,creatorId,timeModified,userId,
        // referenceId,status,content
        PopulatePartDocument(doc, values);

        // then add history-specific fields
        doc["referenceId"] = values[8];
        doc["status"] = int.Parse(values[9]);

        // replace content if provided (since the index is different for
        // history parts)
        if (values.Length > 10 && !string.IsNullOrEmpty(values[10]))
            doc["content"] = BsonDocument.Parse(values[10]);
    }

    private static BsonDocument? ParseCsvLine(string line, string collection)
    {
        // skip header line (contains column names)
        if (line.StartsWith("_id,")) return null;

        string[] values = SplitCsvLine(line);
        if (values.Length < 1) return null;

        BsonDocument doc = [];

        switch (collection)
        {
            case "items":
                PopulateItemDocument(doc, values);
                break;
            case "history_items":
                PopulateHistoryItemDocument(doc, values);
                break;
            case "parts":
                PopulatePartDocument(doc, values);
                break;
            case "history_parts":
                PopulateHistoryPartDocument(doc, values);
                break;
        }

        return doc;
    }

    private static void PopulateItemDocument(BsonDocument doc, string[] values)
    {
        doc["_id"] = values[0];
        doc["title"] = values[1];
        doc["description"] = values[2];
        doc["facetId"] = values[3];
        doc["groupId"] = values[4];
        doc["sortKey"] = values[5];
        doc["flags"] = int.Parse(values[6]);
        doc["timeCreated"] = DateTime.Parse(values[7]).ToUniversalTime();
        doc["creatorId"] = values[8];
        doc["timeModified"] = DateTime.Parse(values[9]).ToUniversalTime();
        doc["userId"] = values[10];
    }

    private static void PopulateHistoryItemDocument(BsonDocument doc,
        string[] values)
    {
        PopulateItemDocument(doc, values);
        doc["referenceId"] = values[11];
        doc["status"] = int.Parse(values[12]);
    }

    private void InsertDocuments(string collectionName,
        List<BsonDocument> documents)
    {
        // map collection names to actual MongoDB collection names
        string actualCollectionName = collectionName switch
        {
            "items" => MongoItem.COLLECTION,
            "history_items" => MongoHistoryItem.COLLECTION,
            "parts" => MongoPart.COLLECTION,
            "history_parts" => MongoHistoryPart.COLLECTION,
            _ => collectionName
        };

        IMongoCollection<BsonDocument> collection = Database
            .GetCollection<BsonDocument>(actualCollectionName);
        collection.InsertMany(documents);
    }

    private static string[] SplitCsvLine(string line)
    {
        // simple CSV parser that handles quoted values
        List<string> values = [];
        bool inQuotes = false;
        StringBuilder currentValue = new();

        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                values.Add(currentValue.ToString());
                currentValue.Clear();
            }
            else currentValue.Append(c);
        }

        values.Add(currentValue.ToString());
        return [.. values];
    }

    public void Dispose()
    {
        ClearDatabase();
        GC.SuppressFinalize(this);
    }
}
