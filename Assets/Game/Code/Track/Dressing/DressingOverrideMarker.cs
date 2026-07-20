using UnityEngine;

namespace F1Game.Track.Dressing
{
    /// <summary>
    /// Tags a manually-placed object so the procedural dressing regeneration will
    /// never delete it. Put this on any hand-authored trackside object that lives
    /// under the dressing hierarchy; <see cref="TrackDressing.Regenerate"/> rescues
    /// every marker-carrying object (and the "Overrides" subroot) across a rebuild.
    /// </summary>
    public sealed class DressingOverrideMarker : MonoBehaviour
    {
    }
}
