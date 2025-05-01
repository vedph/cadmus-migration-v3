using Cadmus.Core.Config;
using System;

namespace Cadmus.Import;

/// <summary>
/// A generic thesaurus reader.
/// </summary>
/// <seealso cref="IDisposable" />
public interface IThesaurusReader : IDisposable
{
    /// <summary>
    /// Read the next thesaurus entry from source.
    /// </summary>
    /// <returns>Thesaurus, or null if no more thesauri in source.</returns>
    Thesaurus? Next();
}