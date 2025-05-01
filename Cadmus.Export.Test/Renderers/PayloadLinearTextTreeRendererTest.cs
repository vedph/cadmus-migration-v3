using Cadmus.Core;
using Cadmus.Export.Filters;
using Cadmus.Export.Renderers;
using Cadmus.General.Parts;
using Cadmus.Philology.Parts;
using Fusi.Tools.Data;
using System.Collections.Generic;
using System;
using Xunit;
using Proteus.Rendering;

namespace Cadmus.Export.Test.Renderers;

public sealed class PayloadLinearTextTreeRendererTest
{
    private static TokenTextPart GetTextPart()
    {
        TokenTextPart part = new();
        part.Lines.Add(new TextLine
        {
            Y = 1,
            Text = "illuc unde negant redire quemquam"
        });
        return part;
    }

    private static TokenTextLayerPart<ApparatusLayerFragment> GetApparatusPart()
    {
        // 1     2    3      4      5
        // illuc unde negant redire quemquam
        // AAAAA....................BBBBBBBB
        TokenTextLayerPart<ApparatusLayerFragment> part = new();

        // illuc
        part.Fragments.Add(new()
        {
            Location = "1.1",
            Entries =
            [
                new ApparatusEntry
                {
                    Type = ApparatusEntryType.Note,
                    IsAccepted = true,
                    Witnesses =
                    [
                        new AnnotatedValue
                        {
                            Value = "O1",
                        }
                    ]
                },
                new ApparatusEntry
                {
                    Type = ApparatusEntryType.Replacement,
                    Value = "illud",
                    Witnesses =
                    [
                        new AnnotatedValue { Value = "O" },
                        new AnnotatedValue { Value = "G" },
                        new AnnotatedValue { Value = "R" }
                    ]
                },
                new ApparatusEntry
                {
                    Type = ApparatusEntryType.Replacement,
                    Value = "illic",
                    Authors =
                    [
                        new LocAnnotatedValue
                        {
                            Value = "Fruterius",
                            Note = "(†1566) 1605a 388"
                        },
                    ]
                },
            ]
        });

        // quemquam
        part.Fragments.Add(new()
        {
            Location = "1.5",
            Entries =
            [
                new ApparatusEntry
                {
                    Type = ApparatusEntryType.Note,
                    IsAccepted = true,
                    Witnesses =
                    [
                        new AnnotatedValue { Value = "O" },
                        new AnnotatedValue { Value = "G" },
                    ]
                },
                new ApparatusEntry
                {
                    Type = ApparatusEntryType.Replacement,
                    Value = "umquam",
                    Witnesses =
                    [
                        new AnnotatedValue { Value = "R" }
                    ],
                    Note = "some note"
                },
            ]
        });

        return part;
    }

    public static (TreeNode<ExportedSegment> tree, IItem item) GetTreeAndItem()
    {
        // get item
        TokenTextPart textPart = GetTextPart();
        TokenTextLayerPart<ApparatusLayerFragment> appPart = GetApparatusPart();
        Item item = new();
        item.Parts.Add(textPart);
        item.Parts.Add(appPart);

        // flatten
        TokenTextPartFlattener flattener = new();
        Tuple<string, IList<AnnotatedTextRange>> tr = flattener.Flatten(
            textPart, [appPart]);

        // merge ranges
        IList<AnnotatedTextRange> mergedRanges = AnnotatedTextRange.MergeRanges(
            0, tr.Item1.Length - 1, tr.Item2);
        // assign text to merged ranges
        foreach (AnnotatedTextRange range in mergedRanges)
            range.AssignText(tr.Item1);

        // build a linear tree from ranges
        TreeNode<ExportedSegment> tree = CadmusTextTreeBuilder.BuildTreeFromRanges(
            mergedRanges, tr.Item1);
        // apply block filter
        return (new BlockLinearTextTreeFilter().Apply(tree, item), item);
    }

    [Fact]
    public void Apply_WithLines_SingleLine_Ok()
    {
        (TreeNode<ExportedSegment>? tree, IItem _) = GetTreeAndItem();
        PayloadLinearTextTreeRenderer renderer = new();

        string json = renderer.Render(tree, new CadmusRendererContext());

        Assert.StartsWith("[[", json);
        Assert.Equal("[[{\"Range\":" +
            "{\"Start\":0,\"End\":4,\"FragmentIds\":" +
            "[\"it.vedph.token-text-layer:fr.it.vedph.apparatus@0\"]," +
            "\"Text\":\"illuc\"},\"IsBeforeEol\":false," +
            "\"Text\":\"illuc\"}," +
            "{\"Range\":{\"Start\":5,\"End\":24,\"FragmentIds\":[]," +
            "\"Text\":\" unde negant redire \"}," +
            "\"IsBeforeEol\":false,\"Text\":\" unde negant redire \"}," +
            "{\"Range\":{\"Start\":25,\"End\":32,\"FragmentIds\":" +
            "[\"it.vedph.token-text-layer:fr.it.vedph.apparatus@1\"]," +
            "\"Text\":\"quemquam\"},\"IsBeforeEol\":false," +
            "\"Text\":\"quemquam\"}]]", json);
    }

    [Fact]
    public void Apply_WithoutLines_SingleLine_Ok()
    {
        (TreeNode<ExportedSegment>? tree, IItem _) = GetTreeAndItem();
        PayloadLinearTextTreeRenderer renderer = new();
        renderer.Configure(new PayloadLinearTextTreeRendererOptions
        {
            FlattenLines = true
        });

        string json = renderer.Render(tree, new CadmusRendererContext());

        Assert.StartsWith("[{", json);
        Assert.Equal("[{\"Range\":" +
            "{\"Start\":0,\"End\":4,\"FragmentIds\":" +
            "[\"it.vedph.token-text-layer:fr.it.vedph.apparatus@0\"]," +
            "\"Text\":\"illuc\"},\"IsBeforeEol\":false," +
            "\"Text\":\"illuc\"}," +
            "{\"Range\":{\"Start\":5,\"End\":24,\"FragmentIds\":[]," +
            "\"Text\":\" unde negant redire \"}," +
            "\"IsBeforeEol\":false,\"Text\":\" unde negant redire \"}," +
            "{\"Range\":{\"Start\":25,\"End\":32,\"FragmentIds\":" +
            "[\"it.vedph.token-text-layer:fr.it.vedph.apparatus@1\"]," +
            "\"Text\":\"quemquam\"},\"IsBeforeEol\":false," +
            "\"Text\":\"quemquam\"}]", json);
    }

    // TODO: add multiple lines tests
}
