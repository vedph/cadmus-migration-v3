using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Cadmus.Export.Rdf.Test;

public sealed class RdfExporterTest
{
    [Fact]
    public async Task Export_DefaultSettings_Ok()
    {
        TestHelper.SeedData();

        RdfExporter exporter = new(TestHelper.GetConnectionString());
        RamRdfWriter writer = new();
        using MemoryStream ms = new();
        using StreamWriter sw = new(ms, Encoding.UTF8);

        await exporter.ExportAsync(sw);

        Assert.Equal(2, writer.Statistics.LiteralTripleCount);
        // TODO
    }
}
