using System.Linq;
using LiteDB;

namespace LiteDbExplorer.Core
{
    public class FileDocumentReference : DocumentReference
    {
        public FileDocumentReference(BsonDocument document, CollectionReference collection) : base(document, collection)
        {
        }

        public string Filename => LiteDocument["filename"].AsString;

        public string GetIdAsFilename()
        {
            var fileName = LiteDocument["_id"].AsString.Replace('/', ' ').Split('.').FirstOrDefault();

            return System.IO.Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, '_'));
        }

        public void SaveFile(string path)
        {
            var file = GetFileObject();
            file.SaveAs(path);
        }

        public LiteFileInfo<string> GetFileObject()
        {
            return Collection.Database.LiteDatabase.FileStorage.FindById(LiteDocument["_id"]);
        }
    }
}