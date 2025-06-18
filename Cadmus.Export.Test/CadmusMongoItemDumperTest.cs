using Cadmus.Core.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Cadmus.Export.Test;

public class CadmusMongoItemDumperTest(MongoFixture fixture) :
    IClassFixture<MongoFixture>
{
    private readonly MongoFixture _fixture = fixture;

    private static CadmusMongoItemDumperOptions GetBasicOptions(
        string dbName = "test-db")
    {
        return new CadmusMongoItemDumperOptions
        {
            ConnectionString = "mongodb://localhost:27017/{0}",
            DatabaseName = dbName,
            OutputDirectory = Path.GetTempPath(),
            Indented = true
        };
    }

    private void LoadMockData(string resourceName)
    {
        Assembly assembly = typeof(CadmusMongoItemDumperTest).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(
            $"Cadmus.Export.Test.Assets.{resourceName}")
            ?? throw new ArgumentException($"Resource {resourceName} not found");
        _fixture.ClearDatabase();
        _fixture.LoadDataFromCsv(stream);
    }

    [Fact]
    public void GetItems_FullDump_ReturnsAllItemsWithCorrectStatus()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        Assert.Equal(4, items.Count); // 3 active items + 1 deleted item

        MongoDB.Bson.BsonDocument item1 = items.First(
            i => i["_id"].AsString == "item1");
        MongoDB.Bson.BsonDocument item2 = items.First(
            i => i["_id"].AsString == "item2");
        MongoDB.Bson.BsonDocument item3 = items.First(
            i => i["_id"].AsString == "item3");
        MongoDB.Bson.BsonDocument item4 = items.First(
            i => i["_id"].AsString == "item4");

        // timeCreated == timeModified
        Assert.Equal(EditStatus.Created, (EditStatus)item1["_status"].AsInt32);
        // timeCreated != timeModified
        Assert.Equal(EditStatus.Updated, (EditStatus)item2["_status"].AsInt32);
        // timeCreated == timeModified
        Assert.Equal(EditStatus.Created, (EditStatus)item3["_status"].AsInt32);
        // from history with status=2
        Assert.Equal(EditStatus.Deleted, (EditStatus)item4["_status"].AsInt32);

        // check parts were added to non-deleted items
        Assert.True(item1.Contains("_parts"));
        Assert.Single(item1["_parts"].AsBsonArray);
        Assert.True(item2.Contains("_parts"));
        Assert.Equal(2, item2["_parts"].AsBsonArray.Count);
        Assert.True(item3.Contains("_parts"));
        Assert.Single(item3["_parts"].AsBsonArray);
        Assert.False(item4.Contains("_parts")); // deleted item has no parts
    }

    [Fact]
    public void GetItems_NoParts_ReturnsItemsWithoutParts()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.NoParts = true;
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        Assert.Equal(4, items.Count);

        // verify no parts were added
        foreach (MongoDB.Bson.BsonDocument? item in items)
            Assert.False(item.Contains("_parts"));
    }

    [Fact]
    public void GetItems_NoDeleted_ExcludesDeletedItems()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.NoDeleted = true;
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item4");
    }

    [Fact]
    public void GetItems_FacetFilter_ReturnsMatchingItems()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.Filter = new ItemFilter { FacetId = "default" };
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        // 2 active items + 1 deleted item with facetId=default
        Assert.Equal(3, items.Count);
        // item3 has facetId=special
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item3");
    }

    [Fact]
    public void GetItems_TimeFilter_ReturnsItemsInTimeframe()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.Filter = new ItemFilter
        {
            MinModified = new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            MaxModified = new DateTime(2023, 5, 2, 0, 0, 0, DateTimeKind.Utc)
        };
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        Assert.Equal(3, items.Count);

        // check items were correctly included
        // modified on March 1
        Assert.Contains(items, i => i["_id"].AsString == "item2");
        // modified on April 1
        Assert.Contains(items, i => i["_id"].AsString == "item3");
        // deleted on May 1
        Assert.Contains(items, i => i["_id"].AsString == "item4");

        // check statuses are correct for incremental dump
        MongoDB.Bson.BsonDocument item2 = items.First(
            i => i["_id"].AsString == "item2");
        MongoDB.Bson.BsonDocument item3 = items.First(
            i => i["_id"].AsString == "item3");
        MongoDB.Bson.BsonDocument item4 = items.First(
            i => i["_id"].AsString == "item4");

        Assert.Equal(EditStatus.Updated, (EditStatus)item2["_status"].AsInt32);
        // created within timeframe
        Assert.Equal(EditStatus.Created, (EditStatus)item3["_status"].AsInt32);
        Assert.Equal(EditStatus.Deleted, (EditStatus)item4["_status"].AsInt32);
    }

    [Fact]
    public void GetItems_PartTimeFilter_ReturnsItemsWithMatchingParts()
    {
        LoadMockData("IncrementalDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.Filter = new ItemFilter
        {
            MinModified = new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        Assert.Equal(4, items.Count);

        // item2 should be included because one of its parts was modified on May 10
        Assert.Contains(items, i => i["_id"].AsString == "item2");
        // item4 should be included because it was deleted on May 1
        Assert.Contains(items, i => i["_id"].AsString == "item4");
        // item5 should be included because it was created on May 15
        Assert.Contains(items, i => i["_id"].AsString == "item5");

        // check part statuses
        MongoDB.Bson.BsonDocument item2 = items.First(
            i => i["_id"].AsString == "item2");
        MongoDB.Bson.BsonDocument item5 = items.First(
            i => i["_id"].AsString == "item5");

        MongoDB.Bson.BsonValue? part3 = item2["_parts"].AsBsonArray
            .FirstOrDefault(p => p["_id"].AsString == "part3");
        Assert.NotNull(part3);
        Assert.Equal(EditStatus.Updated, (EditStatus)part3["_status"].AsInt32);

        MongoDB.Bson.BsonValue? part6 = item5["_parts"].AsBsonArray
            .FirstOrDefault(p => p["_id"].AsString == "part6");
        Assert.NotNull(part6);
        Assert.Equal(EditStatus.Created, (EditStatus)part6["_status"].AsInt32);
    }

    [Fact]
    public void GetItems_NoPartDate_ExcludesItemsWithMatchingPartsOnly()
    {
        LoadMockData("IncrementalDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.Filter = new ItemFilter
        {
            MinModified = new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        options.NoPartDate = true;
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        Assert.Equal(3, items.Count);

        // item2 should now be excluded because the item itself wasn't modified
        // in the timeframe
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item2");

        // item4 and item5 should still be included
        Assert.Contains(items, i => i["_id"].AsString == "item4");
        Assert.Contains(items, i => i["_id"].AsString == "item5");
    }

    [Fact]
    public void GetItems_WhitelistPartTypes_FiltersPartsByType()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.WhitePartTypeKeys = ["token"];
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        foreach (MongoDB.Bson.BsonDocument? item in items.Where(
            i => i.Contains("_parts")))
        {
            MongoDB.Bson.BsonArray parts = item["_parts"].AsBsonArray;
            Assert.All(parts, p => Assert.Equal("token", p["typeId"].AsString));
        }
    }

    [Fact]
    public void GetItems_BlacklistPartTypes_ExcludesPartsByType()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.BlackPartTypeKeys = ["token"];
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        foreach (MongoDB.Bson.BsonDocument? item in items
            .Where(i => i.Contains("_parts")))
        {
            MongoDB.Bson.BsonArray parts = item["_parts"].AsBsonArray;
            Assert.All(parts, p => Assert.NotEqual("token", p["typeId"].AsString));
        }
    }

    [Fact]
    public void GetItems_RoleFilter_FiltersPartsByRole()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.WhitePartTypeKeys = ["token:sample"];
        CadmusMongoItemDumper dumper = new(options);

        List<MongoDB.Bson.BsonDocument> items = [.. dumper.GetItems()];

        // only item2 should have parts (part3 with typeId=token and
        // roleId=sample)
        List<MongoDB.Bson.BsonDocument> itemsWithParts =
            [.. items.Where(i => i.Contains("_parts") &&
                                 i["_parts"].AsBsonArray.Count > 0)];
        Assert.Single(itemsWithParts);
        Assert.Equal("item2", itemsWithParts[0]["_id"].AsString);

        MongoDB.Bson.BsonArray parts = itemsWithParts[0]["_parts"].AsBsonArray;
        Assert.Single(parts);
        Assert.Equal("token", parts[0]["typeId"].AsString);
        Assert.Equal("sample", parts[0]["roleId"].AsString);
    }
}
