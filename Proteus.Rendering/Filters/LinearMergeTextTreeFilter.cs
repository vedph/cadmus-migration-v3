using Fusi.Tools;
using Fusi.Tools.Configuration;
using Fusi.Tools.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Proteus.Rendering.Filters;

/// <summary>
/// A merge filter for linear trees. This filter merges multiple
/// nodes into a single one whose text is the concatenation of the text of
/// all the merged nodes. Merging is done by checking the features of the
/// nodes.
/// <para>Tag: <c>it.vedph.text-tree-filter.linear-merge</c>.</para>
/// </summary>
[Tag("it.vedph.text-tree-filter.linear-merge")]
public sealed class LinearMergeTextTreeFilter : ITextTreeFilter,
    IConfigurable<MergeTextTreeFilterOptions>
{
    private MergeTextTreeFilterOptions _options = new();

    /// <summary>
    /// Gets or sets the logger.
    /// </summary>
    public ILogger? Logger { get; set; }

    private HashSet<string> GetRelevantFeatures(
        TreeNode<ExportedSegment> node)
    {
        HashSet<string> keys = [];
        if (node.Data?.Features == null || node.Data.Features.Count == 0)
            return keys;

        foreach (StringPair f in node.Data.Features)
        {
            if (_options.Features?.Contains(f.Name) != false)
            {
                // apply value filters if any
                string? value = f.Value != null
                    ? _options.ApplyFilters(f.Value)
                    : null;

                // add key with feature name = filtered value
                keys.Add($"{f.Name}={value}");
            }
        }

        return keys;
    }

    /// <summary>
    /// Applies this filter to the specified tree, generating a new tree or
    /// just returning the source tree if no merge is possible.
    /// </summary>
    /// <param name="tree">The tree's root node.</param>
    /// <param name="source">The source being rendered.</param>
    /// <returns>
    /// The root node of the new tree.
    /// </returns>
    /// <exception cref="ArgumentNullException">tree</exception>
    public TreeNode<ExportedSegment> Apply(TreeNode<ExportedSegment> tree,
        object? source = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        // nothing to do for an empty text
        if (tree.FirstChild == null) return tree;

        // create root of the new tree
        TreeNode<ExportedSegment> root = tree.Clone(false, false);

        // add to it a copy of first node as child
        TreeNode<ExportedSegment> target = root.AddChild(
            tree.FirstChild.Clone(false, false));

        // collect relevant features from it
        HashSet<string> segFeats = GetRelevantFeatures(tree.FirstChild);

        // start from source's grandchild
        TreeNode<ExportedSegment>? current = tree.FirstChild.FirstChild;

        while (current != null)
        {
            // get relevant features of current node
            HashSet<string> feats = GetRelevantFeatures(current);

            // if features are equal and either we don't care about LF or there
            // is no LF at current's text end, merge current node into target
            if (segFeats.SetEquals(feats) &&
                (_options.NoBreakAtLF || current.Data?.Text?.EndsWith('\n') != true))
            {
                // merge into current and move to next child
                if (current.Label != null)
                {
                    target.Label = target.Label != null
                        ? target.Label + current.Label : current.Label;
                }
                target.Data = ExportedSegment.MergeSegments(current.Data,
                    target.Data);
                current = current.FirstChild;
            }
            else
            {
                // append a copy current node to the built branch
                target = target.AddChild(current.Clone(false, false));
                // move to next child
                current = current.FirstChild;
            }

            segFeats = feats;
        }

        return root;
    }

    /// <summary>
    /// Configures this filter with the specified options.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <exception cref="ArgumentNullException">options</exception>
    public void Configure(MergeTextTreeFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }
}

/// <summary>
/// Replacement option for the <see cref="MergeTextTreeFilterOptions"/>.
/// </summary>
public class MergeTextTreeFilterRepOption
{
    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Find"/> is a regex
    /// rather than a literal string.
    /// </summary>
    public bool IsRegex { get; set; }

    /// <summary>
    /// Gets or sets the find text or expression.
    /// </summary>
    public required string Find { get; set; }

    /// <summary>
    /// Gets or sets the replacement text.
    /// </summary>
    public required string Replace { get; set; }

    /// <summary>
    /// Converts to string.
    /// </summary>
    /// <returns>String.</returns>
    public override string ToString()
    {
        return $"{(IsRegex? "\u24c7" : "")}{Find} => {Replace}";
    }
}

/// <summary>
/// Options for the <see cref="LinearMergeTextTreeFilter"/>.
/// </summary>
public class MergeTextTreeFilterOptions
{
    private List<Regex?>? _regexes;

    /// <summary>
    /// Gets or sets the names of the features considered as criteria for
    /// merging. If not specified, all the features are considered.
    /// </summary>
    public HashSet<string>? Features { get; set; }

    /// <summary>
    /// Gets or sets the filters to apply to features values before comparing
    /// them. This is useful to ignore some parts of the values.
    /// </summary>
    public List<MergeTextTreeFilterRepOption>? ValueFilters { get; set; }

    /// <summary>
    /// Gets or sets a value indicating not to force a segment break after
    /// any node whose text ends with (or is equal to) a LF.
    /// </summary>
    public bool NoBreakAtLF { get; set; }

    /// <summary>
    /// Applies the filters defined in <see cref="ValueFilters"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The filtered text.</returns>
    public string ApplyFilters(string text)
    {
        if (string.IsNullOrEmpty(text) || ValueFilters == null) return text;

        // create regexes if not done yet
        if (_regexes == null)
        {
            _regexes = new List<Regex?>(ValueFilters.Count);
            if (ValueFilters != null)
            {
                foreach (MergeTextTreeFilterRepOption filter in ValueFilters)
                {
                    Regex? regex = filter.IsRegex
                        ? new Regex(filter.Find) : null;
                    _regexes.Add(regex);
                }
            }
        }

        // apply filters
        string result = text;
        for (int i = 0; i < _regexes.Count; i++)
        {
            result = _regexes[i] != null
                ? _regexes[i]!.Replace(result, ValueFilters![i].Replace)
                : result.Replace(ValueFilters![i].Find, ValueFilters[i].Replace);
        }

        return result;
    }
}
