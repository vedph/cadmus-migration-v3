using Cadmus.Core;
using Proteus.Core.Text;
using Proteus.Rendering;
using System.Collections.Generic;

namespace Cadmus.Export;

/// <summary>
/// Cadmus renderer for text trees.
/// </summary>
public interface ICadmusTextTreeRenderer : ITextTreeRenderer<string>
{
    /// <summary>
    /// Text filters.
    /// </summary>
    List<ITextFilter> Filters { get; }

    /// <summary>
    /// Called when items group has changed.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="prevGroupId">The previous group identifier.</param>
    /// <param name="context">The context.</param>
    //void OnGroupChanged(IItem item, string? prevGroupId, CadmusRendererContext context);
}
