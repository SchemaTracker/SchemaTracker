using System;

namespace SchemaTracker
{
    public partial class SchemaService
    {
        public class EconApp
        {
            public int Id { get; private set; }

            public string Name { get; private set; }

            public string SchemaFileName { get; private set; }

            public string SchemaClassFileName { get; private set; }
            public string SchemaName { get; private set; }

            public string SchemaUrl { get; internal set; }

            public DateTime LastModified { get; internal set; }

            public string ClientSchemaUrl { get; internal set; }

            public string ClientSchemaFileName { get; private set; }

            public EconApp(int id, string name, string schemaName)
            {
                this.Id = id;
                this.Name = name;
                this.SchemaName = schemaName + "Schema";
                this.SchemaClassFileName = schemaName + "Schema.cs";
                this.SchemaFileName = "schema_" + id + ".json";
                this.ClientSchemaFileName = "clientschema_" + id + ".vdf";
            }
        }
    }
}
