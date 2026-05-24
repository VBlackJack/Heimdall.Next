/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

public class SplitTreeHelperTests
{
    // ── Helper factories ─────────────────────────────────────────────

    private static SessionPaneModel MakePane(string id = "")
    {
        var pane = new SessionPaneModel();
        if (!string.IsNullOrEmpty(id)) pane.PaneId = id;
        return pane;
    }

    private static SplitContainerModel MakeSplit(
        ISplitContent first, ISplitContent second,
        SplitOrientation orientation = SplitOrientation.Vertical, double ratio = 0.5) =>
        new() { First = first, Second = second, Orientation = orientation, SplitRatio = ratio };

    // ── EnumerateLeaves ──────────────────────────────────────────────

    [Fact]
    public void EnumerateLeaves_Null_ReturnsEmpty()
    {
        Assert.Empty(SplitTreeHelper.EnumerateLeaves(null));
    }

    [Fact]
    public void EnumerateLeaves_SinglePane_ReturnsSelf()
    {
        var pane = MakePane("A");
        var leaves = SplitTreeHelper.EnumerateLeaves(pane).ToList();
        Assert.Single(leaves);
        Assert.Same(pane, leaves[0]);
    }

    [Fact]
    public void EnumerateLeaves_TwoPanes_ReturnsBothInOrder()
    {
        var a = MakePane("A");
        var b = MakePane("B");
        var root = MakeSplit(a, b);

        var leaves = SplitTreeHelper.EnumerateLeaves(root).ToList();
        Assert.Equal(2, leaves.Count);
        Assert.Same(a, leaves[0]);
        Assert.Same(b, leaves[1]);
    }

    [Fact]
    public void EnumerateLeaves_ThreePanes_ReturnsAllDepthFirst()
    {
        //       split1
        //      /      \
        //    split2    C
        //   /    \
        //  A      B
        var a = MakePane("A");
        var b = MakePane("B");
        var c = MakePane("C");
        var inner = MakeSplit(a, b);
        var root = MakeSplit(inner, c);

        var leaves = SplitTreeHelper.EnumerateLeaves(root).ToList();
        Assert.Equal(3, leaves.Count);
        Assert.Same(a, leaves[0]);
        Assert.Same(b, leaves[1]);
        Assert.Same(c, leaves[2]);
    }

    [Fact]
    public void EnumerateLeaves_FourPanes_BalancedTree()
    {
        //        root
        //       /    \
        //    left    right
        //   / \      / \
        //  A   B    C   D
        var a = MakePane("A");
        var b = MakePane("B");
        var c = MakePane("C");
        var d = MakePane("D");
        var root = MakeSplit(MakeSplit(a, b), MakeSplit(c, d));

        var leaves = SplitTreeHelper.EnumerateLeaves(root).ToList();
        Assert.Equal(4, leaves.Count);
        Assert.Same(a, leaves[0]);
        Assert.Same(b, leaves[1]);
        Assert.Same(c, leaves[2]);
        Assert.Same(d, leaves[3]);
    }

    // ── FindPane ─────────────────────────────────────────────────────

    [Fact]
    public void FindPane_Null_ReturnsNull()
    {
        Assert.Null(SplitTreeHelper.FindPane(null, "x"));
    }

    [Fact]
    public void FindPane_EmptyId_ReturnsNull()
    {
        Assert.Null(SplitTreeHelper.FindPane(MakePane("A"), ""));
    }

    [Fact]
    public void FindPane_RootIsTarget_ReturnsIt()
    {
        var pane = MakePane("A");
        Assert.Same(pane, SplitTreeHelper.FindPane(pane, "A"));
    }

    [Fact]
    public void FindPane_DeepInTree_FindsCorrectly()
    {
        var target = MakePane("TARGET");
        var root = MakeSplit(MakePane("A"), MakeSplit(MakePane("B"), target));

        Assert.Same(target, SplitTreeHelper.FindPane(root, "TARGET"));
    }

    [Fact]
    public void FindPane_NonExistent_ReturnsNull()
    {
        var root = MakeSplit(MakePane("A"), MakePane("B"));
        Assert.Null(SplitTreeHelper.FindPane(root, "NOPE"));
    }

    // ── FindPaneByHostControl ────────────────────────────────────────

