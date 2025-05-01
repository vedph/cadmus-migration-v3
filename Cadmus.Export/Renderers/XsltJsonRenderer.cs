using DevLab.JmesPath;
using Fusi.Tools.Configuration;
using Fusi.Tools.Data;
using Fusi.Xml;
using Fusi.Xml.Extras.Render;
using Newtonsoft.Json;
using Proteus.Core.Text;
using Proteus.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cadmus.Export.Renderers;

/// <summary>
/// XSLT based JSON renderer. This is one of the most customizable renderers,
/// using an optional pipeline of JMESPath transforms to preprocess the
/// JSON input, and an XSLT script to render it once converted to XML.
/// <para>Once transformations have been processed, you also have the option
/// of converting regions of Markdown code in the result (or the whole result)
/// into HTML or plain text.</para>
/// <para>Tag: <c>it.vedph.json-renderer.xslt</c>.</para>
/// </summary>
[Tag("it.vedph.json-renderer.xslt")]
public sealed class XsltJsonRenderer : CadmusJsonRenderer, IJsonRenderer,
    IConfigurable<XsltJsonRendererOptions>
{
    // https://jmespath.org/tutorial.html
    // https://github.com/jdevillard/JmesPath.Net
    // https://www.newtonsoft.com/json/help/html/ConvertingJSONandXML.htm

    private readonly Regex _rootRegex;
    private XsltJsonRendererOptions? _options;
    private XsltTransformer? _transformer;
    private Dictionary<XName, XName>? _wrappedEntryNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="XsltJsonRenderer"/> class.
    /// </summary>
    public XsltJsonRenderer()
    {
        _rootRegex = new Regex(@"^\s*\{\s*""root"":", RegexOptions.Compiled);
    }

    /// <summary>
    /// Configures the object with the specified options.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <exception cref="ArgumentNullException">options</exception>
    public void Configure(XsltJsonRendererOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;

        if (!string.IsNullOrWhiteSpace(options.Xslt))
            _transformer = new XsltTransformer(options.Xslt);
        else
            _transformer = null;

        if (_options.WrappedEntryNames?.Count > 0)
        {
            _wrappedEntryNames = [];
            IXmlNamespaceResolver nsmgr = _options.GetResolver();

            foreach (var p in _options.WrappedEntryNames)
            {
                XName key = NamespaceOptions.ResolvePrefixedName(p.Key,
                    nsmgr,
                    _options.DefaultNsPrefix);

                _wrappedEntryNames[key] =
                    NamespaceOptions.ResolvePrefixedName(p.Value,
                    nsmgr,
                    _options.DefaultNsPrefix);
            }
        }
        else
        {
            _wrappedEntryNames = null;
        }
    }

    /// <summary>
    /// Wraps sequences of 1 or more of the specified XML elements into
    /// a parent element.
    /// </summary>
    /// <param name="doc">The document to edit.</param>
    /// <param name="map">The map between the name of the elements to be
    /// wrapped (keys) and the name of the wrapping element (value).</param>
    /// <exception cref="ArgumentNullException">doc or map</exception>
    public static void WrapXmlArrays(XDocument doc, IDictionary<XName, XName> map)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(map);

        foreach (XName name in map.Keys)
        {
            // first find all sequences of elements with the same name
            List<XElement> firstElements = [..
                doc.Descendants(name).Where(
                e => e.ElementsBeforeSelf().LastOrDefault()?.Name != name)];

            foreach (XElement firstElement in firstElements)
            {
                // get all subsequent siblings with the same name
                List<XElement> siblings =
                [
                    .. firstElement.ElementsAfterSelf()
                               .TakeWhile(e => e.Name == name)
                ];

                // create a list of all elements to be wrapped
                List<XElement> allElements = [firstElement];
                allElements.AddRange(siblings);

                // if there's only one element, just wrap its contents
                if (siblings.Count == 0)
                {
                    // create a wrapper element with the desired name
                    XElement wrapper = new(map[name]);

                    // move the contents to the wrapper
                    foreach (XElement child in firstElement.Elements().ToList())
                    {
                        child.Remove();
                        wrapper.Add(child);
                    }

                    firstElement.Add(wrapper);
                }
                // otherwise, create a container with multiple wrapped elements
                else
                {
                    // remove all elements from their parent
                    XElement parent = firstElement.Parent!;
                    foreach (XElement element in allElements)
                    {
                        element.Remove();
                    }

                    // create a container element with the same name
                    // as the originals
                    XElement containerElement = new(name);

                    // for each original element, create a wrapper and
                    // add the contents
                    foreach (XElement original in allElements)
                    {
                        XElement wrapper = new(map[name]);
                        foreach (XElement child in original.Elements())
                        {
                            wrapper.Add(new XElement(child));
                        }
                        containerElement.Add(wrapper);
                    }

                    // add the container back to the parent at the position
                    // of the first element
                    parent.Add(containerElement);
                }
            }
        }
    }

    /// <summary>
    /// Renders the specified JSON code.
    /// </summary>
    /// <param name="json">The input JSON. This will be automatically wrapped
    /// into a root object with a single <c>root</c> property whose value
    /// is the received JSON code, unless this already has this form.</param>
    /// <param name="context">The optional renderer context.</param>
    /// <returns>Rendered output.</returns>
    /// <param name="tree">The optional text tree. This is used for layer
    /// fragments to get source IDs targeting the various portions of the
    /// text.</param>
    /// <exception cref="ArgumentNullException">json</exception>
    protected override string DoRender(string json,
        CadmusRendererContext context, TreeNode<ExportedSegment>? tree = null)
    {
        if (_options == null) return json;

        ArgumentNullException.ThrowIfNull(json);
        if (!_options.FrDecoration &&
            _transformer == null &&
            _options.JsonExpressions?.Count == 0)
        {
            return "";
        }

        // wrap object properties in root (unless already wrapped)
        if (!_rootRegex.IsMatch(json))
            json = "{\"root\":" + json + "}";

        // decorate if requested
        if (_options.FrDecoration)
            json = JsonDecorator.DecorateLayerPartFrr(json);

        // transform JSON if requested
        if (_options.JsonExpressions?.Count > 0)
        {
            JmesPath jmes = new();
            foreach (string e in _options.JsonExpressions)
            {
                json = jmes.Transform(json, e);
            }
            if (_options.QuoteStripping && json.Length > 1
                && json[0] == '"' && json[^1] == '"')
            {
                json = json[1..^1];
            }
        }

        // transform via XSLT if required
        if (_transformer != null)
        {
            // convert to XML
            XmlDocument? doc = JsonConvert.DeserializeXmlNode(json);
            if (doc is null) return "";

            // wrap array elements if required
            string xml;
            if (_wrappedEntryNames?.Count > 0)
            {
                XDocument xdoc = doc.ToXDocument();
                WrapXmlArrays(xdoc, _wrappedEntryNames);
                xml = xdoc.ToString(SaveOptions.DisableFormatting |
                    SaveOptions.OmitDuplicateNamespaces);
            }
            else
            {
                xml = doc.OuterXml;
            }

            // transform via XSLT
            return _transformer.Transform(xml);
        }

        return json;
    }
}

