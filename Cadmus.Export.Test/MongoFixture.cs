using Cadmus.Mongo;
using CsvHelper;
using CsvHelper.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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

    private void ProcessRecords(string collectionName, List<string[]> rawLines)
    {
        // first line should be the header
        if (rawLines.Count < 2) return;

        // join all lines to create a valid CSV
        string headerLine = rawLines[0][0];
        List<string> contentLines = [.. rawLines.Skip(1).Select(l => l[0])];
        string csvContent = $"{headerLine}\n{string.Join("\n", contentLines)}";

        // create in-memory stream
        using MemoryStream memoryStream = new(Encoding.UTF8.GetBytes(csvContent));
        using StreamReader reader = new(memoryStream);
        using CsvReader csv = new(reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                PrepareHeaderForMatch = args => args.Header.ToLower()
            });

        // read records
        List<dynamic> records = [.. csv.GetRecords<dynamic>()];

        // convert to BsonDocuments
        List<BsonDocument> documents = [];
        foreach (dynamic? record in records)
        {
            BsonDocument doc = [];

            // convert dynamic record to dictionary
            IDictionary<string, object> recordDict =
                (IDictionary<string, object>)record;

            switch (collectionName)
            {
                case "items":
                    PopulateItemDocument(doc, recordDict);
                    break;
                case "history_items":
                    PopulateHistoryItemDocument(doc, recordDict);
                    break;
                case "parts":
                    PopulatePartDocument(doc, recordDict);
                    break;
                case "history_parts":
                    PopulateHistoryPartDocument(doc, recordDict);
                    break;
            }

            documents.Add(doc);
        }

        // insert the documents
        if (documents.Count > 0) InsertDocuments(collectionName, documents);
    }

    public void LoadDataFromCsv(Stream csvStream)
    {
        using StreamReader reader = new(csvStream);
        string? line;
        string currentCollection = "";
        List<string[]> records = [];

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith('#'))
            {
                // process previous collection if any
                if (records.Count > 0 && !string.IsNullOrEmpty(currentCollection))
                {
                    ProcessRecords(currentCollection, records);
                    records.Clear();
                }

                // new collection marker
                currentCollection = line[1..];
                continue;
            }

            // add line to be processed using CsvHelper
            records.Add([line]);
        }

        // process any remaining records
        if (records.Count > 0 && !string.IsNullOrEmpty(currentCollection))
            ProcessRecords(currentCollection, records);
    }

    private static void ReadJsonContent(IDictionary<string, object> record,
        BsonDocument doc)
    {
        if (record.TryGetValue("content", out object? value) &&
            !string.IsNullOrEmpty(value?.ToString()))
        {
            try
            {
                // ensure the JSON is valid by parsing it first
                doc["content"] = BsonDocument.Parse(value.ToString()!);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing JSON content: {ex.Message}");
                Debug.WriteLine($"Content value: '{value}'");

                // if parsing fails, use an empty document
                doc["content"] = new BsonDocument();
            }
        }
        else
        {
            doc["content"] = new BsonDocument();
        }
    }

    private static void PopulatePartDocument(BsonDocument doc,
        IDictionary<string, object> record)
    {
        // based on the CSV structure for parts
        // _id,itemId,typeId,roleId,timeCreated,creatorId,timeModified,userId,content
        doc["_id"] = record["_id"].ToString();
        doc["itemId"] = record["itemid"].ToString();
        doc["typeId"] = record["typeid"].ToString();

        // roleId might be empty
        if (record.TryGetValue("roleid", out object? value) &&
            !string.IsNullOrEmpty(value?.ToString()))
        {
            doc["roleId"] = value.ToString();
        }

        doc["timeCreated"] = DateTime.Parse(record["timecreated"].ToString()!)
            .ToUniversalTime();
        doc["creatorId"] = record["creatorid"].ToString();
        doc["timeModified"] = DateTime.Parse(record["timemodified"].ToString()!)
            .ToUniversalTime();
        doc["userId"] = record["userid"].ToString();

        // content is stored as JSON
        ReadJsonContent(record, doc);
    }

    private static void PopulateHistoryPartDocument(BsonDocument doc,
        IDictionary<string, object> record)
    {
        // first populate the base part fields
        PopulatePartDocument(doc, record);

        // then add history-specific fields
        doc["referenceId"] = record["referenceid"].ToString();
        doc["status"] = int.Parse(record["status"].ToString()!);

        // replace content if provided (since we might have already set it
        // from PopulatePartDocument)
        if (record.TryGetValue("content", out _))
            ReadJsonContent(record, doc);
    }

    private static void PopulateItemDocument(BsonDocument doc,
        IDictionary<string, object> record)
    {
        doc["_id"] = record["_id"].ToString();
        doc["title"] = record["title"].ToString();
        doc["description"] = record["description"].ToString();
        doc["facetId"] = record["facetid"].ToString();
        doc["groupId"] = record["groupid"].ToString();
        doc["sortKey"] = record["sortkey"].ToString();
        doc["flags"] = int.Parse(record["flags"].ToString()!);
        doc["timeCreated"] = DateTime.Parse(record["timecreated"].ToString()!)
            .ToUniversalTime();
        doc["creatorId"] = record["creatorid"].ToString();
        doc["timeModified"] = DateTime.Parse(record["timemodified"].ToString()!)
            .ToUniversalTime();
        doc["userId"] = record["userid"].ToString();
    }

    private static void PopulateHistoryItemDocument(BsonDocument doc,
        IDictionary<string, object> record)
    {
        PopulateItemDocument(doc, record);
        doc["referenceId"] = record["referenceid"].ToString();
        doc["status"] = int.Parse(record["status"].ToString()!);
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

    public void Dispose()
    {
        ClearDatabase();
        GC.SuppressFinalize(this);
    }
}
