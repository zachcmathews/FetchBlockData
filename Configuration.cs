// SPDX-License-Identifier: Apache-2.0
// See LICENSE in the project root.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FetchBlockData;

public class Block
{
    public required string Name { get; set; }
    public required string Key { get; set; }
}

public class Table
{
    public required string Name { get; set; }
    public required string Key { get; set; }
}

public class MappingBlock
{
    public required string Attribute { get; set; }
}

public class MappingTable
{
    public required string Column { get; set; }
}

public class Mapping
{
    public required MappingBlock Block { get; set; }
    public required MappingTable Table { get; set; }
}

public class Entry
{
    public required Block Block { get; set; }
    public required Table Table { get; set; }
    public List<Mapping> Mappings { get; set; } = [];
}

public class Configuration
{
    public required int Version { get; set; }
    public required string Database { get; set; }
    public List<Entry> Rules { get; set; } = [];

    internal static readonly StringComparer CadNameComparer = StringComparer.OrdinalIgnoreCase;

    public static Configuration Load(string path)
    {
        path = Path.GetFullPath(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .Build();

        using var reader = File.OpenText(path);
        var config = deserializer.Deserialize<Configuration>(reader)
            ?? throw new InvalidDataException("The YAML configuration is empty.");

        if (config.Version != 1)
            throw new InvalidDataException("The YAML configuration must specify version: 1.");

        RequireName(config.Database, "database");
        if (config.Rules is not { Count: > 0 })
            throw new InvalidDataException("The YAML configuration must contain at least one rule.");

        var blockNames = new HashSet<string>(CadNameComparer);
        for (var index = 0; index < config.Rules.Count; index++)
        {
            var entry = config.Rules[index];
            var location = $"rules[{index}]";
            if (entry?.Block is null || entry.Table is null)
                throw new InvalidDataException($"{location} must contain block and table sections.");

            RequireName(entry.Block.Name, $"{location}.block.name");
            RequireName(entry.Block.Key, $"{location}.block.key");
            RequireName(entry.Table.Name, $"{location}.table.name");
            RequireName(entry.Table.Key, $"{location}.table.key");

            if (!blockNames.Add(entry.Block.Name))
                throw new InvalidDataException($"{location} repeats block name '{entry.Block.Name}'.");

            if (entry.Mappings is not { Count: > 0 })
                throw new InvalidDataException($"{location} must contain at least one mapping.");

            var targetTags = new HashSet<string>(CadNameComparer);
            for (var mappingIndex = 0; mappingIndex < entry.Mappings.Count; mappingIndex++)
            {
                var mapping = entry.Mappings[mappingIndex];
                var mappingLocation = $"{location}.mappings[{mappingIndex}]";
                if (mapping?.Block is null || mapping.Table is null)
                    throw new InvalidDataException($"{mappingLocation} must contain block and table sections.");

                var tag = mapping.Block.Attribute;
                RequireName(tag, $"{mappingLocation}.block.attribute");
                RequireName(mapping.Table.Column, $"{mappingLocation}.table.column");
                if (CadNameComparer.Equals(tag, entry.Block.Key))
                    throw new InvalidDataException($"{mappingLocation} targets the block key '{tag}'.");
                if (!targetTags.Add(tag))
                    throw new InvalidDataException($"{mappingLocation} repeats target attribute '{tag}'.");
            }
        }

        config.Database = Path.GetFullPath(config.Database, Path.GetDirectoryName(path)!);
        return config;
    }

    private static void RequireName(string? value, string location)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            throw new InvalidDataException($"{location} must contain a name or path.");
    }
}
