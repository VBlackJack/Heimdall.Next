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

namespace Heimdall.Core.Models;

/// <summary>
/// Pure static utility methods for manipulating the recursive split pane tree.
/// All operations are side-effect free except where noted. No WPF dependency.
/// </summary>
public static class SplitTreeHelper
{
    /// <summary>
    /// Enumerates all leaf panes in depth-first order (left/first child before right/second).
    /// </summary>
    public static IEnumerable<SessionPaneModel> EnumerateLeaves(ISplitContent? root)
    {
        if (root is null) yield break;

        if (root is SessionPaneModel pane)
        {
            yield return pane;
        }
        else if (root is SplitContainerModel container)
        {
            foreach (var leaf in EnumerateLeaves(container.First))
                yield return leaf;
            foreach (var leaf in EnumerateLeaves(container.Second))
                yield return leaf;
        }
    }

    /// <summary>
    /// Finds a leaf pane by its <see cref="SessionPaneModel.PaneId"/>.
    /// Returns null if not found.
    /// </summary>
    public static SessionPaneModel? FindPane(ISplitContent? root, string paneId)
    {
        if (string.IsNullOrEmpty(paneId) || root is null) return null;

        if (root is SessionPaneModel pane &&
            string.Equals(pane.PaneId, paneId, StringComparison.Ordinal))
        {
            return pane;
        }

        if (root is SplitContainerModel container)
        {
            return FindPane(container.First, paneId) ?? FindPane(container.Second, paneId);
        }

        return null;
    }

    /// <summary>
    /// Finds a leaf pane by its <see cref="SessionPaneModel.ServerId"/>.
    /// Returns the first match (depth-first). Returns null if not found.
    /// </summary>
    public static SessionPaneModel? FindPaneByServerId(ISplitContent? root, string serverId)
    {
        if (string.IsNullOrEmpty(serverId) || root is null) return null;

        if (root is SessionPaneModel pane &&
            string.Equals(pane.ServerId, serverId, StringComparison.Ordinal))
        {
            return pane;
        }

        if (root is SplitContainerModel container)
        {
            return FindPaneByServerId(container.First, serverId)
                   ?? FindPaneByServerId(container.Second, serverId);
        }

        return null;
    }

    /// <summary>
    /// Finds a leaf pane by its <see cref="SessionPaneModel.HostControl"/> reference.
    /// Returns null if not found.
    /// </summary>
    public static SessionPaneModel? FindPaneByHostControl(ISplitContent? root, object? hostControl)
    {
        if (hostControl is null || root is null) return null;

        if (root is SessionPaneModel pane && ReferenceEquals(pane.HostControl, hostControl))
        {
            return pane;
        }

        if (root is SplitContainerModel container)
        {
            return FindPaneByHostControl(container.First, hostControl)
                   ?? FindPaneByHostControl(container.Second, hostControl);
        }

        return null;
    }

    /// <summary>
    /// Finds the parent <see cref="SplitContainerModel"/> of a pane identified by its PaneId.
    /// Returns null if the pane is the root or not found.
    /// </summary>
    public static SplitContainerModel? FindParent(ISplitContent? root, string paneId)
    {
        if (string.IsNullOrEmpty(paneId) || root is not SplitContainerModel container) return null;

        if (IsDirectChild(container.First, paneId) || IsDirectChild(container.Second, paneId))
        {
            return container;
        }

        return FindParent(container.First, paneId) ?? FindParent(container.Second, paneId);
    }

    /// <summary>
    /// Returns the sibling of a pane within the same parent container.
    /// Returns null if the pane has no parent (root) or not found.
    /// </summary>
    public static ISplitContent? FindSibling(ISplitContent? root, string paneId)
    {
        var parent = FindParent(root, paneId);
        if (parent is null) return null;

        if (IsDirectChild(parent.First, paneId)) return parent.Second;
        if (IsDirectChild(parent.Second, paneId)) return parent.First;
        return null;
    }

