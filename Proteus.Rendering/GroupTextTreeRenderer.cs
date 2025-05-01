﻿using Fusi.Tools.Text;

namespace Proteus.Rendering;

/// <summary>
/// A text tree renderer which handles items groups so that each group becomes
/// a distinct TEI document in output.
/// </summary>
/// <seealso cref="TextTreeRenderer{HandledType}" />
public abstract class GroupTextTreeRenderer<HandledType> :
    TextTreeRenderer<HandledType>
{
    private int _group;
    private string? _pendingGroupId;

    /// <summary>
    /// Gets or sets the group head template. This must be set by the derived
    /// class from its options or logic.
    /// </summary>
    protected string? GroupHeadTemplate { get; set; }

    /// <summary>
    /// Gets or sets the group tail template. This must be set by the derived
    /// class from its options or logic.
    /// </summary>
    protected string? GroupTailTemplate { get; set; }

    /// <summary>
    /// Resets the state of this renderer. This is called once before
    /// starting the rendering process.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Reset(IRendererContext context)
    {
        _group = 0;
        _pendingGroupId = null;
    }

    /// <summary>
    /// Gets the group identifier from the specified context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>Group ID or null.</returns>
    protected abstract string? GetGroupId(IRendererContext context);

    /// <summary>
    /// Called when items group has changed.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="prevGroupId">The previous group identifier.</param>
    public override void OnGroupChanged(IRendererContext context,
        string? prevGroupId)
    {
        _group++;
        _pendingGroupId = GetGroupId(context);
    }

    // TODO: possibly implement in derived renderers handling string
    //public override HandledType? RenderTail(IRendererContext context)
    //{
    //    // close a group if any
    //    if (_group > 0 && !string.IsNullOrEmpty(GroupTailTemplate))
    //    {
    //        return TextTemplate.FillTemplate(
    //            GroupTailTemplate, context.Data);
    //    }
    //    return default;
    //}

    /// <summary>
    /// Wraps the received XML in the group head and tail templates, if any.
    /// </summary>
    /// <param name="xml">The XML.</param>
    /// <param name="context">The renderer context.</param>
    /// <returns>Wrapped XML or just the input XML.</returns>
    protected string WrapXml(string xml, IRendererContext context)
    {
        // if there is a pending group ID:
        // - if there is a current group, prepend tail.
        // - prepend head.
        if (_pendingGroupId != null)
        {
            if (_group > 0 && !string.IsNullOrEmpty(GroupTailTemplate))
            {
                return TextTemplate.FillTemplate(
                    GroupTailTemplate, context.Data) + xml;
            }
            if (!string.IsNullOrEmpty(GroupHeadTemplate))
            {
                return TextTemplate.FillTemplate(
                    GroupHeadTemplate, context.Data) + xml;
            }
        }
        return xml;
    }
}
