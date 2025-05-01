using Cadmus.Core.Config;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Cadmus.Import.Test;

public sealed class CsvThesaurusReaderTest
{
    private static Stream GetStream(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Read_TwoThesauri_Ok()
    {
        string text = "thesaurusId,id,value,targetId\r\n" +
            "colors@en,r,red,\r\n" +
            "colors@en,g,green,\r\n" +
            "colors@en,b,blue,\r\n" +
            "shapes@en,trg,triangle,\r\n" +
            "shapes@en,rct,rectangle,\r\n";
        CsvThesaurusReader reader = new(GetStream(text));

        // colors thesaurus
        Thesaurus? thesaurus = reader.Next();
        Assert.NotNull(thesaurus);
        Assert.Equal("colors@en", thesaurus!.Id);
        Assert.Null(thesaurus.TargetId);
        Assert.Equal(3, thesaurus.Entries.Count);
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "r" && e.Value == "red"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "g" && e.Value == "green"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "b" && e.Value == "blue"));

        // shapes thesaurus
        thesaurus = reader.Next();
        Assert.NotNull(thesaurus);
        Assert.Equal("shapes@en", thesaurus!.Id);
        Assert.Null(thesaurus.TargetId);
        Assert.Equal(2, thesaurus.Entries.Count);
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "trg" && e.Value == "triangle"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "rct" && e.Value == "rectangle"));
    }

    [Fact]
    public void Read_TwoThesauriWithImplicitId_Ok()
    {
        string text = "thesaurusId,id,value,targetId\r\n" +
            "colors@en,r,red,\r\n" +
            ",g,green,\r\n" +
            ",b,blue,\r\n" +
            "shapes@en,trg,triangle,\r\n" +
            ",rct,rectangle,\r\n";
        CsvThesaurusReader reader = new(GetStream(text));

        // colors thesaurus
        Thesaurus? thesaurus = reader.Next();
        Assert.NotNull(thesaurus);
        Assert.Equal("colors@en", thesaurus!.Id);
        Assert.Null(thesaurus.TargetId);
        Assert.Equal(3, thesaurus.Entries.Count);
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "r" && e.Value == "red"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "g" && e.Value == "green"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "b" && e.Value == "blue"));

        // shapes thesaurus
        thesaurus = reader.Next();
        Assert.NotNull(thesaurus);
        Assert.Equal("shapes@en", thesaurus!.Id);
        Assert.Null(thesaurus.TargetId);
        Assert.Equal(2, thesaurus.Entries.Count);
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "trg" && e.Value == "triangle"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "rct" && e.Value == "rectangle"));
    }

    [Fact]
    public void Read_ThesaurusAndAlias_Ok()
    {
        string text = "thesaurusId,id,value,targetId\r\n" +
            "colors@en,r,red,\r\n" +
            "colors@en,g,green,\r\n" +
            "colors@en,b,blue,\r\n" +
            "colours@en,,,colors\r\n";
        CsvThesaurusReader reader = new(GetStream(text));

        // colors thesaurus
        Thesaurus? thesaurus = reader.Next();
        Assert.NotNull(thesaurus);
        Assert.Equal("colors@en", thesaurus!.Id);
        Assert.Null(thesaurus.TargetId);
        Assert.Equal(3, thesaurus.Entries.Count);
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "r" && e.Value == "red"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "g" && e.Value == "green"));
        Assert.NotNull(thesaurus.Entries
            .FirstOrDefault(e => e.Id == "b" && e.Value == "blue"));

        // alias thesaurus
        thesaurus = reader.Next();
        Assert.NotNull(thesaurus);
        Assert.Equal("colours@en", thesaurus!.Id);
        Assert.Equal("colors", thesaurus.TargetId);
        Assert.Empty(thesaurus.Entries);
    }
}
