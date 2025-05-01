using System;
using Cadmus.Core;
using Fusi.Tools.Data;
using Proteus.Rendering;

namespace Cadmus.Export.Renderers;

/// <summary>
/// Base class for <see cref="ICadmusTextTreeRenderer"/> implementations.
/// </summary>
public abstract class CadmusTextTreeRenderer : FilteredRenderer
{
    /// <summary>
    /// Resets the state of this renderer if any. This is called once before
    /// starting the rendering process.
    /// </summary>
    /// <param name="context">The context.</param>
    public virtual void Reset(CadmusRendererContext context)
    {
        // nothing to do, override if required
    }

    /// <summary>
    /// Renders the head of the output. This is called by the item composer
    /// once when starting the rendering process and can be used to output
    /// specific content at the document's start.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>Head content.</returns>
    public virtual string RenderHead(CadmusRendererContext context)
    {
        return "";
    }

    /// <summary>
    /// Called when items group has changed. The default implementation does
    /// nothing.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="prevGroupId">The previous group identifier.</param>
    /// <param name="context">The context.</param>
    public virtual void OnGroupChanged(IItem item, string? prevGroupId,
        CadmusRendererContext context)
    {
        // nothing to do, override if required
    }

    /// <summary>
    /// Renders the tail of the output. This is called by the item composer
    /// once when ending the rendering process and can be used to output
    /// specific content at the document's end.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>Tail content.</returns>
    public virtual string RenderTail(CadmusRendererContext context)
    {
        return "";
    }

    /// <summary>
    /// Renders the specified JSON code.
    /// </summary>
    /// <param name="tree">The root node of the text tree.</param>
    /// <param name="context">The renderer context.</param>
    /// <returns>Rendered output.</returns>
    protected abstract string DoRender(TreeNode<ExportedSegment> tree,
        CadmusRendererContext context);

    /// <summary>
    /// Renders the specified JSON code.
    /// </summary>
    /// <param name="tree">The root node of the text tree.</param>
    /// <param name="context">The renderer context.</param>
    /// <returns>Rendered output.</returns>
    /// <exception cref="ArgumentNullException">tree</exception>
    public string Render(TreeNode<ExportedSegment> tree,
        CadmusRendererContext context)
    {
        ArgumentNullException.ThrowIfNull(tree);

        string result = DoRender(tree, context);
        return ApplyFilters(result, context);
    }
}
