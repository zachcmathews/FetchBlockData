# FetchBlockData

FetchBlockData is an AutoCAD add-in that updates block attributes from a SQLite database or GeoPackage. A YAML configuration defines which blocks to update, how to match them to database rows, and which values to copy.

Use the database as the single source of truth (SSOT) for mapped values. Update a record once, then run `FETCHBLOCKDATA` in each drawing that uses it. This keeps repeated information consistent across drawings and cuts down on manual re-entry.

## Use the add-in

1. In GitHub Actions, download the Release artifact that covers the version shown in AutoCAD’s About dialog. Extract it and keep the DLLs together.
2. In AutoCAD, use `NETLOAD` to load `FetchBlockData.dll`.
3. Save the example below as a local `config.yaml` and adapt it to your drawing and database.
4. Run `FETCHBLOCKDATA` and select the YAML file. The command reads the database in read-only mode and updates matching block inserts in model space and every paper-space layout of the active drawing.

## Build targets

| AutoCAD versions | Runtime | Release artifact |
| --- | --- | --- |
| 2026 through 2026.1.1 | .NET 8 | `FetchBlockData-AutoCAD-2026-Release-win-x64` |
| 2026.1.2 and later 2026 updates | .NET 10 | `FetchBlockData-AutoCAD-2026.1.2-Release-win-x64` |
| 2027 and its updates | .NET 10 | `FetchBlockData-AutoCAD-2027-Release-win-x64` |

Set `AutoCADVersion` to `2026`, `2026.1.2`, or `2027` for a local build.

```sh
dotnet build FetchBlockData.csproj -c Release -p:AutoCADVersion=2026.1.2
```

Local builds default to the AutoCAD 2027 target.

## Example config.yaml

```yaml
version: 1
database: ./items.gpkg
rules:
  - block:
      name: ITEM_LABEL
      key: UUID
    table:
      name: items
      key: uuid
    mappings:
      - block:
          attribute: ID
        table:
          column: id
      - block:
          attribute: NAME
        table:
          column: name
```

FetchBlockData uses the block insert’s `UUID` attribute to find the row with the same `uuid` in the `items` table. It copies that row’s `id` and `name` into the corresponding attributes. The table key must be unique, and relative database paths resolve from the folder containing the YAML file.

## License

FetchBlockData is licensed under the [Apache License, Version 2.0](LICENSE).