#region XsltJsonRendererOptions
/// <summary>
/// Options for <see cref="XsltJsonRenderer"/>.
/// </summary>
public class XsltJsonRendererOptions : NamespaceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether fragment decoration
    /// is enabled. When true, the JSON corresponding to a layer part
    /// gets an additional <c>_key</c> property in each of its <c>fragments</c>
    /// array items. This key is built from the layer type ID eventually
    /// followed by <c>|</c> plus the role ID, followed by the fragment's
    /// index.
    /// </summary>
    public bool FrDecoration { get; set; }

    /// <summary>
    /// Gets or sets the JSON transform expressions using JMES Path.
    /// </summary>
    public IList<string> JsonExpressions { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether quotes wrapping a string
    /// result should be removed once JSON transforms have completed.
    /// </summary>
    public bool QuoteStripping { get; set; }

    /// <summary>
    /// Gets or sets the XSLT script used to produce the final result.
    /// </summary>
    public string? Xslt { get; set; }

    /// <summary>
    /// Gets or sets the names of the XML elements representing entries
    /// derived from the conversion of a JSON array. When converting JSON
    /// into XML, any JSON array is converted into a list of entry elements.
    /// So, from a <c>guys</c> array with 3 entries you get 3 elements
    /// named <c>guys</c>. If you want to wrap these elements into an array
    /// parent element, set the name of the entries element as the key of this
    /// dictionary, and the name of the single entry element as the value
    /// of this dictionary (e.g. key=<c>guys</c>, value=<c>guy</c>,
    /// essentially plural=singular). If you need to set a namespace, add
    /// its prefix before colon, like <c>tei:div</c>. These prefixes are
    /// optionally defined in <see cref="NamespaceOptions.Namespaces"/>.
    /// </summary>
    public IDictionary<string, string>? WrappedEntryNames { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="XsltJsonRendererOptions"/> class.
    /// </summary>
    public XsltJsonRendererOptions()
    {
        DefaultNsPrefix = null;
    }
}
#endregion
