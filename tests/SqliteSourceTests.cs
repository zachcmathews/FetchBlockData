// SPDX-License-Identifier: Apache-2.0
// See LICENSE in the project root.

using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YamlDotNet.Core;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace FetchBlockData.Tests;

[TestClass]
public class SqliteSourceTests
{
    private string directory = null!;
    private string configPath = null!;

    [TestInitialize]
    public void CreateFixture()
    {
        directory = Path.Combine(Path.GetTempPath(), "FetchBlockData-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        configPath = Path.Combine(directory, "config.yaml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "example-rules.yaml"), configPath);
    }

    [TestCleanup]
    public void RemoveFixture() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public void LoadResolvesDatabaseRelativeToTheYaml()
    {
        var configuration = Configuration.Load(configPath);

        Assert.AreEqual(Path.Combine(directory, "test-items.gpkg"), configuration.Database);
        Assert.HasCount(2, configuration.Rules);
    }

    [TestMethod]
    public void LoadRejectsTheOldEntriesField()
    {
        File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("rules:", "entries:"));

        Assert.ThrowsExactly<YamlException>(() => Configuration.Load(configPath));
    }

    [TestMethod]
    public void ReadsLabelsAndFormatsOnlyBooleanColumns()
    {
        var configuration = Configuration.Load(configPath);
        Execute(configuration, """
            CREATE TABLE items (
                uuid TEXT, id TEXT, quantity INTEGER, measurement REAL,
                active BOOLEAN, verified BOOL
            );
            INSERT INTO items VALUES
                (' X ', 'I-1', 1, 2.5, TRUE, FALSE),
                ('x', '', 0, NULL, ' false ', 'true'),
                (' ', 'ignored', 0, 0, 0, 0);
            CREATE TABLE item_groups (
                uuid INTEGER, id TEXT, quantity INTEGER, description TEXT, status TEXT
            );
            INSERT INTO item_groups VALUES (42, 'G-1', 200, NULL, 'Open');
            """);
        var originalDatabase = File.ReadAllBytes(configuration.Database);

        var rows = SqliteSource.ReadRows(configuration);

        var items = rows["item_label"];
        Assert.HasCount(2, items);
        CollectionAssert.AreEqual(new string?[] { "I-1", "1", "2.5", "YES", "NO" }, items["X"]);
        CollectionAssert.AreEqual(new string?[] { "", "0", null, "NO", "YES" }, items["x"]);
        CollectionAssert.AreEqual(new string?[] { "G-1", "200", null, "Open" }, rows["GROUP_LABEL"]["42"]);
        CollectionAssert.AreEqual(originalDatabase, File.ReadAllBytes(configuration.Database));
    }

    [TestMethod]
    public void ReadRowsDoesNotCreateAMissingDatabase()
    {
        var configuration = Configuration.Load(configPath);

        Assert.ThrowsExactly<InvalidDataException>(() => SqliteSource.ReadRows(configuration));

        Assert.IsFalse(File.Exists(configuration.Database));
    }

    [TestMethod]
    public void ReadRowsRejectsDuplicateKeysAfterTrimming()
    {
        var configuration = Configuration.Load(configPath);
        Execute(configuration, """
            CREATE TABLE items (uuid, id, quantity, measurement, active, verified);
            INSERT INTO items (uuid) VALUES ('X'), (' X ');
            """);

        var error = Assert.ThrowsExactly<InvalidDataException>(() => SqliteSource.ReadRows(configuration));

        StringAssert.Contains(error.Message, "duplicate key 'X'");
    }

    [TestMethod]
    public void ReadRowsRejectsAnUnknownMappedColumn()
    {
        var configuration = Configuration.Load(configPath);
        Execute(configuration, "CREATE TABLE items (uuid, id)");

        var error = Assert.ThrowsExactly<InvalidDataException>(() => SqliteSource.ReadRows(configuration));

        StringAssert.Contains(error.Message, "no such column");
    }

    private static void Execute(Configuration configuration, string sql)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = configuration.Database,
            Pooling = false
        };
        using var connection = new SqliteConnection(connectionString.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
