using Cadmus.Core;
using Cadmus.Export.Renderers;
using Fusi.Tools.Text;

namespace Cadmus.Export.ML.Renderers;

/// <summary>
/// A text tree renderer which handles items groups so that each group becomes
/// a distinct TEI document in output.
/// </summary>
/// <seealso cref="CadmusTextTreeRenderer" />
public abstract class CadmusGroupTextTreeRenderer : CadmusTextTreeRenderer
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
    public override void Reset(CadmusRendererContext context)
    {
        _group = 0;
        _pendingGroupId = null;
    }

    /// <summary>
    /// Called when items group has changed.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="prevGroupId">The previous group identifier.</param>
    /// <param name="context">The context.</param>
    public override void OnGroupChanged(IItem item, string? prevGroupId,
        CadmusRendererContext context)
    {
        _group++;
        _pendingGroupId = item.GroupId;
    }

    /// <summary>
    /// Renders the tail of the output. This is called by the item composer
    /// once when ending the rendering process and can be used to output
    /// specific content at the document's end.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>Tail content.</returns>
    public override string RenderTail(CadmusRendererContext context)
    {
        // close a group if any
        if (_group > 0 && !string.IsNullOrEmpty(GroupTailTemplate))
        {
            return TextTemplate.FillTemplate(
                GroupTailTemplate, context.Data);
        }
        return "";
    }

    /// <summary>
    /// Wraps the received XML in the group head and tail templates, if any.
    /// </summary>
    /// <param name="xml">The XML.</param>
    /// <param name="context">The renderer context.</param>
    /// <returns>Wrapped XML or just the input XML.</returns>
    protected string WrapXml(string xml, CadmusRendererContext context)
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
