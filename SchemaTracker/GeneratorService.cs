using System;
using Xamasoft.JsonClassGenerator;
using Xamasoft.JsonClassGenerator.CodeWriters;

namespace SchemaTracker
{
    public static class GeneratorService
    {
        private static readonly ILog Log = LogManager.GetLog(typeof(GeneratorService));

        internal static bool GenerateClass(SchemaService.EconApp app, string schemaDirectory, string json)
        {
            try
            {
                var gen = CreateNewGenerator(schemaDirectory, app.SchemaName, json);
                gen.GenerateClasses();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
            return true;
        }

        private static JsonClassGenerator CreateNewGenerator(string schemaDirectory, string schemaFileName, string json)
        {
            var gen = new JsonClassGenerator
            {
                InternalVisibility = false,
                SingleFile = true,
                Namespace = "SteamEcon.Schema." + schemaFileName,
                CodeWriter = new CSharpCodeWriter(),
                NoHelperClass = true,
                UsePascalCase = true,
                UseNestedClasses = false,
                ApplyObfuscationAttributes = false,
                ExamplesInDocumentation = false,
                UseProperties = true,

                SecondaryNamespace = schemaFileName,
                TargetFolder = schemaDirectory,
                MainClass = schemaFileName,
                Example = json
            };

            return gen;
        }
    }
}