    [Fact]
    public void FindPaneByHostControl_MatchesReference()
    {
        var control = new object();
        var target = MakePane("A");
        target.HostControl = control;
        var root = MakeSplit(MakePane("B"), target);

        Assert.Same(target, SplitTreeHelper.FindPaneByHostControl(root, control));
    }

    [Fact]
    public void FindPaneByHostControl_NullControl_ReturnsNull()
    {
        var root = MakePane("A");
        Assert.Null(SplitTreeHelper.FindPaneByHostControl(root, null));
    }

    // ── FindParent ───────────────────────────────────────────────────

    [Fact]
    public void FindParent_RootPane_ReturnsNull()
    {
        var pane = MakePane("A");
        Assert.Null(SplitTreeHelper.FindParent(pane, "A"));
    }

    [Fact]
    public void FindParent_DirectChild_ReturnsContainer()
    {
        var child = MakePane("B");
        var root = MakeSplit(MakePane("A"), child);

        Assert.Same(root, SplitTreeHelper.FindParent(root, "B"));
    }

    [Fact]
    public void FindParent_DeepChild_ReturnsCorrectContainer()
    {
        var target = MakePane("C");
        var inner = MakeSplit(MakePane("B"), target);
        var root = MakeSplit(MakePane("A"), inner);

        Assert.Same(inner, SplitTreeHelper.FindParent(root, "C"));
    }

    [Fact]
    public void FindParent_NotFound_ReturnsNull()
    {
        var root = MakeSplit(MakePane("A"), MakePane("B"));
        Assert.Null(SplitTreeHelper.FindParent(root, "NOPE"));
    }

    // ── CountLeaves ──────────────────────────────────────────────────

    [Fact]
    public void CountLeaves_Null_ReturnsZero()
    {
        Assert.Equal(0, SplitTreeHelper.CountLeaves(null));
    }

    [Fact]
    public void CountLeaves_SinglePane_ReturnsOne()
    {
        Assert.Equal(1, SplitTreeHelper.CountLeaves(MakePane()));
    }

    [Fact]
    public void CountLeaves_ComplexTree_ReturnsCorrectCount()
    {
        var root = MakeSplit(
            MakeSplit(MakePane(), MakePane()),
            MakeSplit(MakePane(), MakeSplit(MakePane(), MakePane())));

        Assert.Equal(5, SplitTreeHelper.CountLeaves(root));
    }

    // ── FirstLeaf ────────────────────────────────────────────────────

    [Fact]
    public void FirstLeaf_Null_ReturnsNull()
    {
        Assert.Null(SplitTreeHelper.FirstLeaf(null));
    }

    [Fact]
    public void FirstLeaf_SinglePane_ReturnsSelf()
    {
        var pane = MakePane("A");
        Assert.Same(pane, SplitTreeHelper.FirstLeaf(pane));
    }

    [Fact]
    public void FirstLeaf_DeepTree_ReturnsLeftmostLeaf()
    {
        var leftmost = MakePane("FIRST");
        var root = MakeSplit(MakeSplit(leftmost, MakePane()), MakePane());
        Assert.Same(leftmost, SplitTreeHelper.FirstLeaf(root));
    }

    // ── RemovePane ───────────────────────────────────────────────────

    [Fact]
    public void RemovePane_SinglePane_ReturnsNull()
    {
        var pane = MakePane("A");
        Assert.Null(SplitTreeHelper.RemovePane(pane, "A"));
    }

    [Fact]
    public void RemovePane_TwoPanes_RemoveFirst_PromotesSecond()
    {
        var a = MakePane("A");
        var b = MakePane("B");
        var root = MakeSplit(a, b);

        var result = SplitTreeHelper.RemovePane(root, "A");
        Assert.Same(b, result);
    }

    [Fact]
    public void RemovePane_TwoPanes_RemoveSecond_PromotesFirst()
    {
        var a = MakePane("A");
        var b = MakePane("B");
        var root = MakeSplit(a, b);

        var result = SplitTreeHelper.RemovePane(root, "B");
        Assert.Same(a, result);
    }

