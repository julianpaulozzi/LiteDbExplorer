using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using LiteDB;
using PropertyChanging;

namespace LiteDbExplorer.Core
{
    public class CollectionReference : ReferenceNode<CollectionReference>
    {
        private ObservableCollection<DocumentReference> _items;

        public CollectionReference(string name, DatabaseReference database)
        {
            Name = name;
            Database = database;
        }

        public string Name { get; set; }

        [AlsoNotifyFor(nameof(LiteCollection))]
        public DatabaseReference Database { get; set; }

        public ILiteCollection<BsonDocument> LiteCollection => Database.LiteDatabase.GetCollection(Name);

        public ObservableCollection<DocumentReference> Items
        {
            get
            {
                if (_items == null)
                {
                    _items = new ObservableCollection<DocumentReference>();
                    // TODO: Lazy load on non UI thread
                    foreach (var item in GetAllItem(LiteCollection))
                    {
                        _items.Add(item);
                    }
                    _items.CollectionChanged += OnDocumentsCollectionChanged;
                }

                return _items;
            }
            set
            {
                OnPropertyChanging(nameof(Items));
                if (_items != null)
                {
                    _items.CollectionChanged -= OnDocumentsCollectionChanged;
                }
                _items = value;
                if (_items != null)
                {
                    _items.CollectionChanged += OnDocumentsCollectionChanged;
                }

                OnPropertyChanged(nameof(Items));
            }
        }

        public IEnumerable<DocumentReference> this[string name]
        {
            get
            {
                return Items == null ? Enumerable.Empty<DocumentReference>() : Items.Where(p => p.LiteDocument.ContainsKey(name));
            }
        }

        public bool IsFilesOrChunks => this.IsFilesOrChunksCollection();

        public event EventHandler<CollectionReferenceChangedEventArgs<DocumentReference>> DocumentsCollectionChanged;

        public virtual void UpdateDocument(DocumentReference document)
        {
            LiteCollection.Update(document.LiteDocument);

            document.OnReferenceChanged(ReferenceNodeChangeAction.Update, document);

            OnDocumentsCollectionChanged(ReferenceNodeChangeAction.Update, new[] {document});
        }

        public virtual void RemoveDocument(DocumentReference document)
        {
            LiteCollection.Delete(document.LiteDocument["_id"]);
            Items.Remove(document);
        }

        public virtual DocumentReference AddDocument(BsonDocument document)
        {
            LiteCollection.Insert(document);
            var newDoc = new DocumentReference(document, this);
            Items.Add(newDoc);
            return newDoc;
        }

        public virtual void Refresh()
        {
            OnPropertyChanging(nameof(Items));

            if (_items == null)
            {
                _items = new ObservableCollection<DocumentReference>();
            }
            else
            {
                _items.Clear();
            }

            foreach (var item in GetAllItem(LiteCollection))
            {
                _items.Add(item);
            }

            OnPropertyChanged(nameof(Items));
        }

        public IReadOnlyList<string> GetDistinctKeys(FieldSortOrder sortOrder = FieldSortOrder.Original)
        {
            return Items.SelectAllDistinctKeys(sortOrder).ToList();
        }

        protected virtual IEnumerable<DocumentReference> GetAllItem(ILiteCollection<BsonDocument> liteCollection)
        {
            /*if (IsFilesOrChunks)
            {
                return LiteCollection.FindAll().Select(bsonDocument => new FileDocumentReference(bsonDocument, this));
            }*/

            return LiteCollection.FindAll().Select(bsonDocument => new DocumentReference(bsonDocument, this));
        }

        protected virtual void OnDocumentsCollectionChanged(ReferenceNodeChangeAction action,
            IEnumerable<DocumentReference> items)
        {
            DocumentsCollectionChanged?.Invoke(this,
                new CollectionReferenceChangedEventArgs<DocumentReference>(action, items));
        }

        protected override void Dispose(bool disposing)
        {
            Items = null;
            Database = null;
        }

        private void OnDocumentsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                {
                    if (e.NewItems != null)
                    {
                        BroadcastChanges(ReferenceNodeChangeAction.Add, e.NewItems.Cast<DocumentReference>());
                    }
                    break;
                }
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Reset:
                {
                    if (e.OldItems != null)
                    {
                        BroadcastChanges(ReferenceNodeChangeAction.Remove, e.OldItems.Cast<DocumentReference>());
                    }
                    break;
                }
            }
        }

        private void BroadcastChanges(ReferenceNodeChangeAction action, IEnumerable<DocumentReference> items)
        {
            foreach (var documentReference in items)
            {
                documentReference.OnReferenceChanged(action, documentReference);
            }

            OnDocumentsCollectionChanged(action, items);
        }
    }
}