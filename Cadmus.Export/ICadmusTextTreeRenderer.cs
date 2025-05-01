using Cadmus.Core;
using Fusi.Tools.Data;
using Proteus.Core.Text;
using Proteus.Rendering;
using System.Collections.Generic;

namespace Cadmus.Export;

/// <summary>
/// Cadmus renderer for text trees.
/// </summary>
public interface ICadmusTextTreeRenderer
{
    /// <summary>
    /// Text filters.
    /// </summary>
    List<ITextFilter> Filters { get; }

    /// <summary>
    /// Resets the state of this renderer if any. This is called once before
    /// starting the rendering process.
    /// </summary>
    /// <param name="context">The context.</param>
    void Reset(CadmusRendererContext context);

    /// <summary>
    /// Renders the head of the output. This is called by the item composer
    /// once when starting the rendering process and can be used to output
    /// specific content at the document's start.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>Head content.</returns>
    string RenderHead(CadmusRendererContext context);

    /// <summary>
    /// Called when items group has changed.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="prevGroupId">The previous group identifier.</param>
    /// <param name="context">The context.</param>
    void OnGroupChanged(IItem item, string? prevGroupId, CadmusRendererContext context);

    /// <summary>
    /// Renders the specified tree.
    /// </summary>
    /// <param name="tree">The tree.</param>
    /// <param name="context">The rendering context.</param>
    /// <returns>Rendition.</returns>
    string Render(TreeNode<ExportedSegment> tree, CadmusRendererContext context);

    /// <summary>
    /// Renders the tail of the output. This is called by the item composer
    /// once when ending the rendering process and can be used to output
    /// specific content at the document's end.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>Tail content.</returns>
    string RenderTail(CadmusRendererContext context);
}
