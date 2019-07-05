﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using LiteDbExplorer.Presentation.Converters;
using LiteDB;
using LiteDbExplorer.Core;

namespace LiteDbExplorer.Controls
{
    /// <summary>
    ///     Interaction logic for CollectionListView.xaml
    /// </summary>
    public partial class CollectionListView : UserControl
    {
        private readonly BsonValueToStringConverter _bsonValueToStringConverter;

        public static readonly DependencyProperty CollectionReferenceProperty = DependencyProperty.Register(
            nameof(CollectionReference),
            typeof(CollectionReference),
            typeof(CollectionListView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsArrange, OnCollectionReferenceChanged));

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(CollectionListView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
            nameof(SelectedItems),
            typeof(IList),
            typeof(CollectionListView),
            new FrameworkPropertyMetadata(default(IList), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemsChanged));

        public static readonly DependencyProperty DoubleClickCommandProperty = DependencyProperty.Register(
            nameof(DoubleClickCommand), typeof(ICommand), typeof(CollectionListView),
            new PropertyMetadata(default(ICommand)));


        public static readonly DependencyProperty ContentMaxLengthProperty = DependencyProperty.Register(
            nameof(ContentMaxLength), typeof(int), typeof(CollectionListView), new PropertyMetadata(200));

        public int ContentMaxLength
        {
            get => (int) GetValue(ContentMaxLengthProperty);
            set => SetValue(ContentMaxLengthProperty, value);
        }

        private bool _modelHandled;
        private bool _viewHandled;

        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection;
        private bool _stopDoubleClick;
        private bool _listLoaded;

        public CollectionListView()
        {
            InitializeComponent();

            _bsonValueToStringConverter = new BsonValueToStringConverter { MaxLength = ContentMaxLength };

            ListCollectionData.MouseDoubleClick += ListCollectionDataOnMouseDoubleClick;
            ListCollectionData.SelectionChanged += OnListViewSelectionChanged;
            ListCollectionData.Loaded += ListCollectionDataOnLoaded;

            Unloaded += (sender, args) =>
            {
                ListCollectionData.MouseDoubleClick -= ListCollectionDataOnMouseDoubleClick;
                ListCollectionData.SelectionChanged -= OnListViewSelectionChanged;
                ListCollectionData.Loaded -= ListCollectionDataOnLoaded;

                CollectionReference = null;
            };

            ListCollectionData.SetBinding(Selector.SelectedItemProperty, new Binding
            {
                Source = this,
                Path = new PropertyPath(nameof(SelectedItem)),
                Mode = BindingMode.TwoWay
            });
        }

        public CollectionReference CollectionReference
        {
            get => (CollectionReference) GetValue(CollectionReferenceProperty);
            set => SetValue(CollectionReferenceProperty, value);
        }

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public IList SelectedItems
        {
            get => (IList) GetValue(SelectedItemsProperty);
            set => SetValue(SelectedItemsProperty, value);
        }

        public ICommand DoubleClickCommand
        {
            get => (ICommand) GetValue(DoubleClickCommandProperty);
            set => SetValue(DoubleClickCommandProperty, value);
        }

        public ContextMenu ListViewContextMenu
        {
            get => ListCollectionData.ContextMenu;
            set => ListCollectionData.ContextMenu = value;
        }

        private IEnumerable<DocumentReference> DbSelectedItems
        {
            get
            {
                if (ListCollectionData.Visibility == Visibility.Visible)
                {
                    return ListCollectionData.SelectedItems.Cast<DocumentReference>();
                }

                return null;
            }
        }

        private int DbItemsSelectedCount
        {
            get
            {
                if (ListCollectionData?.ItemsSource != null)
                {
                    return ListCollectionData.SelectedItems.Count;
                }

                return 0;
            }
        }

