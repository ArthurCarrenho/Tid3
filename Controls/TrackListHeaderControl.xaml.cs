using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class TrackListHeaderControl : UserControl
    {
        public static readonly DependencyProperty AlbumColumnWidthProperty =
            DependencyProperty.Register("AlbumColumnWidth", typeof(GridLength), typeof(TrackListHeaderControl), new PropertyMetadata(new GridLength(0.5, GridUnitType.Star), OnAlbumColumnWidthChanged));

        public GridLength AlbumColumnWidth
        {
            get => (GridLength)GetValue(AlbumColumnWidthProperty);
            set => SetValue(AlbumColumnWidthProperty, value);
        }

        private static void OnAlbumColumnWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListHeaderControl control && control.ShowAlbum)
                control.AlbumColumnDef.Width = (GridLength)e.NewValue;
        }

        public static readonly DependencyProperty ShowAlbumProperty =
            DependencyProperty.Register("ShowAlbum", typeof(bool), typeof(TrackListHeaderControl), new PropertyMetadata(true, OnShowAlbumChanged));

        public bool ShowAlbum
        {
            get => (bool)GetValue(ShowAlbumProperty);
            set => SetValue(ShowAlbumProperty, value);
        }

        private static void OnShowAlbumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListHeaderControl control)
            {
                bool show = (bool)e.NewValue;
                control.AlbumColumnDef.Width = show ? control.AlbumColumnWidth : new GridLength(0);
                control.AlbumHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public static readonly DependencyProperty ShowHeartProperty =
            DependencyProperty.Register("ShowHeart", typeof(bool), typeof(TrackListHeaderControl), new PropertyMetadata(false, OnShowHeartChanged));

        public bool ShowHeart
        {
            get => (bool)GetValue(ShowHeartProperty);
            set => SetValue(ShowHeartProperty, value);
        }

        private static void OnShowHeartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListHeaderControl control)
                control.HeartColumnDef.Width = (bool)e.NewValue ? new GridLength(36) : new GridLength(0);
        }

        public static readonly DependencyProperty ShowRowNumberProperty =
            DependencyProperty.Register("ShowRowNumber", typeof(bool), typeof(TrackListHeaderControl), new PropertyMetadata(true, OnShowRowNumberChanged));

        public bool ShowRowNumber
        {
            get => (bool)GetValue(ShowRowNumberProperty);
            set => SetValue(ShowRowNumberProperty, value);
        }

        private static void OnShowRowNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TrackListHeaderControl control)
            {
                bool show = (bool)e.NewValue;
                control.RowNumberColumnDef.Width = show ? new GridLength(32) : new GridLength(0);
                control.RowNumberHeader.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public TrackListHeaderControl()
        {
            this.InitializeComponent();
        }
    }
}
