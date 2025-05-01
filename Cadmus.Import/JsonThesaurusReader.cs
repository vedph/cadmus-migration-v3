using Cadmus.Core.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Cadmus.Import;

/// <summary>
/// JSON thesaurus reader. This reads a JSON document containing either an
/// array of thesauri, or a single thesaurus.
/// </summary>
/// <seealso cref="IThesaurusReader" />
public sealed class JsonThesaurusReader : IThesaurusReader
{
    private readonly JsonDocument _doc;
    private readonly JsonSerializerOptions _options;
    private IList<JsonElement>? _elements;
    private int _index;
    private bool _disposedValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonThesaurusReader"/> class.
    /// </summary>
    /// <param name="source">The source stream.</param>
    /// <exception cref="ArgumentNullException">source</exception>
    public JsonThesaurusReader(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using StreamReader reader = new(source, Encoding.UTF8);
        string json = reader.ReadToEnd();
        _doc = JsonDocument.Parse(json);
        _index = -1;
        _options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonThesaurusReader"/> class.
    /// </summary>
    /// <param name="json">The JSON code to read thesauri from.</param>
    /// <exception cref="ArgumentNullException">json</exception>
    public JsonThesaurusReader(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        _doc = JsonDocument.Parse(json);
        _index = -1;
        _options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Read the next thesaurus entry from source.
    /// </summary>
    /// <returns>
    /// Thesaurus, or null if no more thesauri in source.
    /// </returns>
    public Thesaurus? Next()
    {
        if (_index == -1)
        {
            switch (_doc.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    _elements = _doc.RootElement.EnumerateArray().ToList();
                    if (_elements.Count == 0) return null;
                    _index = 0;
                    return _elements[0].Deserialize<Thesaurus>(_options);

                case JsonValueKind.Object:
                    _index = 0;
                    _elements = Array.Empty<JsonElement>();
                    return _doc.RootElement.Deserialize<Thesaurus>(_options);

                default:
                    return null;
            }
        }
        else
        {
            _index++;
            if (_index >= _elements!.Count) return null;
            return _elements[_index].Deserialize<Thesaurus>(_options);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _doc.Dispose();
            }
            _disposedValue = true;
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing,
    /// or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
