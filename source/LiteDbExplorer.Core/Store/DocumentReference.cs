using System.IO;
using System.Runtime.CompilerServices;
using LiteDB;

namespace LiteDbExplorer.Core
{
    public class DocumentReference : ReferenceNode<DocumentReference>, IJsonSerializerProvider
    {
        public DocumentReference(BsonDocument document, CollectionReference collection)
        {
            LiteDocument = document;
            Collection = collection;
        }

        public BsonDocument LiteDocument { get; set; }

        public CollectionReference Collection { get; set; }

        [IndexerName(@"Item")]
        public BsonValue this[string name]
        {
            get => LiteDocument[name];
            set => LiteDocument[name] = value;
        }

        public bool ContainsReference(CollectionReference collectionReference)
        {
            if (Collection == null)
            {
                return false;
            }

            return Collection.InstanceId.Equals(collectionReference?.InstanceId);
        }

        public BsonDocument FindFromCollectionRef()
        {
            return Collection.LiteCollection.FindById(LiteDocument["_id"]);
        }

        public void RemoveSelf()
        {
            Collection?.RemoveDocument(this);
        }

        public bool TryGetValue(string key, out BsonValue value)
        {
            if (LiteDocument.ContainsKey(key))
            {
                value = LiteDocument[key];
                return true;
            }

            value = null;
            return false;
        }

        public void NotifyDocumentChanged()
        {
            OnPropertyChanged(nameof(LiteDocument));
            OnPropertyChanged(@"Item[]");
        }

        protected override void Dispose(bool disposing)
        {
            LiteDocument = null;
            Collection = null;
        }

        public string Serialize(bool decoded = true)
        {
            return decoded ? LiteDocument.SerializeDecoded() : JsonSerializer.Serialize(LiteDocument);
        }

        public void Serialize(TextWriter writer)
        {
            JsonSerializer.Serialize(LiteDocument, writer);
        }
    }
}