    [Fact]
    public void RemovePane_ThreePanes_RemoveMiddle_PreservesStructure()
    {
        //    root
        //   /    \
        //  inner  C
        //  / \
        // A   B
        var a = MakePane("A");
        var b = MakePane("B");
        var c = MakePane("C");
        var inner = MakeSplit(a, b);
        var root = MakeSplit(inner, c);

        // Remove B → inner promoted to A, tree becomes split(A, C)
        var result = SplitTreeHelper.RemovePane(root, "B");
        Assert.IsType<SplitContainerModel>(result);
        var container = (SplitContainerModel)result!;
        Assert.Same(a, container.First);
        Assert.Same(c, container.Second);
    }

    [Fact]
    public void RemovePane_FourPanes_RemoveDeepLeaf()
    {
        //        root
        //       /    \
        //    left    right
        //   / \      / \
        //  A   B    C   D
        var a = MakePane("A");
        var b = MakePane("B");
        var c = MakePane("C");
        var d = MakePane("D");
        var left = MakeSplit(a, b);
        var right = MakeSplit(c, d);
        var root = MakeSplit(left, right);

        // Remove C → right becomes D, tree becomes split(left, D)
        var result = SplitTreeHelper.RemovePane(root, "C");
        Assert.IsType<SplitContainerModel>(result);
        var newRoot = (SplitContainerModel)result!;
        Assert.Same(left, newRoot.First);
        Assert.Same(d, newRoot.Second);
    }

    [Fact]
    public void RemovePane_NonExistentId_ReturnsUnchanged()
    {
        var root = MakeSplit(MakePane("A"), MakePane("B"));
        var result = SplitTreeHelper.RemovePane(root, "NOPE");
        Assert.Same(root, result);
    }

    // ── ReplacePane ──────────────────────────────────────────────────

    [Fact]
    public void ReplacePane_RootIsTarget_ReturnsReplacement()
    {
        var pane = MakePane("A");
        var replacement = MakeSplit(MakePane("X"), MakePane("Y"));

        var result = SplitTreeHelper.ReplacePane(pane, "A", replacement);
        Assert.Same(replacement, result);
    }

    [Fact]
    public void ReplacePane_DirectChild_ReplacesInContainer()
    {
        var a = MakePane("A");
        var b = MakePane("B");
        var root = MakeSplit(a, b);

        var replacement = MakeSplit(MakePane("X"), MakePane("Y"));
        SplitTreeHelper.ReplacePane(root, "B", replacement);

        Assert.Same(a, root.First);
        Assert.Same(replacement, root.Second);
    }

    [Fact]
    public void ReplacePane_DeepChild_ReplacesCorrectly()
    {
        var target = MakePane("TARGET");
        var inner = MakeSplit(MakePane("A"), target);
        var root = MakeSplit(inner, MakePane("C"));

        var replacement = MakeSplit(MakePane("X"), MakePane("Y"));
        SplitTreeHelper.ReplacePane(root, "TARGET", replacement);

        Assert.Same(replacement, inner.Second);
    }

    [Fact]
    public void ReplacePane_VeryDeepChild_ReplacesCorrectly()
    {
        //        root
        //       /    \
        //    level1   D
        //    /    \
        //  level2  C
        //  /    \
        // A    TARGET
        var target = MakePane("TARGET");
        var a = MakePane("A");
        var c = MakePane("C");
        var d = MakePane("D");
        var level2 = MakeSplit(a, target);
        var level1 = MakeSplit(level2, c);
        var root = MakeSplit(level1, d);

        var replacement = MakeSplit(MakePane("X"), MakePane("Y"));
        SplitTreeHelper.ReplacePane(root, "TARGET", replacement);

        Assert.Same(replacement, level2.Second);
        Assert.Equal(5, SplitTreeHelper.CountLeaves(root));
    }

    [Fact]
    public void ReplacePane_NonExistentPaneId_ReturnsUnchangedRoot()
    {
        var a = MakePane("A");
        var b = MakePane("B");
        var root = MakeSplit(a, b);

        var replacement = MakePane("X");
        var result = SplitTreeHelper.ReplacePane(root, "NOPE", replacement);

        Assert.Same(root, result);
        Assert.Same(a, root.First);
        Assert.Same(b, root.Second);
    }

