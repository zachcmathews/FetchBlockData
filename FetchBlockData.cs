// SPDX-License-Identifier: Apache-2.0
// See LICENSE in the project root.

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(FetchBlockData.Commands))]

namespace FetchBlockData;

public class Commands
{
    [CommandMethod("FETCHBLOCKDATA", CommandFlags.Modal)]
    public static void Run()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document is null)
            return;

        var editor = document.Editor;
        try
        {
            var options = new PromptOpenFileOptions("Select a FetchBlockData YAML configuration")
            {
                Filter = "YAML files (*.yaml;*.yml)|*.yaml;*.yml"
            };
            var selection = editor.GetFileNameForOpen(options);
            if (selection.Status == PromptStatus.Cancel)
                return;
            if (selection.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nFETCHBLOCKDATA could not select a configuration file.");
                return;
            }

            var config = Configuration.Load(selection.StringResult);
            var rows = SqliteSource.ReadRows(config);
            var result = DrawingSync.Apply(document.Database, config, rows);

            editor.WriteMessage($"\nFETCHBLOCKDATA updated {result.ChangedAttributes} attribute(s) on {result.ChangedInserts} block insert(s).");
            foreach (var skip in result.Skips)
                editor.WriteMessage($"\n  Skipped {skip.Value} {skip.Key}.");
        }
        catch (System.Exception exception)
        {
            editor.WriteMessage($"\nFETCHBLOCKDATA failed. {exception.Message}");
        }
    }
}
