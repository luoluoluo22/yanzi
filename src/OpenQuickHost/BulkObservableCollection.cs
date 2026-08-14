using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OpenQuickHost;

/// <summary>
/// Wraps a <see cref="List{T}"/> in an <see cref="ObservableCollection{T}"/>-style
/// notifier that emits a single <see cref="NotifyCollectionChangedAction.Reset"/>
/// event for batch updates instead of one event per Add/Remove call. This dramatically
/// reduces the work WPF needs to do when rebuilding long result lists.
/// </summary>
public sealed class BulkObservableCollection<T> : Collection<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private const string CountString = "Count";
    private const string IndexerName = "Item[]";

    public BulkObservableCollection() { }
    public BulkObservableCollection(IEnumerable<T> items) : base(new List<T>(items)) { }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReplaceAll(IEnumerable<T> items)
    {
        var newItems = items as IReadOnlyCollection<T> ?? (List<T>)items;
        ((List<T>)Items).Clear();
        if (newItems.Count > 0)
        {
            ((List<T>)Items).Capacity = newItems.Count;
            ((List<T>)Items).AddRange(newItems);
        }

        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionReset();
    }

    public void Reset(IEnumerable<T>? items = null)
    {
        if (items != null && !ReferenceEquals(items, Items))
        {
            ((List<T>)Items).Clear();
            foreach (var item in items)
            {
                ((List<T>)Items).Add(item);
            }
        }
        else
        {
            ((List<T>)Items).Clear();
        }

        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionReset();
    }

    protected override void ClearItems()
    {
        ((List<T>)Items).Clear();
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionReset();
    }

    protected override void InsertItem(int index, T item)
    {
        ((List<T>)Items).Insert(index, item);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected override void RemoveItem(int index)
    {
        var removed = Items[index];
        ((List<T>)Items).RemoveAt(index);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    protected override void SetItem(int index, T item)
    {
        var original = Items[index];
        ((List<T>)Items)[index] = item;
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, original, item, index));
    }

    public void Move(int oldIndex, int newIndex)
    {
        var items = (List<T>)Items;
        var item = items[oldIndex];
        items.RemoveAt(oldIndex);
        if (newIndex > items.Count)
        {
            newIndex = items.Count;
        }
        items.Insert(newIndex, item);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex));
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        CollectionChanged?.Invoke(this, args);
    }

    private void OnCollectionReset()
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}