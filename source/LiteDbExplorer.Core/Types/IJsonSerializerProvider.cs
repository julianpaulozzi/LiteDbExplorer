using System.IO;

namespace LiteDbExplorer.Core
{
    public interface IJsonSerializerProvider
    {
        string Serialize(bool decoded);
        void Serialize(TextWriter writer);
    }
}