        private void ListCollectionDataOnLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action) (() =>
            {
                var maxWidth = Math.Max(600, ListCollectionData.ActualWidth) / Math.Min(3, GridCollectionData.Columns.Count + 1);
                foreach (var col in GridCollectionData.Columns)
                {
                    col.Width = col.ActualWidth > maxWidth ? maxWidth : Math.Max(100, col.ActualWidth);
                }
            }));
            _listLoaded = true;
        }

        private static void OnCollectionReferenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var collectionListView = d as CollectionListView;
            var collectionReference = e.NewValue as CollectionReference;
            collectionListView?.UpdateGridColumns(collectionReference);
        }

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is CollectionListView collectionListView))
            {
                return;
            }

            if (collectionListView._modelHandled)
            {
                return;
            }

            collectionListView._modelHandled = true;
            collectionListView.SelectItems();
            collectionListView._modelHandled = false;
        }

        public void ScrollIntoItem(object item)
        {
            ListCollectionData.ScrollIntoView(item);
        }

        public void ScrollIntoSelectedItem()
        {
            ListCollectionData.ScrollIntoView(ListCollectionData.SelectedItem);
        }
        
        public void UpdateGridColumns()
        {
            var headers = GridCollectionData.Columns.Select(a => ((GridViewColumnHeader) a.Header).Tag.ToString()).ToArray();
            var keys = CollectionReference.GetDistinctKeys(App.Settings.FieldSortOrder);
            
            foreach (var key in headers.Except(keys))
            {
                RemoveGridColumn(key);
            }

            foreach (var key in keys.Except(headers))
            {
                AddGridColumn(key);
            }
        }

        public void UpdateGridColumns(CollectionReference collectionReference)
        {
            if (ListCollectionData.Items is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= OnListViewItemsChanged;
            }

            ListCollectionData.ItemsSource = null;

            GridCollectionData.Columns.Clear();

            if (collectionReference == null)
            {
                return;
            }
            
            var keys = collectionReference.GetDistinctKeys(App.Settings.FieldSortOrder);

            foreach (var key in keys)
            {
                AddGridColumn(key);
            }

            if (ListCollectionData.Items is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += OnListViewItemsChanged;
            }

            ListCollectionData.ItemsSource = collectionReference.Items;
        }

        public void Find(string text, bool matchCase)
        {
            if (string.IsNullOrEmpty(text) || CollectionReference == null)
            {
                return;
            }

            var skipIndex = -1;
            if (DbItemsSelectedCount > 0)
            {
                skipIndex = CollectionReference.Items.IndexOf(DbSelectedItems.Last());
            }

            foreach (var item in CollectionReference.Items.Skip(skipIndex + 1))
            {
                if (ItemMatchesSearch(text, item, matchCase))
                {
                    SelectedItem = item;
                    ScrollIntoSelectedItem();
                    return;
                }
            }
        }

        public void FindPrevious(string text, bool matchCase)
        {
            if (string.IsNullOrEmpty(text) || CollectionReference == null)
            {
                return;
            }

            var skipIndex = 0;
            if (DbItemsSelectedCount > 0)
            {
                skipIndex = CollectionReference.Items.Count - CollectionReference.Items.IndexOf(DbSelectedItems.Last()) - 1;
            }

            foreach (var item in CollectionReference.Items.Reverse().Skip(skipIndex + 1))
            {
                if (ItemMatchesSearch(text, item, matchCase))
                {
                    SelectedItem = item;
                    ScrollIntoSelectedItem();
                    return;
                }
            }
        }

        private bool ItemMatchesSearch(string matchTerm, DocumentReference document, bool matchCase)
        {
            var stringData = JsonSerializer.Serialize(document.LiteDocument);

            if (matchCase)
            {
                return stringData.IndexOf(matchTerm, 0, StringComparison.InvariantCulture) != -1;
            }
            else
            {
                return stringData.IndexOf(matchTerm, 0, StringComparison.InvariantCultureIgnoreCase) != -1;
            }
        }

        private void ListCollectionDataOnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DoubleClickCommand?.CanExecute(SelectedItem) != true || _stopDoubleClick)
            {
                return;
            }

            DoubleClickCommand?.Execute(SelectedItem);
        }

        private void OnListViewItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_viewHandled)
            {
                return;
            }

            if (ListCollectionData.Items.SourceCollection == null)
            {
                return;
            }

            SelectItems();
        }

        private void OnListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewHandled)
            {
                return;
            }

            if (ListCollectionData.Items.SourceCollection == null)
            {
                return;
            }

            SelectedItems = ListCollectionData.SelectedItems.Cast<DocumentReference>().ToList();
        }

        private void SelectItems()
        {
            _viewHandled = true;
            /*SelectedItems.Clear();
            if (SelectedItems != null)
            {
                foreach (var item in SelectedItems)
                {
                    SelectedItems.Add(item);
                }
            }*/
            _viewHandled = false;
        }

        private void AddGridColumn(string key)
        {
            var column = new GridViewColumn
            {
                Header = new GridViewColumnHeader
                {
                    Content = key,
                    Tag = key,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                },
                DisplayMemberBinding = new Binding
                {
                    Path = new PropertyPath($"LiteDocument[{key}]"),
                    Mode = BindingMode.OneWay,
                    Converter = _bsonValueToStringConverter
                },
                HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate
            };
            
            GridCollectionData.Columns.Add(column);

            if (_listLoaded)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action) (() =>
                {
                    var maxWidth = Math.Max(600, ListCollectionData.ActualWidth) / Math.Min(3, GridCollectionData.Columns.Count + 1);
                    column.Width = column.ActualWidth > maxWidth ? maxWidth : Math.Max(100, column.ActualWidth);

                }));
            }
        }
        
        private void RemoveGridColumn(string key)
        {
            GridViewColumn columnToRemove = null;
            foreach (var gridViewColumn in GridCollectionData.Columns)
            {
                if (gridViewColumn.Header is GridViewColumnHeader header && header.Tag.Equals(key))
                {
                    columnToRemove = gridViewColumn;
                }
            }

            if (columnToRemove != null)
            {
                GridCollectionData.Columns.Remove(columnToRemove);
            } 
        }

        private void ListCollectionData_OnHeaderClick(object sender, RoutedEventArgs e)
        {
            _stopDoubleClick = true;

            if (e.OriginalSource is GridViewColumnHeader headerClicked)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    ListSortDirection direction;

                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Ascending;
                    }
                    else
                    {
                        direction = _lastDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                    }

                    var sortBy = headerClicked.Tag.ToString();

                    Sort(sortBy, direction);

                    if (direction == ListSortDirection.Ascending)
                    {
                        headerClicked.Column.HeaderTemplate =
                            Resources["HeaderTemplateArrowUp"] as DataTemplate;
                    }
                    else
                    {
                        headerClicked.Column.HeaderTemplate =
                            Resources["HeaderTemplateArrowDown"] as DataTemplate;
                    }

                    // Remove arrow from previously sorted header  
                    if (_lastHeaderClicked != null && _lastHeaderClicked != headerClicked)
                    {
                        _lastHeaderClicked.Column.HeaderTemplate = Resources["HeaderTemplate"] as DataTemplate;
                    }

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }

            _stopDoubleClick = false;
        }

        // Sort code
        private void Sort(string sortBy, ListSortDirection direction)  
        {  
            var dataView =  (ListCollectionView)CollectionViewSource.GetDefaultView(ListCollectionData.ItemsSource);
            dataView.CustomSort = new SortBsonValue(sortBy, direction == ListSortDirection.Descending);
        }
    }
}