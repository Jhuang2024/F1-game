using System;
using System.Collections.Generic;

namespace F1Game.UI.Screens.TrackSelect
{
    public sealed class TrackSelectPresenter
    {
        readonly TrackSelectView view;
        IReadOnlyList<TrackCardModel> tracks = Array.Empty<TrackCardModel>();

        public Action<TrackCardModel> OnTrackChosen;
        public Action OnBack;

        public TrackSelectPresenter(TrackSelectView view)
        {
            this.view = view;
            view.RowActivated += index =>
            {
                if (index >= 0 && index < tracks.Count)
                {
                    OnTrackChosen?.Invoke(tracks[index]);
                }
            };
            view.BackButton.Clicked += () => OnBack?.Invoke();
        }

        public void Present(IReadOnlyList<TrackCardModel> trackList)
        {
            tracks = trackList;
            view.Render(tracks);
        }
    }
}
