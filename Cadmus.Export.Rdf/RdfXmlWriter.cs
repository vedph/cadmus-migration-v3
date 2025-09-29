using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cadmus.Export.Rdf;

/// <summary>
/// RDF/XML format RDF writer.
/// </summary>
public sealed class RdfXmlWriter : RdfWriter
{
    /// <summary>
    /// Creates a new RDF/XML writer.
    /// </summary>
    /// <param name="settings">The RDF export settings.</param>
    /// <param name="prefixMappings">The prefix mappings.</param>
    /// <param name="uriMappings">The URI mappings.</param>
    public RdfXmlWriter(RdfExportSettings settings,
        Dictionary<string, string> prefixMappings,
        Dictionary<int, string> uriMappings)
        : base(settings, prefixMappings, uriMappings)
    {
    }

    private static string XmlEscape(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Write the header, including comments and namespace declarations.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <exception cref="ArgumentNullException">writer</exception>"
    public override async Task WriteHeaderAsync(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        await writer.WriteLineAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");

        if (_settings.IncludeComments)
        {
            await writer.WriteLineAsync("<!-- RDF data exported from Cadmus " +
                "Graph database -->");
            await writer.WriteLineAsync(
                $"<!-- Export date: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} -->");
        }

        // build namespace declarations
        StringBuilder rdfTag = new("<rdf:RDF");
        rdfTag.Append(" xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"");

        foreach (KeyValuePair<string, string> mapping in _prefixMappings)
        {
            if (mapping.Key != "rdf") // rdf is already declared
            {
                rdfTag.Append($" xmlns:{mapping.Key}=\"{XmlEscape(mapping.Value)}\"");
            }
        }

        if (!string.IsNullOrEmpty(_settings.BaseUri))
        {
            rdfTag.Append($" xml:base=\"{XmlEscape(_settings.BaseUri)}\"");
        }

        rdfTag.Append('>');

        if (_settings.PrettyPrint)
            await writer.WriteLineAsync(rdfTag.ToString());
        else
            await writer.WriteAsync(rdfTag.ToString());
    }

    /// <summary>
    /// Writes the given triples in RDF/XML format.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="triples">The triples.</param>
    /// <exception cref="ArgumentNullException">writer or triples</exception>"
    public override async Task WriteAsync(TextWriter writer,
        List<RdfTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(triples);

        // group triples by subject for better RDF/XML structure
        IEnumerable<IGrouping<int, RdfTriple>> groupedTriples =
            triples.GroupBy(t => t.SubjectId);

        foreach (IGrouping<int, RdfTriple> subjectGroup in groupedTriples)
        {
            string subjectUri = GetFullUri(subjectGroup.Key);

            if (_settings.PrettyPrint)
            {
                await writer.WriteLineAsync(
                    $"  <rdf:Description rdf:about=\"{XmlEscape(subjectUri)}\">");
            }
            else
            {
                await writer.WriteAsync(
                    $"<rdf:Description rdf:about=\"{XmlEscape(subjectUri)}\">");
            }

            foreach (RdfTriple triple in subjectGroup)
            {
                string predicateUri = GetUriForId(triple.PredicateId);
                string predicateFullUri = GetFullUri(triple.PredicateId);

                // try to use prefixed form
                string predicateTag;
                if (predicateUri.Contains(':') && !predicateUri.StartsWith("http"))
                {
                    // already in prefix:local format
                    predicateTag = predicateUri.Replace(':', ':');
                }
                else
                {
                    // use full URI in rdf:resource format
                    predicateTag = $"rdf:predicate_{triple.PredicateId}";
                }

                string indent = _settings.PrettyPrint ? "    " : "";

                if (!string.IsNullOrEmpty(triple.ObjectLiteral))
                {
                    // literal value
                    StringBuilder literalTag = new($"{indent}<{predicateTag}");

                    if (!string.IsNullOrEmpty(triple.ObjectLiteralLanguage))
                    {
                        literalTag.Append(
                            $" xml:lang=\"{XmlEscape(triple.ObjectLiteralLanguage)}\"");
                    }
                    else if (!string.IsNullOrEmpty(triple.ObjectLiteralType))
                    {
                        string datatype = triple.ObjectLiteralType.Contains(':') &&
                            !triple.ObjectLiteralType.StartsWith("http")
                            ? UriHelper.ExpandUri(triple.ObjectLiteralType, _prefixMappings)
                            : triple.ObjectLiteralType;
                        literalTag.Append($" rdf:datatype=\"{XmlEscape(datatype)}\"");
                    }

                    literalTag.Append($">{XmlEscape(triple.ObjectLiteral)}</{predicateTag}>");

                    if (_settings.PrettyPrint)
                        await writer.WriteLineAsync(literalTag.ToString());
                    else
                        await writer.WriteAsync(literalTag.ToString());
                }
                else if (triple.ObjectId.HasValue)
                {
                    // resource reference
                    string objectUri = GetFullUri(triple.ObjectId.Value);
                    string resourceTag =
                        $"{indent}<{predicateTag} rdf:resource=\"{XmlEscape(objectUri)}\"/>";

                    if (_settings.PrettyPrint)
                        await writer.WriteLineAsync(resourceTag);
                    else
                        await writer.WriteAsync(resourceTag);
                }
            }

            if (_settings.PrettyPrint)
                await writer.WriteLineAsync("  </rdf:Description>");
            else
                await writer.WriteAsync("</rdf:Description>");
        }
    }

    /// <summary>
    /// Write the footer.
    /// </summary>
    /// <param name="writer"></param>
    /// <exception cref="ArgumentNullException">writer</exception>
    public override async Task WriteFooterAsync(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (_settings.PrettyPrint)
            await writer.WriteLineAsync("</rdf:RDF>");
        else
            await writer.WriteAsync("</rdf:RDF>");
    }
}
