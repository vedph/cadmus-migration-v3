using System;

namespace Cadmus.Export.Test;

internal class MockItem
{
    public string _id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string FacetId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string SortKey { get; set; } = "";
    public int Flags { get; set; }
    public DateTime TimeCreated { get; set; }
    public string CreatorId { get; set; } = "";
    public DateTime TimeModified { get; set; }
    public string UserId { get; set; } = "";
}
