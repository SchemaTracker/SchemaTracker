using System.Collections.Generic;
using System.IO;

namespace SchemaTracker
{
    public class Helper
    {
        public static readonly HashSet<SchemaService.EconApp> DefaultAppIds = new HashSet<SchemaService.EconApp>()
        {
            new SchemaService.EconApp(440, "Team Fortress 2", "TF2"),
            new SchemaService.EconApp(570, "Dota 2", "Dota2"),
            new SchemaService.EconApp(620, "Portal 2", "Portal2"),
            new SchemaService.EconApp(730, "Counter-Strike: Global Offensive", "CSGO"),
            new SchemaService.EconApp(816,"Dota 2 Internal Test","Dota2InternalTest"),
            new SchemaService.EconApp(841, "Portal 2 Beta", "Portal2Beta"),
            new SchemaService.EconApp(205790, "Dota 2 (Beta) Test","Dota2BetaTest")
        };

        public static string CombinePaths(bool combine, params string[] paths)
        {
            return combine ? Path.Combine(Path.GetTempPath(), Path.Combine(paths)) : Path.Combine(paths);
        }
    }
}
