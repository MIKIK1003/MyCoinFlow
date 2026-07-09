using System.Collections.Generic;
using MyCoinFlow.Models;

namespace MyCoinFlow.Services
{
    public interface ICreditCardImportRepository
    {
        IList<ImportSchema> ImportSchemasGetAll();
        ImportSchema ImportSchemaInsert(ImportSchema s);
        void ImportSchemaDelete(int schemaId);

        IList<FieldMapping> FieldMappingsGetBySchema(int schemaId);
        void FieldMappingsReplace(int schemaId, List<FieldMapping> items);
        void FieldMappingsDeleteBySchema(int schemaId);
        void ImportSchemaUpdateName(int schemaId, string name);

    }
}
