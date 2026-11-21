// SPDX-License-Identifier: Apache-2.0
// See LICENSE in the project root.

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace FetchBlockData;

internal sealed class SyncResult
{
    public int ChangedInserts { get; set; }
    public int ChangedAttributes { get; set; }
    public Dictionary<string, int> Skips { get; } = new(StringComparer.Ordinal);

    public void Skip(string reason)
    {
        Skips.TryGetValue(reason, out var count);
        Skips[reason] = count + 1;
    }
}

internal static class DrawingSync
{
    public static SyncResult Apply(
        Database database, Configuration config, Dictionary<string, Dictionary<string, string?[]>> lookup)
    {
        var rules = config.Rules.ToDictionary(entry => entry.Block.Name, Configuration.CadNameComparer);
        var result = new SyncResult();
        var blockReferenceClass = RXObject.GetClass(typeof(BlockReference));

        using var transaction = database.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId recordId in blockTable)
        {
            var space = (BlockTableRecord)transaction.GetObject(recordId, OpenMode.ForRead);
            if (!space.IsLayout)
                continue;

            foreach (ObjectId entityId in space)
            {
                if (entityId.IsErased || !entityId.ObjectClass.IsDerivedFrom(blockReferenceClass))
                    continue;

                if (transaction.GetObject(entityId, OpenMode.ForRead) is BlockReference insert)
                    UpdateInsert(transaction, insert, rules, lookup, result);
            }
        }

        transaction.Commit();
        return result;
    }

    private static void UpdateInsert(
        Transaction transaction,
        BlockReference insert,
        Dictionary<string, Entry> rules,
        Dictionary<string, Dictionary<string, string?[]>> lookup,
        SyncResult result)
    {
        var definitionId = insert.IsDynamicBlock ? insert.DynamicBlockTableRecord : insert.BlockTableRecord;
        var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
        if (!rules.TryGetValue(definition.Name, out var entry))
            return;

        if (definition.IsFromExternalReference || definition.IsFromOverlayReference)
        {
            result.Skip("xref inserts");
            return;
        }
        if (IsLayerLocked(transaction, insert))
        {
            result.Skip("inserts on locked layers");
            return;
        }

        var attributes = new Dictionary<string, AttributeReference?>(Configuration.CadNameComparer);
        foreach (ObjectId attributeId in insert.AttributeCollection)
        {
            if (attributeId.IsErased ||
                transaction.GetObject(attributeId, OpenMode.ForRead) is not AttributeReference attribute)
                continue;

            attributes[attribute.Tag] = attributes.ContainsKey(attribute.Tag) ? null : attribute;
        }

        if (!attributes.TryGetValue(entry.Block.Key, out var keyAttribute))
        {
            result.Skip("inserts with a missing key attribute");
            return;
        }
        if (keyAttribute is null)
        {
            result.Skip("inserts with an ambiguous key attribute");
            return;
        }

        var key = ReadText(keyAttribute).Trim();
        if (key.Length == 0)
        {
            result.Skip("inserts with a blank key");
            return;
        }
        if (!lookup[entry.Block.Name].TryGetValue(key, out var values))
        {
            result.Skip("inserts with no matching database row");
            return;
        }

        var changed = false;
        for (var index = 0; index < entry.Mappings.Count; index++)
        {
            var tag = entry.Mappings[index].Block.Attribute;
            if (!attributes.TryGetValue(tag, out var target))
            {
                result.Skip("missing target attributes");
                continue;
            }
            if (target is null)
            {
                result.Skip("ambiguous target attributes");
                continue;
            }
            if (target.IsConstant || target.HasFields || IsLayerLocked(transaction, target))
            {
                result.Skip("constant, field-backed, or locked target attributes");
                continue;
            }

            var value = values[index];
            if (value is null)
            {
                result.Skip("null or binary source values");
                continue;
            }
            if (HasText(target, value))
                continue;

            var writableTarget = (AttributeReference)transaction.GetObject(target.ObjectId, OpenMode.ForWrite);
            WriteText(writableTarget, value);
            result.ChangedAttributes++;
            changed = true;
        }

        if (changed)
            result.ChangedInserts++;
    }

    private static bool IsLayerLocked(Transaction transaction, Entity entity) =>
        ((LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked;

    private static string ReadText(AttributeReference attribute)
    {
        if (!attribute.IsMTextAttribute)
            return attribute.TextString;

        using var text = attribute.MTextAttribute;
        return text.Text.ReplaceLineEndings("\n");
    }

    private static bool HasText(AttributeReference attribute, string value)
    {
        if (!attribute.IsMTextAttribute)
            return string.Equals(attribute.TextString, value, StringComparison.Ordinal);

        using var text = attribute.MTextAttribute;
        return string.Equals(text.Contents, ToMTextContents(value), StringComparison.Ordinal) ||
            string.Equals(text.Text.ReplaceLineEndings("\n"), value.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static void WriteText(AttributeReference attribute, string value)
    {
        if (!attribute.IsMTextAttribute)
        {
            attribute.TextString = value;
            return;
        }

        using var text = attribute.MTextAttribute;
        text.Contents = ToMTextContents(value);
        attribute.MTextAttribute = text;
    }

    private static string ToMTextContents(string value)
    {
        var escaped = value.Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("%", "\\U+0025");
        return escaped.ReplaceLineEndings("\\P");
    }
}
