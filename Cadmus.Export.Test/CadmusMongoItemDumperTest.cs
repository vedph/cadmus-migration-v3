using Cadmus.Core.Storage;
using MongoDB.Bson;
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
    public void GetItems_FullDump_ReturnsAllItems()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        // Create an empty filter for full dump
        CadmusDumpFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 0  // no paging by default
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        Assert.Equal(4, items.Count); // should return all 4 items from history

        // verify we got the latest version of each item
        BsonDocument item1 = items.First(i => i["_id"].AsString == "item1");
        BsonDocument item2 = items.First(i => i["_id"].AsString == "item2");
        BsonDocument item3 = items.First(i => i["_id"].AsString == "item3");
        BsonDocument item4 = items.First(i => i["_id"].AsString == "item4");

        // check that statuses are correct
        Assert.Equal(EditStatus.Created, (EditStatus)item1["_status"].AsInt32);
        Assert.Equal(EditStatus.Updated, (EditStatus)item2["_status"].AsInt32);
        Assert.Equal(EditStatus.Created, (EditStatus)item3["_status"].AsInt32);
        Assert.Equal(EditStatus.Deleted, (EditStatus)item4["_status"].AsInt32);

        // check parts were correctly added
        Assert.True(item1.Contains("_parts"));
        Assert.Single(item1["_parts"].AsBsonArray);

        Assert.True(item2.Contains("_parts"));
        Assert.Equal(2, item2["_parts"].AsBsonArray.Count);

        Assert.True(item3.Contains("_parts"));
        Assert.Single(item3["_parts"].AsBsonArray);

        // deleted items should also have parts if they exist in history_parts
        Assert.True(item4.Contains("_parts"));
        Assert.Single(item4["_parts"].AsBsonArray);
    }

    [Fact]
    public void GetItems_NoParts_ReturnsItemsWithoutParts()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.NoParts = true;
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 0
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        Assert.Equal(4, items.Count);

        // verify no parts were added
        foreach (BsonDocument item in items)
            Assert.False(item.Contains("_parts"));
    }

    [Fact]
    public void GetItems_NoDeleted_ExcludesDeletedItems()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.NoDeleted = true;
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 0
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item4");
    }

    [Fact]
    public void GetItems_FacetFilter_ReturnsMatchingItems()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            FacetId = "default",
            PageNumber = 1,
            PageSize = 0
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        // 2 active items + 1 deleted item with facetId=default
        Assert.Equal(3, items.Count);

        // item3 has facetId=special
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item3");
    }

    [Fact]
    public void GetItems_TimeframeFilter_ReturnsItemsUpToTimeframe()
    {
        LoadMockData("BasicDataset.csv");

        // Set timeframe to March 1, 2023 - this should include:
        // - item1 (created January 1)
        // - item2 (modified March 1)
        // But exclude:
        // - item3 (created April 1)
        // - item4 (deleted May 1)
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            MaxModified = new DateTime(2023, 3, 1, 23, 59, 59, DateTimeKind.Utc),
            PageNumber = 1,
            PageSize = 0
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i["_id"].AsString == "item1");
        Assert.Contains(items, i => i["_id"].AsString == "item2");
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item3");
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item4");

        // check that statuses are correct
        BsonDocument item1 = items.First(i => i["_id"].AsString == "item1");
        BsonDocument item2 = items.First(i => i["_id"].AsString == "item2");

        Assert.Equal(EditStatus.Created, (EditStatus)item1["_status"].AsInt32);
        Assert.Equal(EditStatus.Updated, (EditStatus)item2["_status"].AsInt32);

        // check part content - for item2, we should get the version as of March 1
        BsonArray item2Parts = item2["_parts"].AsBsonArray;
        BsonValue? part3 = item2Parts.FirstOrDefault(
            p => p["_id"].AsString == "part3");
        Assert.NotNull(part3);

        // in our test data, part3 was updated multiple times:
        // we should get the version that was current as of March 1
        Assert.Equal("content3", part3["content"]["value"].AsString);
    }

    [Fact]
    public void GetItems_IncrementalTimeframeFilter_ReturnsItemsChangedInTimeframe()
    {
        LoadMockData("IncrementalDataset.csv");

        // get items changed between April 15 and May 20, 2023.
        // This should include:
        // - item4 (deleted on May 1)
        // - item5 (created on May 15)
        // - item2 (has part3 updated on May 10)
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            MinModified = new DateTime(2023, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            MaxModified = new DateTime(2023, 5, 20, 23, 59, 59, DateTimeKind.Utc),
            PageNumber = 1,
            PageSize = 0
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        Assert.Equal(3, items.Count);
        // deleted May 1
        Assert.Contains(items, i => i["_id"].AsString == "item4");
        // created May 15
        Assert.Contains(items, i => i["_id"].AsString == "item5");
        // has part updated May 10
        Assert.Contains(items, i => i["_id"].AsString == "item2");

        // check that part3 of item2 has the latest content as of May 20
        BsonDocument item2 = items.First(i => i["_id"].AsString == "item2");
        BsonArray parts = item2["_parts"].AsBsonArray;
        BsonValue? part3 = parts.FirstOrDefault(p => p["_id"].AsString == "part3");

        Assert.NotNull(part3);
        Assert.Equal("updated content3", part3["content"]["value"].AsString);
    }

    [Fact]
    public void GetItems_NoPartDate_ExcludesItemsWithMatchingPartsOnly()
    {
        LoadMockData("IncrementalDataset.csv");

        // Get items changed between April 15 and May 20, 2023.
        // With NoPartDate=true, item2 should be excluded because the item itself
        // wasn't modified in the timeframe, only its part was
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        options.NoPartDate = true;
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            MinModified = new DateTime(2023, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            MaxModified = new DateTime(2023, 5, 20, 23, 59, 59, DateTimeKind.Utc),
            PageNumber = 1,
            PageSize = 0
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        Assert.Equal(2, items.Count);
        // deleted May 1
        Assert.Contains(items, i => i["_id"].AsString == "item4");
        // created May 15
        Assert.Contains(items, i => i["_id"].AsString == "item5");
        // only part updated, not item
        Assert.DoesNotContain(items, i => i["_id"].AsString == "item2");
    }

    [Fact]
    public void GetItems_WhitelistPartTypes_FiltersPartsByType()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 0,
            WhitePartTypeKeys = ["token"]
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        foreach (BsonDocument? item in items.Where(i => i.Contains("_parts")
            && i["_parts"].AsBsonArray.Count > 0))
        {
            BsonArray parts = item["_parts"].AsBsonArray;
            Assert.All(parts, p => Assert.Equal("token", p["typeId"].AsString));
        }
    }

    [Fact]
    public void GetItems_BlacklistPartTypes_ExcludesPartsByType()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 0,
            BlackPartTypeKeys = ["token"]
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        foreach (BsonDocument? item in items.Where(i => i.Contains("_parts")
            && i["_parts"].AsBsonArray.Count > 0))
        {
            BsonArray parts = item["_parts"].AsBsonArray;
            Assert.All(parts, p => Assert.NotEqual("token", p["typeId"].AsString));
        }
    }

    [Fact]
    public void GetItems_RoleFilter_FiltersPartsByRole()
    {
        LoadMockData("BasicDataset.csv");
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        CadmusDumpFilter filter = new()
        {
            PageNumber = 1,
            PageSize = 0,
            WhitePartTypeKeys = ["token:sample"]
        };

        List<BsonDocument> items = [.. dumper.GetItems(filter)];

        // find all items that have parts
        List<BsonDocument> itemsWithParts = [..
            items.Where(i => i.Contains("_parts") &&
                             i["_parts"].AsBsonArray.Count > 0)];

        // check if any item has a part with typeId=token and roleId=sample
        bool anyMatchingPart = false;
        foreach (BsonDocument? item in itemsWithParts)
        {
            BsonArray parts = item["_parts"].AsBsonArray;
            foreach (BsonValue part in parts)
            {
                if (part["typeId"].AsString == "token" &&
                    part.AsBsonDocument.Contains("roleId") &&
                    part["roleId"].AsString == "sample")
                {
                    anyMatchingPart = true;
                }
            }
        }

        Assert.True(anyMatchingPart);
    }

    [Fact]
    public void GetItems_Pagination_ReturnsCorrectPage()
    {
        LoadMockData("BasicDataset.csv");

        // create dumper
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        // configure to return 2 items per page
        CadmusDumpFilter page1Filter = new()
        {
            PageNumber = 1,
            PageSize = 2
        };

        // get page 1
        List<BsonDocument> page1Items = [.. dumper.GetItems(page1Filter)];

        // get page 2
        CadmusDumpFilter page2Filter = new()
        {
            PageNumber = 2,
            PageSize = 2
        };
        List<BsonDocument> page2Items = [.. dumper.GetItems(page2Filter)];

        Assert.Equal(2, page1Items.Count);
        Assert.Equal(2, page2Items.Count);

        // make sure we got different items on different pages
        List<string> allItemIds = page1Items.Select(i => i["_id"].AsString)
            .Concat(page2Items.Select(i => i["_id"].AsString))
            .ToList();

        Assert.Equal(4, allItemIds.Distinct().Count());
    }

    [Fact]
    public void GetItems_ReuseDumper_WorksWithMultipleCalls()
    {
        LoadMockData("BasicDataset.csv");

        // create dumper with basic options
        CadmusMongoItemDumperOptions options = GetBasicOptions();
        CadmusMongoItemDumper dumper = new(options);

        // first call - get all items
        CadmusDumpFilter allItemsFilter = new()
        {
            PageNumber = 1,
            PageSize = 0
        };
        List<BsonDocument> allItems = [.. dumper.GetItems(allItemsFilter)];

        // second call - get only default facet items
        CadmusDumpFilter defaultFacetFilter = new()
        {
            FacetId = "default",
            PageNumber = 1,
            PageSize = 0
        };
        List<BsonDocument> defaultFacetItems =
            [.. dumper.GetItems(defaultFacetFilter)];

        // Third call - get only special facet items
        CadmusDumpFilter specialFacetFilter = new()
        {
            FacetId = "special",
            PageNumber = 1,
            PageSize = 0
        };
        List<BsonDocument> specialFacetItems =
            [.. dumper.GetItems(specialFacetFilter)];

        Assert.Equal(4, allItems.Count);
        Assert.Equal(3, defaultFacetItems.Count);
        Assert.Single(specialFacetItems);

        // verify we get correct items for each filter
        Assert.Contains(defaultFacetItems, i => i["_id"].AsString == "item1");
        Assert.Contains(defaultFacetItems, i => i["_id"].AsString == "item2");
        Assert.Contains(defaultFacetItems, i => i["_id"].AsString == "item4");
        Assert.DoesNotContain(defaultFacetItems, i => i["_id"].AsString == "item3");

        Assert.Contains(specialFacetItems, i => i["_id"].AsString == "item3");
    }
}
