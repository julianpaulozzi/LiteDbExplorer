using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Humanizer;
using LiteDB;

namespace LiteDbExplorer.Core
{
    public static class LiteDbReferenceExtensions
    {
        public static IDictionary<string, BsonType> SelectFirstDistinctKeyTypePair(this IEnumerable<DocumentReference> documents)
        {
            return documents
                .SelectMany(p => p.LiteDocument.RawValue)
                .GroupBy(p => p.Key)
                .Select(p => new {p.First().Key, Value = p.First().Value.Type})
                .ToDictionary(p => p.Key, p => p.Value);
        }

        public static IEnumerable<string> SelectAllDistinctKeys(this IEnumerable<DocumentReference> documents, FieldSortOrder sortOrder = FieldSortOrder.Original)
        {
            if (documents == null)
            {
                return Enumerable.Empty<string>();
            }

            var result = documents
                .SelectMany(p => p.LiteDocument.Keys)
                .Distinct(StringComparer.InvariantCulture);

            if (sortOrder == FieldSortOrder.Alphabetical)
            {
                result = result.OrderBy(_ => _);
            }

            return result;
        }

        public static IEnumerable<string> SelectAllDistinctKeys(this IEnumerable<BsonDocument> documents,
            FieldSortOrder sortOrder = FieldSortOrder.Original)
        {
            if (documents == null)
            {
                return Enumerable.Empty<string>();
            }

            var result = documents
                .SelectMany(p => p.Keys)
                .Distinct(StringComparer.InvariantCulture);

            if (sortOrder == FieldSortOrder.Alphabetical)
            {
                result = result.OrderBy(_ => _);
            }

            return result;
        }

        public static bool IsFilesOrChunksCollection(this CollectionReference reference)
        {
            if (reference == null)
            {
                return false;
            }

            return reference.Name == @"_files" || reference.Name == @"_chunks";
        }

        public static bool HasAnyDocumentsReference(this IEnumerable<DocumentReference> documentReferences, DocumentTypeFilter filter = DocumentTypeFilter.All)
        {
            if (documentReferences == null)
            {
                return false;
            }

            if (filter == DocumentTypeFilter.All)
            {
                return documentReferences.Any();
            }

            return documentReferences
                .Where(p => p.Collection != null)
                .All(p => filter == DocumentTypeFilter.File ? p.Collection.IsFilesOrChunks : !p.Collection.IsFilesOrChunks);
        }
        
        public static bool IsFilesCollection(this CollectionReference collectionReference)
        {
            return collectionReference != null && collectionReference.IsFilesOrChunks;
        }

        public static string ToDisplayName(this DocumentReference documentReference)
        {
            if (documentReference == null)
            {
                return string.Empty;
            }
            
            return string.Join(" - ", documentReference.Collection?.Name, documentReference.LiteDocument["_id"].RawValue.ToString());
        }

        public static string ToDisplayValue(this BsonValue bsonValue, int? maxLength = null, ICultureFormat cultureFormat = null)
        {
            if (bsonValue == null)
            {
                return string.Empty;
            }

            if (cultureFormat == null)
            {
                cultureFormat = DefaultCultureFormat.Invariant;
            }

            var cultureInfo = cultureFormat.Culture ?? CultureInfo.InvariantCulture;

            try
            {
                string result;

                switch (bsonValue.Type)
                {
                    case BsonType.MinValue:
                        result = @"-∞";
                        break;
                    case BsonType.MaxValue:
                        result = @"+∞";
                        break;
                    case BsonType.Boolean:
                        result = bsonValue.AsBoolean.ToString(cultureInfo).ToLower();
                        break;
                    case BsonType.DateTime:
                        result = !string.IsNullOrEmpty(cultureFormat.DateTimeFormat) ? bsonValue.AsDateTime.ToString(cultureFormat.DateTimeFormat, cultureInfo) : bsonValue.AsDateTime.ToString(cultureInfo);
                        break;
                    case BsonType.Null:
                        result = "(null)";
                        break;
                    case BsonType.Binary:
                        result = Convert.ToBase64String(bsonValue.AsBinary);
                        break;
                    case BsonType.Int32:
                    case BsonType.Int64:
                    case BsonType.Double:
                    case BsonType.Decimal:
                    case BsonType.String:
                    case BsonType.ObjectId:
                    case BsonType.Guid:
                        result = Convert.ToString(bsonValue.RawValue, cultureInfo);
                        break;
                    case BsonType.Document:
                        result = @"[Document]";
                        break;
                    case BsonType.Array:
                        result = @"[Array]";
                        break;
                    default:
                        result = JsonSerializer.Serialize(bsonValue);
                        break;
                }

                if (maxLength.HasValue)
                {
                    return result?.Truncate(maxLength.Value);
                }

                return result;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}