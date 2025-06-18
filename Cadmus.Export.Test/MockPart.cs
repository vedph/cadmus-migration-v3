using System;

namespace Cadmus.Export.Test;

internal class MockPart
{
    public string _id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string TypeId { get; set; } = "";
    public string? RoleId { get; set; }
    public DateTime TimeCreated { get; set; }
    public string CreatorId { get; set; } = "";
    public DateTime TimeModified { get; set; }
    public string UserId { get; set; } = "";
    public string Content { get; set; } = "{}";
}
