using Cadmus.Export.Filters;
using Cadmus.Export.Renderers;
using Cadmus.General.Parts;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Cadmus.Export.Test.Renderers;

public sealed class XsltJsonRendererTest
{
    private readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private static XDocument GetSampleDocument(int count = 3)
    {
        XDocument doc = new(new XElement("root"));
        for (int i = 0; i < count; i++)
        {
            doc.Root!.Add(new XElement("entries",
                new XElement("a", i),
                new XElement("b", i)));
        }
        return doc;
    }

    [Fact]
    public void WrapXmlArrays_Single_Changed()
    {
        Dictionary<XName, XName> map = new()
        {
            ["entries"] = "entry"
        };
        XDocument doc = GetSampleDocument(1);

        XsltJsonRenderer.WrapXmlArrays(doc, map);

        Assert.NotNull(doc.Root!.Element("entries"));
        for (int i = 0; i < 1; i++)
        {
            XElement? entry = doc.Root.Element("entries")!
                .Elements("entry").Skip(i).FirstOrDefault();
            Assert.NotNull(entry);
            Assert.Equal($"<entry><a>{i}</a><b>{i}</b></entry>",
                entry.ToString(SaveOptions.DisableFormatting));
        }
    }

    [Fact]
    public void WrapXmlArrays_Array_Changed()
    {
        Dictionary<XName, XName> map = new()
        {
            ["entries"] = "entry"
        };
        XDocument doc = GetSampleDocument();

        XsltJsonRenderer.WrapXmlArrays(doc, map);

        Assert.NotNull(doc.Root!.Element("entries"));
        for (int i = 0; i < 3; i++)
        {
            XElement? entry = doc.Root.Element("entries")!
                .Elements("entry").Skip(i).FirstOrDefault();
            Assert.NotNull(entry);
            Assert.Equal($"<entry><a>{i}</a><b>{i}</b></entry>",
                entry.ToString(SaveOptions.DisableFormatting));
        }
    }

    [Fact]
    public void WrapXmlArrays_JsonDeserializedFormat_Changed()
    {
        // this mimics the XML structure that comes from JSON deserialization
        XDocument doc = new(
            new XElement("root",
                new XElement("citation", "CIL 1,23"),
                new XElement("lines",
                    new XElement("y", "1"),
                    new XElement("text", "que bixit")),
                new XElement("lines",
                    new XElement("y", "2"),
                    new XElement("text", "annos XX"))
            )
        );

        Dictionary<XName, XName> map = new()
        {
            ["lines"] = "line"
        };

        XsltJsonRenderer.WrapXmlArrays(doc, map);

        // verify the structure transformed correctly
        XElement? linesElement = doc.Root?.Element("lines");
        Assert.NotNull(linesElement);

        // check that we have 2 line elements
        List<XElement> lineElements = [.. linesElement.Elements("line")];
        Assert.Equal(2, lineElements.Count);

        // check content of first line
        XElement firstLine = lineElements[0];
        Assert.Equal("1", firstLine.Element("y")?.Value);
        Assert.Equal("que bixit", firstLine.Element("text")?.Value);

        // check content of second line
        XElement secondLine = lineElements[1];
        Assert.Equal("2", secondLine.Element("y")?.Value);
        Assert.Equal("annos XX", secondLine.Element("text")?.Value);

        // optionally check the full XML string for debugging
        string xml = doc.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("<lines><line><y>1</y><text>que bixit</text>" +
            "</line><line><y>2</y><text>annos XX</text></line></lines>", xml);
    }

    [Fact]
    public void Render_XsltOnly_Ok()
    {
        XsltJsonRenderer renderer = new();
        renderer.Configure(new XsltJsonRendererOptions
        {
            Xslt = TestHelper.LoadResourceText("TokenTextPart.xslt")
        });
        TokenTextPart text = CadmusPreviewerTest.GetSampleTextPart();
        string json = JsonSerializer.Serialize(text, _options);

        string result = renderer.Render(json, new CadmusRendererContext());

        Assert.NotNull(result);
        Assert.Equal("[CIL 1,23]\r\n1  que bixit\r\n2  annos XX\r\n", result);
    }

    [Fact]
    public void Render_XsltOnlyWithArrayWrap_Ok()
    {
        XsltJsonRenderer renderer = new();
        renderer.Configure(new XsltJsonRendererOptions
        {
            Xslt = TestHelper.LoadResourceText("TokenTextPartWrap.xslt"),
            WrappedEntryNames = new Dictionary<string, string>
            {
                ["lines"] = "line"
            }
        });
        TokenTextPart text = CadmusPreviewerTest.GetSampleTextPart();
        string json = JsonSerializer.Serialize(text, _options);

        string result = renderer.Render(json, new CadmusRendererContext());

        Assert.NotNull(result);
        Assert.Equal("[CIL 1,23]\r\n1  que bixit\r\n2  annos XX\r\n", result);
    }

    [Fact]
    public void Render_JmesPathOnly_Ok()
    {
        XsltJsonRenderer renderer = new();
        renderer.Configure(new XsltJsonRendererOptions
        {
            JsonExpressions = ["root.citation"],
            QuoteStripping = true
        });
        TokenTextPart text = CadmusPreviewerTest.GetSampleTextPart();
        string json = JsonSerializer.Serialize(text, _options);

        string result = renderer.Render(json, new CadmusRendererContext());

        Assert.NotNull(result);
        Assert.Equal("CIL 1,23", result);
    }

    [Fact]
    public void Render_JmesPathOnlyMd_Ok()
    {
        XsltJsonRenderer renderer = new();
        renderer.Configure(new XsltJsonRendererOptions
        {
            JsonExpressions = ["root.text"],
            QuoteStripping = true,
        });
        MarkdownTextFilter filter = new();
        filter.Configure(new MarkdownRendererFilterOptions
        {
            Format = "html"
        });
        renderer.Filters.Add(filter);

        NotePart note = new()
        {
            CreatorId = "zeus",
            UserId = "zeus",
            Text = "This is a *note* using MD"
        };
        string json = JsonSerializer.Serialize(note, _options);

        string result = renderer.Render(json, new CadmusRendererContext());

        Assert.NotNull(result);
        Assert.Equal("<p>This is a <em>note</em> using MD</p>\n", result);
    }
}
