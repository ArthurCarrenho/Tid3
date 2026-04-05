using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace TidalUi3.Controls
{
    public sealed partial class TrackListItemControl : UserControl
    {
        public static readonly DependencyProperty AlbumColumnWidthProperty =
            DependencyProperty.Register("AlbumColumnWidth", typeof(GridLength), typeof(TrackListItemControl), new PropertyMetadata(new GridLength(0.5, GridUnitType.Star), OnAlbumColumnWidthChanged));

        public GridLength AlbumColumnWidth
        {
            get => (GridLength)GetValue(AlbumColumnWidthProperty);
            set => SetValue(AlbumColumnWidthProperty, value);
        }

        private static void OnAlbumColumnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListItemControl control)
            {
                if (control.ShowAlbum)
                {
                    control.AlbumColumnDef.Width = (GridLength)e.NewValue;
                }
            }
        }

        public static readonly DependencyProperty ShowAlbumProperty =
            DependencyProperty.Register("ShowAlbum", typeof(bool), typeof(TrackListItemControl), new PropertyMetadata(true, OnShowAlbumChanged));

        public bool ShowAlbum
        {
            get => (bool)GetValue(ShowAlbumProperty);
            set => SetValue(ShowAlbumProperty, value);
        }

        private static void OnShowAlbumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListItemControl control)
            {
                bool show = (bool)e.NewValue;
                control.AlbumColumnDef.Width = show ? control.AlbumColumnWidth : new GridLength(0);
                control.AlbumText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public static readonly DependencyProperty ShowHeartProperty =
            DependencyProperty.Register("ShowHeart", typeof(bool), typeof(TrackListItemControl), new PropertyMetadata(false, OnShowHeartChanged));

        public bool ShowHeart
        {
            get => (bool)GetValue(ShowHeartProperty);
            set => SetValue(ShowHeartProperty, value);
        }

        private static void OnShowHeartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListItemControl control)
            {
                bool show = (bool)e.NewValue;
                if(show) {
                    control.HeartColumnDef.Width = new GridLength(36);
                    control.HeartIcon.Visibility = Visibility.Visible;
                } else {
                    control.HeartColumnDef.Width = new GridLength(0);
                    control.HeartIcon.Visibility = Visibility.Collapsed;
                }
            }
        }

        public static readonly DependencyProperty ShowRowNumberProperty =
            DependencyProperty.Register("ShowRowNumber", typeof(bool), typeof(TrackListItemControl), new PropertyMetadata(true, OnShowRowNumberChanged));

        public bool ShowRowNumber
        {
            get => (bool)GetValue(ShowRowNumberProperty);
            set => SetValue(ShowRowNumberProperty, value);
        }

        private static void OnShowRowNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListItemControl control)
            {
                bool show = (bool)e.NewValue;
                control.RowNumberColumnDef.Width = show ? new GridLength(32) : new GridLength(0);
                control.RowNumberText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public TrackListItemControl()
        {
            this.InitializeComponent();
            HeartColumnDef.Width = new GridLength(0);
        }
    }
}
