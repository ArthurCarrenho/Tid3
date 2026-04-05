using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections;

namespace TidalUi3.Controls
{
    public sealed partial class CarouselSectionControl : UserControl
    {
        public event ItemClickEventHandler? ItemClick;

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(CarouselSectionControl), new PropertyMetadata(string.Empty, OnTitleChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CarouselSectionControl control)
                control.TitleBlock.Text = (string)e.NewValue;
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(object), typeof(CarouselSectionControl), new PropertyMetadata(null, OnItemsSourceChanged));

        public object ItemsSource
        {
            get => GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CarouselSectionControl control)
                control.ItemsGrid.ItemsSource = e.NewValue as IEnumerable;
        }

        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register("ItemTemplate", typeof(DataTemplate), typeof(CarouselSectionControl), new PropertyMetadata(null, OnItemTemplateChanged));

        public DataTemplate ItemTemplate
        {
            get => (DataTemplate)GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CarouselSectionControl control)
                control.ItemsGrid.ItemTemplate = (DataTemplate)e.NewValue;
        }

        public CarouselSectionControl()
        {
            this.InitializeComponent();
            ItemsGrid.ItemClick += (s, e) => ItemClick?.Invoke(s, e);
        }
    }
}
