using System;
using System.Collections.Generic;

namespace XRayHands.Analytics
{
    /// <summary>
    /// The single source of truth for linked selection between the specimen and the chart.
    /// Views never talk to each other: the specimen, the chart, the probe and the link lines
    /// all read this model and listen to <see cref="Changed"/>. That is what makes the
    /// brushing-and-linking bidirectional without circular updates.
    /// </summary>
    public sealed class SelectionModel
    {
        private readonly HashSet<int> _brushed = new HashSet<int>();

        /// <summary>Currently focused defect id (0 = none). Set by pinching a dot or a defect.</summary>
        public int FocusedId { get; private set; }

        /// <summary>Defects inside the current chart brush (empty = no brush).</summary>
        public IReadOnlyCollection<int> Brushed => _brushed;

        /// <summary>True when a brush is active.</summary>
        public bool HasBrush => _brushed.Count > 0;

        /// <summary>Raised once after any change (focus or brush).</summary>
        public event Action Changed;

        /// <summary>Focus one defect (0 clears). No event if unchanged.</summary>
        public void Focus(int id)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (id == FocusedId) return;
            FocusedId = id;
            Changed?.Invoke();
        }

        /// <summary>Replace the brushed set. No event if the set is unchanged.</summary>
        public void SetBrush(IEnumerable<int> ids)
        {
            var next = new HashSet<int>(ids);
            if (next.SetEquals(_brushed)) return;
            _brushed.Clear();
            _brushed.UnionWith(next);
            Changed?.Invoke();
        }

        /// <summary>True if <paramref name="id"/> is focused or brushed.</summary>
        public bool IsSelected(int id) => id != 0 && (id == FocusedId || _brushed.Contains(id));

        /// <summary>True if anything is selected at all.</summary>
        public bool HasSelection => FocusedId != 0 || _brushed.Count > 0;

        /// <summary>Clears focus and brush (one event).</summary>
        public void Clear()
        {
            if (!HasSelection) return;
            FocusedId = 0;
            _brushed.Clear();
            Changed?.Invoke();
        }
    }
}
