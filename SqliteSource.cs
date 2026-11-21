// SPDX-License-Identifier: Apache-2.0
// See LICENSE in the project root.

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FetchBlockData;

internal static class SqliteSource
{
    public static Dictionary<string, Dictionary<string, string?[]>> ReadRows(Configuration config)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config.Database,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        };

        using var connection = new SqliteConnection(connectionString.ToString());
        try
        {
            connection.Open();
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException($"Cannot open SQLite database '{config.Database}'. {exception.Message}", exception);
        }

        using var transaction = connection.BeginTransaction(deferred: true);
        var lookup = new Dictionary<string, Dictionary<string, string?[]>>(Configuration.CadNameComparer);
        foreach (var entry in config.Rules)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var columns = new[] { entry.Table.Key }.Concat(entry.Mappings.Select(mapping => mapping.Table.Column));
            var projection = string.Join(", ", columns.Select(column => $"src.{QuoteIdentifier(column)}"));
            command.CommandText = $"SELECT {projection} FROM main.{QuoteIdentifier(entry.Table.Name)} AS src";

            var rows = new Dictionary<string, string?[]>(StringComparer.Ordinal);
            try
            {
                using var reader = command.ExecuteReader();
                var booleanColumns = new bool[entry.Mappings.Count];
                for (var index = 0; index < booleanColumns.Length; index++)
                {
                    var typeName = reader.GetDataTypeName(index + 1).Trim();
                    booleanColumns[index] = typeName.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase)
                        || typeName.Equals("BOOL", StringComparison.OrdinalIgnoreCase);
                }

                while (reader.Read())
                {
                    var key = ToLabelText(reader.GetValue(0))?.Trim();
                    if (key is null || key.Length == 0)
                        continue;

                    var values = new string?[entry.Mappings.Count];
                    for (var index = 0; index < values.Length; index++)
                        values[index] = ToLabelText(reader.GetValue(index + 1), booleanColumns[index]);

                    if (rows.ContainsKey(key))
                    {
                        throw new InvalidDataException(
                            $"Table '{entry.Table.Name}' has duplicate key '{key}' for block '{entry.Block.Name}'.");
                    }
                    rows.Add(key, values);
                }
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException(
                    $"Cannot read table '{entry.Table.Name}' for block '{entry.Block.Name}'. {exception.Message}", exception);
            }

            lookup.Add(entry.Block.Name, rows);
        }

        transaction.Commit();
        return lookup;
    }

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private static string? ToLabelText(object value, bool isBooleanColumn = false) => value switch
    {
        0L or 0d when isBooleanColumn => "NO",
        1L or 1d when isBooleanColumn => "YES",
        string text when isBooleanColumn && bool.TryParse(text, out var boolean) => boolean ? "YES" : "NO",
        string text => text,
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        double real => real.ToString(CultureInfo.InvariantCulture),
        _ => null
    };
}
