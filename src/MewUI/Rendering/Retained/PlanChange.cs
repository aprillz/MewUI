namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Tells a plan that only gained or lost children and drawings from one that changed in any other way.
/// When children or drawings were only put in or taken out, what is left stands where it stood and
/// looks as it looked, so only what was taken out has to be repainted here; what was put in repaints
/// itself as it is recorded. Any other difference (another order, another clip or transform, a child
/// that moved into or out of a scope) changes what the rest looks like too.
/// </summary>
internal static class PlanChange
{
    /// <summary>
    /// Walks both plans in order. Returns false when they differ by more than insertions and deletions
    /// of children and drawings; otherwise reports each entry of the old plan that the new one lacks.
    /// </summary>
    internal static bool TryFindRemoved(CompositionPlan oldPlan, CompositionPlan newPlan, List<CompositionEntry> removed)
    {
        removed.Clear();
        var oldEntries = oldPlan.Entries;
        var newEntries = newPlan.Entries;
        if (oldPlan.IsUnsupported != newPlan.IsUnsupported)
        {
            return false;
        }

        if (!Walk(oldEntries, 0, oldEntries.Length, newEntries, 0, newEntries.Length, removed))
        {
            return false;
        }

        // Taken out of one scope and put into another is a move, which changes how the child looks.
        for (int index = 0; index < removed.Count; index++)
        {
            if (Contains(newEntries, 0, newEntries.Length, removed[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Walk(
        CompositionEntry[] oldEntries,
        int oldIndex,
        int oldEnd,
        CompositionEntry[] newEntries,
        int newIndex,
        int newEnd,
        List<CompositionEntry> removed)
    {
        while (oldIndex < oldEnd || newIndex < newEnd)
        {
            bool hasOld = oldIndex < oldEnd;
            bool hasNew = newIndex < newEnd;
            if (hasOld && hasNew && IsSame(in oldEntries[oldIndex], in newEntries[newIndex]))
            {
                if (IsScope(in oldEntries[oldIndex]))
                {
                    // What a scope encloses is compared on its own, so nothing can slip across its end.
                    int oldInner = oldIndex + 1;
                    int newInner = newIndex + 1;
                    int oldScopeEnd = oldInner + oldEntries[oldIndex].ScopeLength;
                    int newScopeEnd = newInner + newEntries[newIndex].ScopeLength;
                    if (!Walk(oldEntries, oldInner, oldScopeEnd, newEntries, newInner, newScopeEnd, removed))
                    {
                        return false;
                    }

                    oldIndex = oldScopeEnd;
                    newIndex = newScopeEnd;
                }
                else
                {
                    oldIndex++;
                    newIndex++;
                }
            }
            else if (hasOld && IsLeaf(in oldEntries[oldIndex]) && !Contains(newEntries, newIndex, newEnd, in oldEntries[oldIndex]))
            {
                removed.Add(oldEntries[oldIndex]);
                oldIndex++;
            }
            else if (hasNew && IsLeaf(in newEntries[newIndex]) && !Contains(oldEntries, oldIndex, oldEnd, in newEntries[newIndex]))
            {
                newIndex++;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLeaf(in CompositionEntry entry)
        => entry.Kind is CompositionEntryKind.Child or CompositionEntryKind.Content;

    private static bool IsScope(in CompositionEntry entry) => !IsLeaf(in entry);

    private static bool Contains(CompositionEntry[] entries, int start, int end, in CompositionEntry entry)
    {
        for (int index = start; index < end; index++)
        {
            if (IsSame(in entries[index], in entry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSame(in CompositionEntry first, in CompositionEntry second)
        => first.Kind == second.Kind &&
           first.SlotIndex == second.SlotIndex &&
           ReferenceEquals(first.Child, second.Child) &&
           first.Rect == second.Rect &&
           first.RadiusX == second.RadiusX &&
           first.RadiusY == second.RadiusY &&
           first.Matrix == second.Matrix;
}