    [Fact]
    public void ReplacePane_ShortCircuits_DoesNotReplaceSecondMatch()
    {
        // Verify that only the first match is replaced (PaneId should be unique,
        // but this verifies the short-circuit behavior)
        var a = MakePane("DUP");
        var b = MakePane("DUP");
        var root = MakeSplit(a, b);

        var replacement = MakePane("X");
        SplitTreeHelper.ReplacePane(root, "DUP", replacement);

        // Only First should be replaced (short-circuit)
        Assert.Same(replacement, root.First);
        Assert.Same(b, root.Second);
    }

    // ── RemovePane: edge cases ────────────────────────────────────

    [Fact]
    public void RemovePane_DeepSingleChildSubtree_PromotesSibling()
    {
        //        root
        //       /    \
        //    inner    C
        //    /    \
        //   A    deep
        //        /  \
        //       B   TARGET
        var target = MakePane("TARGET");
        var b = MakePane("B");
        var a = MakePane("A");
        var c = MakePane("C");
        var deep = MakeSplit(b, target);
        var inner = MakeSplit(a, deep);
        var root = MakeSplit(inner, c);

        // Remove TARGET → deep promoted to B, inner becomes split(A, B)
        var result = SplitTreeHelper.RemovePane(root, "TARGET");
        Assert.IsType<SplitContainerModel>(result);
        var newRoot = (SplitContainerModel)result!;

        // Tree should now be: root(inner(A, B), C)
        Assert.Same(c, newRoot.Second);
        var newInner = Assert.IsType<SplitContainerModel>(newRoot.First);
        Assert.Same(a, newInner.First);
        Assert.Same(b, newInner.Second);
    }

    // ── SplitContainerModel constants ─────────────────────────────

    [Fact]
    public void SplitRatio_IsClamped_ToMinMax()
    {
        var model = new SplitContainerModel
        {
            First = MakePane("A"),
            Second = MakePane("B"),
            SplitRatio = 0.0
        };

        Assert.Equal(SplitContainerModel.MinRatio, model.SplitRatio);

        model.SplitRatio = 1.0;
        Assert.Equal(SplitContainerModel.MaxRatio, model.SplitRatio);

        model.SplitRatio = 0.5;
        Assert.Equal(0.5, model.SplitRatio);
    }

    // ── Integration: split + remove round-trip ───────────────────────

    [Fact]
    public void SplitThenRemove_RestoresOriginal()
    {
        var original = MakePane("A");
        var newPane = MakePane("B");
        var container = MakeSplit(original, newPane, SplitOrientation.Vertical, 0.6);

        // Replace root with the split container
        ISplitContent root = SplitTreeHelper.ReplacePane(original, "A", container);
        Assert.Equal(2, SplitTreeHelper.CountLeaves(root));

        // Remove the new pane → should get back to just the original
        root = SplitTreeHelper.RemovePane(root, "B")!;
        Assert.Same(original, root);
        Assert.Equal(1, SplitTreeHelper.CountLeaves(root));
    }

    [Fact]
    public void MultipleSplits_CreatesDeepTree()
    {
        // Start with one pane, split it 3 times → 4 panes
        ISplitContent root = MakePane("A");

        // Split A → (A, B)
        var b = MakePane("B");
        root = SplitTreeHelper.ReplacePane(root, "A",
            MakeSplit(SplitTreeHelper.FindPane(root, "A")!, b));
        Assert.Equal(2, SplitTreeHelper.CountLeaves(root));

        // Split B → (B, C)
        var c = MakePane("C");
        root = SplitTreeHelper.ReplacePane(root, "B",
            MakeSplit(SplitTreeHelper.FindPane(root, "B")!, c));
        Assert.Equal(3, SplitTreeHelper.CountLeaves(root));

        // Split A → (A, D)
        var d = MakePane("D");
        root = SplitTreeHelper.ReplacePane(root, "A",
            MakeSplit(SplitTreeHelper.FindPane(root, "A")!, d));
        Assert.Equal(4, SplitTreeHelper.CountLeaves(root));

        // All 4 panes findable
        Assert.NotNull(SplitTreeHelper.FindPane(root, "A"));
        Assert.NotNull(SplitTreeHelper.FindPane(root, "B"));
        Assert.NotNull(SplitTreeHelper.FindPane(root, "C"));
        Assert.NotNull(SplitTreeHelper.FindPane(root, "D"));
    }

    // ── RemovePane: 8-leaf max tree ─────────────────────────────────

