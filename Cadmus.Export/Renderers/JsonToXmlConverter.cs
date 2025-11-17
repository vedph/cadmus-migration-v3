using Proteus.Core.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Cadmus.Export.Renderers;

/// <summary>
/// JSON to XML converter for XSLT processing. This converter transforms
/// JSON documents into XML with special handling for arrays and namespaces,
/// making the output suitable for XSLT transformations.
/// </summary>
public sealed class JsonToXmlConverter
{
    private readonly EnglishSingularizer _singularizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonToXmlConverter"/>
    /// class.
    /// </summary>
    public JsonToXmlConverter()
    {
        _singularizer = new EnglishSingularizer();
    }

    /// <summary>
    /// Converts the specified JSON string into an XML element.
    /// </summary>
    /// <param name="json">The JSON string to convert.</param>
    /// <param name="options">The optional conversion options.</param>
    /// <returns>The root XML element.</returns>
    /// <exception cref="ArgumentNullException">json</exception>
    /// <exception cref="JsonException">Invalid JSON format.</exception>
    public XElement Convert(string json,
        JsonToXmlConverterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        options ??= new JsonToXmlConverterOptions();

        // Parse JSON
        JsonDocument doc = JsonDocument.Parse(json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        // Build XML tree
        XName rootName = options.ResolvePrefixedName(
            options.RootElementName);
        XElement root = new(rootName);

        ProcessJsonElement(doc.RootElement, root, options);

        // Wrap arrays if needed
        if (options.WrappedEntryNames?.Count > 0)
        {
            Dictionary<XName, XName> wrappedMap =
                BuildWrappedEntryNamesMap(options);
            WrapXmlArrays(root, wrappedMap, options);
        }

        return root;
    }

    private static Dictionary<XName, XName> BuildWrappedEntryNamesMap(
        JsonToXmlConverterOptions options)
    {
        Dictionary<XName, XName> map = [];
        IXmlNamespaceResolver resolver = options.GetResolver();

        foreach (KeyValuePair<string, string> pair in
            options.WrappedEntryNames!)
        {
            XName key = NamespaceOptions.ResolvePrefixedName(
                pair.Key, resolver, options.DefaultNsPrefix);
            XName value = NamespaceOptions.ResolvePrefixedName(
                pair.Value, resolver, options.DefaultNsPrefix);

            map[key] = value;
        }

        return map;
    }

    private void ProcessJsonElement(JsonElement element,
        XElement parent, JsonToXmlConverterOptions options)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                ProcessJsonObject(element, parent, options);
                break;
            case JsonValueKind.Array:
                ProcessJsonArray(element, parent, options,
                    parent.Name.LocalName);
                break;
            default:
                ProcessJsonValue(element, parent);
                break;
        }
    }

    private void ProcessJsonObject(JsonElement element,
        XElement parent, JsonToXmlConverterOptions options)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (ShouldSkipProperty(property, options))
                continue;

            XName childName = XName.Get(property.Name);
            XElement child = new(childName);

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                ProcessJsonArray(property.Value, parent, options,
                    property.Name);
            }
            else
            {
                ProcessJsonElement(property.Value, child, options);
                parent.Add(child);
            }
        }
    }

    private void ProcessJsonArray(JsonElement element,
        XElement parent, JsonToXmlConverterOptions options,
        string arrayName)
    {
        int count = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            count++;
        }

        // handle single-item flattening
        if (options.SingleArrayItemFlattening && count == 1)
        {
            XName childName = XName.Get(arrayName);
            XElement child = new(childName);

            JsonElement singleItem = element.EnumerateArray().First();
            ProcessJsonElement(singleItem, child, options);
            parent.Add(child);
            return;
        }

        // process array items
        int index = 1;
        foreach (JsonElement item in element.EnumerateArray())
        {
            XName childName = XName.Get(arrayName);
            XElement child = new(childName);

            if (options.ArrayItemNumbering)
            {
                child.SetAttributeValue("n", index);
            }

            ProcessJsonElement(item, child, options);
            parent.Add(child);
            index++;
        }
    }

    private static void ProcessJsonValue(JsonElement element,
        XElement parent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                parent.Value = element.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Number:
                parent.Value = element.GetRawText();
                break;
            case JsonValueKind.True:
                parent.Value = "true";
                break;
            case JsonValueKind.False:
                parent.Value = "false";
                break;
            case JsonValueKind.Null:
                // leave empty
                break;
        }
    }

    private static bool ShouldSkipProperty(JsonProperty property,
        JsonToXmlConverterOptions options)
    {
        switch (property.Value.ValueKind)
        {
            case JsonValueKind.Null:
                return options.NoNullValues;

            case JsonValueKind.False:
                return options.NoFalseValues;

            case JsonValueKind.Number:
                if (options.NoZeroValues)
                {
                    if (property.Value.TryGetInt32(out int intValue))
                        return intValue == 0;
                    if (property.Value.TryGetInt64(out long longValue))
                        return longValue == 0;
                    if (property.Value.TryGetDouble(out double doubleValue))
                        return Math.Abs(doubleValue) < double.Epsilon;
                    if (property.Value.TryGetDecimal(
                        out decimal decimalValue))
                        return decimalValue == 0;
                }
                return false;

            default:
                return false;
        }
    }

    private void WrapXmlArrays(XElement root,
        Dictionary<XName, XName> map, JsonToXmlConverterOptions options)
    {
        foreach (KeyValuePair<XName, XName> entry in map)
        {
            XName arrayName = entry.Key;
            XName itemName = entry.Value;

            WrapArraySequences(root, arrayName, itemName, options);
        }

        // handle arrays with singularized names (fallback)
        ProcessSingularizedArrays(root, map, options);
    }

    private static void WrapArraySequences(XElement root, XName arrayName,
        XName itemName, JsonToXmlConverterOptions options)
    {
        // find all sequences of elements with the same name
        List<XElement> firstElements = [..root.Descendants(arrayName)
            .Where(e => e.ElementsBeforeSelf().LastOrDefault()?.Name
                != arrayName)];

        foreach (XElement firstElement in firstElements)
        {
            // get all subsequent siblings with the same name
            List<XElement> siblings = [..firstElement.ElementsAfterSelf()
                .TakeWhile(e => e.Name == arrayName)];

            // create a list of all elements to be wrapped
            List<XElement> allElements = [firstElement];
            allElements.AddRange(siblings);

            // if there's only one element, wrap its contents
            if (siblings.Count == 0)
            {
                WrapSingleElement(firstElement, itemName, options);
            }
            else
            {
                WrapMultipleElements(allElements, itemName, options);
            }
        }
    }

    private static void WrapSingleElement(XElement element, XName itemName,
        JsonToXmlConverterOptions options)
    {
        // create a wrapper element with the item name
        XElement wrapper = new(itemName);

        // add numbering if requested
        if (options.ArrayItemNumbering)
        {
            wrapper.SetAttributeValue("n", 1);
        }

        // move the contents to the wrapper
        List<XNode> contents = [..element.Nodes()];
        foreach (XNode node in contents)
        {
            node.Remove();
            wrapper.Add(node);
        }

        // copy attributes if any
        foreach (XAttribute attr in element.Attributes().ToList())
        {
            wrapper.SetAttributeValue(attr.Name, attr.Value);
        }

        element.RemoveNodes();
        element.RemoveAttributes();
        element.Add(wrapper);
    }

    private static void WrapMultipleElements(List<XElement> elements,
        XName itemName, JsonToXmlConverterOptions options)
    {
        XElement parent = elements[0].Parent!;
        XName containerName = elements[0].Name;

        // Remove all elements from their parent
        foreach (XElement element in elements)
        {
            element.Remove();
        }

        // create a container element with the same name as the originals
        XElement container = new(containerName);

        // for each original element, create a wrapper and add the contents
        int index = 1;
        foreach (XElement original in elements)
        {
            XElement wrapper = new(itemName);

            // add numbering if requested
            if (options.ArrayItemNumbering)
            {
                wrapper.SetAttributeValue("n", index);
            }

            // copy contents
            foreach (XNode node in original.Nodes())
            {
                wrapper.Add(new XElement(node is XElement xe
                    ? xe : new XElement("value", node.ToString())));
            }

            // copy attributes
            foreach (XAttribute attr in original.Attributes())
            {
                wrapper.SetAttributeValue(attr.Name, attr.Value);
            }

            container.Add(wrapper);
            index++;
        }

        // add the container back to the parent
        parent.Add(container);
    }

    private void ProcessSingularizedArrays(XElement root,
        Dictionary<XName, XName> explicitMap,
        JsonToXmlConverterOptions options)
    {
        // find all potential array elements (siblings with same name)
        IEnumerable<IGrouping<XName, XElement>> potentialArrays =
            root.Descendants()
                .GroupBy(e => e.Name)
                .Where(g => g.Count() > 1);

        foreach (IGrouping<XName, XElement> group in potentialArrays)
        {
            XName arrayName = group.Key;

            // skip if already in explicit map
            if (explicitMap.ContainsKey(arrayName))
                continue;

            // try to get singularized name
            string? singularName = _singularizer.Singularize(
                arrayName.LocalName);

            if (singularName != null &&
                singularName != arrayName.LocalName)
            {
                XName itemName = XName.Get(singularName,
                    arrayName.NamespaceName);
                WrapArraySequences(root, arrayName, itemName, options);
            }
        }
    }
}