    /// <summary>
    /// Removes a leaf pane from the tree and promotes its sibling.
    /// Returns the new root (which may be a single <see cref="SessionPaneModel"/>
    /// if only one pane remains). Returns null if the pane is the only node.
    /// Does NOT dispose the pane's HostControl — caller must do that first.
    /// </summary>
    public static ISplitContent? RemovePane(ISplitContent? root, string paneId)
    {
        if (root is null || string.IsNullOrEmpty(paneId)) return root;

        // If root IS the pane, removing it leaves nothing
        if (root is SessionPaneModel rootPane &&
            string.Equals(rootPane.PaneId, paneId, StringComparison.Ordinal))
        {
            return null;
        }

        if (root is not SplitContainerModel container) return root;

        // Check if target is a direct child of this container
        if (IsDirectChild(container.First, paneId))
        {
            // Promote the other child (Second) to replace this container
            return PromoteInTree(root, container, container.Second);
        }

        if (IsDirectChild(container.Second, paneId))
        {
            // Promote the other child (First) to replace this container
            return PromoteInTree(root, container, container.First);
        }

        // Recurse deeper — rebuild the tree with the modified subtree
        var modifiedFirst = RemovePane(container.First, paneId);
        if (!ReferenceEquals(modifiedFirst, container.First))
        {
            if (modifiedFirst is null)
            {
                // Entire First subtree consumed — promote Second
                return PromoteInTree(root, container, container.Second);
            }

            container.First = modifiedFirst;
            return root;
        }

        var modifiedSecond = RemovePane(container.Second, paneId);
        if (!ReferenceEquals(modifiedSecond, container.Second))
        {
            if (modifiedSecond is null)
            {
                // Entire Second subtree consumed — promote First
                return PromoteInTree(root, container, container.First);
            }

            container.Second = modifiedSecond;
            return root;
        }

        return root;
    }

    /// <summary>
    /// Replaces a leaf pane with a new subtree (typically a <see cref="SplitContainerModel"/>
    /// wrapping the original pane and a new pane). Returns the new root.
    /// </summary>
    public static ISplitContent ReplacePane(ISplitContent root, string paneId, ISplitContent replacement)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(replacement);

        if (root is SessionPaneModel rootPane &&
            string.Equals(rootPane.PaneId, paneId, StringComparison.Ordinal))
        {
            return replacement;
        }

        if (root is SplitContainerModel container)
        {
            ReplacePaneRecursive(container, paneId, replacement);
        }

        return root;
    }

    /// <summary>
    /// Recursively replaces a leaf pane inside a container tree (in-place mutation).
    /// Returns true when the target pane was found and replaced, allowing short-circuit.
    /// </summary>
    private static bool ReplacePaneRecursive(
        SplitContainerModel container, string paneId, ISplitContent replacement)
    {
        // Direct child check
        if (container.First is SessionPaneModel firstPane &&
            string.Equals(firstPane.PaneId, paneId, StringComparison.Ordinal))
        {
            container.First = replacement;
            return true;
        }

        if (container.Second is SessionPaneModel secondPane &&
            string.Equals(secondPane.PaneId, paneId, StringComparison.Ordinal))
        {
            container.Second = replacement;
            return true;
        }

        // Recurse into nested containers (short-circuit after first match)
        if (container.First is SplitContainerModel firstContainer
            && ReplacePaneRecursive(firstContainer, paneId, replacement))
        {
            return true;
        }

        if (container.Second is SplitContainerModel secondContainer)
        {
            return ReplacePaneRecursive(secondContainer, paneId, replacement);
        }

        return false;
    }

    /// <summary>
    /// Returns the total number of leaf panes in the tree.
    /// </summary>
    public static int CountLeaves(ISplitContent? root) => root switch
    {
        null => 0,
        SessionPaneModel => 1,
        SplitContainerModel c => CountLeaves(c.First) + CountLeaves(c.Second),
        _ => 0
    };

    /// <summary>
    /// Returns the first leaf pane in depth-first order (the "primary" pane).
    /// </summary>
    public static SessionPaneModel? FirstLeaf(ISplitContent? root) => root switch
    {
        null => null,
        SessionPaneModel pane => pane,
        SplitContainerModel c => FirstLeaf(c.First),
        _ => null
    };

    // ── Private helpers ──────────────────────────────────────────────

    private static bool IsDirectChild(ISplitContent child, string paneId)
    {
        return child is SessionPaneModel pane &&
               string.Equals(pane.PaneId, paneId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Replaces <paramref name="target"/> container in the tree with
    /// <paramref name="promotion"/> (the surviving child after a removal).
    /// </summary>
    private static ISplitContent PromoteInTree(
        ISplitContent root, SplitContainerModel target, ISplitContent promotion)
    {
        if (ReferenceEquals(root, target))
        {
            return promotion;
        }

        // Walk the tree to find and replace the target container
        ReplaceContainer(root, target, promotion);
        return root;
    }

    private static bool ReplaceContainer(
        ISplitContent node, SplitContainerModel target, ISplitContent replacement)
    {
        if (node is not SplitContainerModel container) return false;

        if (ReferenceEquals(container.First, target))
        {
            container.First = replacement;
            return true;
        }

        if (ReferenceEquals(container.Second, target))
        {
            container.Second = replacement;
            return true;
        }

        // Short-circuit after first match
        return ReplaceContainer(container.First, target, replacement)
            || ReplaceContainer(container.Second, target, replacement);
    }
}