    [Fact]
    public void RemovePane_EightLeafTree_RemovesCorrectly()
    {
        //          root
        //        /      \
        //      L1        L2
        //     / \       / \
        //   L3   L4   L5   L6
        //  / \  / \  / \  / \
        // A  B C  D E  F G  H
        var a = MakePane("A"); var b = MakePane("B");
        var c = MakePane("C"); var d = MakePane("D");
        var e = MakePane("E"); var f = MakePane("F");
        var g = MakePane("G"); var h = MakePane("H");
        var root = MakeSplit(
            MakeSplit(MakeSplit(a, b), MakeSplit(c, d)),
            MakeSplit(MakeSplit(e, f), MakeSplit(g, h)));

        Assert.Equal(8, SplitTreeHelper.CountLeaves(root));

        // Remove D from L4 (C,D) → L4 becomes C, L1 becomes (L3, C)
        var result = SplitTreeHelper.RemovePane(root, "D");
        Assert.Equal(7, SplitTreeHelper.CountLeaves(result));
        Assert.NotNull(SplitTreeHelper.FindPane(result!, "C"));
        Assert.Null(SplitTreeHelper.FindPane(result!, "D"));

        // Remove A from L3 (A,B) → L3 becomes B
        result = SplitTreeHelper.RemovePane(result!, "A");
        Assert.Equal(6, SplitTreeHelper.CountLeaves(result));
        Assert.Null(SplitTreeHelper.FindPane(result!, "A"));
    }

    // ── Swap idempotence (swap twice returns original) ───────────────

    [Fact]
    public void SwapChildren_TwiceRestoresOriginalOrder()
    {
        var a = MakePane("A");
        var b = MakePane("B");
        var container = MakeSplit(a, b);

        // Swap once
        (container.First, container.Second) = (container.Second, container.First);
        Assert.Same(b, container.First);
        Assert.Same(a, container.Second);

        // Swap again — back to original
        (container.First, container.Second) = (container.Second, container.First);
        Assert.Same(a, container.First);
        Assert.Same(b, container.Second);
    }

    // ── SplitRatio: clamping via OnSplitRatioChanging ────────────────

    [Fact]
    public void SplitRatio_ClampedBeforePropertyChanged()
    {
        var model = new SplitContainerModel
        {
            First = MakePane("A"),
            Second = MakePane("B"),
        };

        // Values below MinRatio are clamped up
        model.SplitRatio = 0.05;
        Assert.Equal(SplitContainerModel.MinRatio, model.SplitRatio);

        // Values above MaxRatio are clamped down
        model.SplitRatio = 0.95;
        Assert.Equal(SplitContainerModel.MaxRatio, model.SplitRatio);

        // Values within range are preserved exactly
        model.SplitRatio = 0.33;
        Assert.Equal(0.33, model.SplitRatio);
    }

    [Fact]
    public void SplitRatio_PropertyChanged_ReportsClampedValue()
    {
        var model = new SplitContainerModel
        {
            First = MakePane("A"),
            Second = MakePane("B"),
            SplitRatio = 0.5
        };

        double? reportedValue = null;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SplitContainerModel.SplitRatio))
                reportedValue = model.SplitRatio;
        };

        model.SplitRatio = 0.01; // Will be clamped to MinRatio
        Assert.NotNull(reportedValue);
        Assert.Equal(SplitContainerModel.MinRatio, reportedValue);
    }

    // ── Defensive: null First/Second on uninitialized container ──────

    [Fact]
    public void EnumerateLeaves_NullChildren_DoesNotCrash()
    {
        var container = new SplitContainerModel();
        // First and Second are null! by default — should not throw
        var leaves = SplitTreeHelper.EnumerateLeaves(container).ToList();
        Assert.Empty(leaves);
    }

    [Fact]
    public void CountLeaves_NullChildren_ReturnsZero()
    {
        var container = new SplitContainerModel();
        Assert.Equal(0, SplitTreeHelper.CountLeaves(container));
    }

    [Fact]
    public void FirstLeaf_NullFirst_ReturnsNull()
    {
        var container = new SplitContainerModel();
        Assert.Null(SplitTreeHelper.FirstLeaf(container));
    }

    [Fact]
    public void FindPane_NullChildren_ReturnsNull()
    {
        var container = new SplitContainerModel();
        Assert.Null(SplitTreeHelper.FindPane(container, "any"));
    }
}