/// <summary>
/// Options for <see cref="JsonToXmlConverter"/>.
/// </summary>
public class JsonToXmlConverterOptions : NamespaceOptions
{
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
    /// True to add an <c>n</c> attribute to array items with their
    /// ordinal number.
    /// </summary>
    public bool ArrayItemNumbering { get; set; }

    /// <summary>
    /// True to flatten arrays with a single item, i.e. to avoid wrapping
    /// them into an entries element.
    /// </summary>
    public bool SingleArrayItemFlattening { get; set; }

    /// <summary>
    /// The name of the root element. Prefix is allowed, e.g. <c>tei:TEI</c>.
    /// Default is "root".
    /// </summary>
    public string RootElementName { get; set; } = "root";

    /// <summary>
    /// True to avoid generating elements for null-valued properties.
    /// </summary>
    public bool NoNullValues { get; set; }

    /// <summary>
    /// True to avoid generating elements for false boolean properties.
    /// </summary>
    public bool NoFalseValues { get; set; }

    /// <summary>
    /// True to avoid generating elements for zero numeric properties.
    /// </summary>
    public bool NoZeroValues { get; set; }

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="JsonToXmlConverterOptions"/> class.
    /// </summary>
    public JsonToXmlConverterOptions()
    {
        DefaultNsPrefix = null;
    }
}
