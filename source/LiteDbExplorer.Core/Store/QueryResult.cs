using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using LiteDB;

namespace LiteDbExplorer.Core
{
    public class QueryResult : IReferenceNode, IJsonSerializerProvider
    {
        private readonly ICultureFormat _cultureFormat;
        private const string EXPR_PATH = @"expr";

        public QueryResult(IEnumerable<BsonValue> bsonValues, ICultureFormat cultureFormat)
        {
            _cultureFormat = cultureFormat;
            InstanceId = Guid.NewGuid().ToString("D");

            Initialize(bsonValues);
        }

        public string InstanceId { get; }

        public bool IsArray { get; private set; }

        public bool IsDocument { get; private set; }

        public int Count { get; private set; }

        public bool HasValue { get; private set; }

        public IEnumerable<BsonValue> Source { get; private set; }

        public BsonArray AsArray { get; private set; }

        public BsonDocument AsDocument { get; private set; }

        public DataTable DataTable => Source.ToDataTable(_cultureFormat);

        public string Serialize(bool decoded = true)
        {
            var json = string.Empty;

            if (IsArray)
            {
                json = JsonSerializer.Serialize(AsArray);
            }
            else if (IsDocument)
            {
                json = JsonSerializer.Serialize(AsDocument);
            }

            return decoded ? EncodingExtensions.DecodeEncodedNonAsciiCharacters(json) : json;
        }

        public void Serialize(TextWriter writer)
        {
            if (IsArray)
            {
                JsonSerializer.Serialize(AsArray, writer);
            }
            else if (IsDocument)
            {
                JsonSerializer.Serialize(AsDocument, writer);
            }
        }

        protected void Initialize(IEnumerable<BsonValue> bsonValues)
        {
            Source = bsonValues;

            if (bsonValues == null)
            {
                HasValue = false;
            }
            else
            {
                HasValue = true;

                var items = bsonValues as BsonValue[] ?? bsonValues.ToArray();

                if (bsonValues is BsonArray bsonArray)
                {
                    IsArray = true;
                    AsArray = bsonArray;
                    Count = bsonArray.Count;
                }
                else if (items.Length == 1 && items[0].IsArray)
                {
                    IsArray = true;
                    AsArray = items[0].AsArray;
                    Count = items[0].AsArray.Count;
                }
                else if (items.Length == 1 && items[0].IsDocument)
                {
                    var bsonDocument = items[0].AsDocument;
                    if (bsonDocument.ContainsKey(EXPR_PATH))
                    {
                        if (bsonDocument[EXPR_PATH].IsArray)
                        {
                            IsArray = true;
                            AsArray = bsonDocument[EXPR_PATH].AsArray;
                            Count = bsonDocument[EXPR_PATH].AsArray.Count;
                        }
                        else
                        {
                            IsDocument = true;
                            AsDocument = new BsonDocument {{@"value", bsonDocument[EXPR_PATH]}};
                            Count = 1;
                        }
                    }
                    else
                    {
                        IsDocument = true;
                        AsDocument = items[0].AsDocument;
                        Count = 1;
                    }
                }
                else
                {
                    IsArray = true;
                    AsArray = new BsonArray(items);
                    Count = items.Length;
                }
            }
        }

    }
}