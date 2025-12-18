# Cadmus Migration V3

Tools for migrating (importing/exporting) Cadmus data. Export tools also include "preview", i.e. a human-friendly, highly customizable output for each Cadmus object, to be integrated in the editor itself.

👉 This is the third generation of Cadmus migration components. For compatibility reasons, the previous generation can be still be reached at its [original repository](https://github.com/vedph/cadmus-migration-v2). This is a complete refactoring of the export section to merge with Proteus and GVE projects. Project Proteus.Rendering is copied from GVE; when stable, it will be moved in the Proteus solution.

👀 [Cadmus Page](https://myrmex.github.io/overview/cadmus/)

Main projects:

- [Cadmus.Export](Cadmus.Export/README.md): general purpose components used to export Cadmus data.
- [Cadmus.Export.ML](Cadmus.Export.ML/README.md): markup-related components used to export Cadmus data into some markup language, typically XML.
- [Cadmus.Export.Rdf](Cadmus.Export.Rdf/README.md): components used to export Cadmus semantic graphs into a standard RDF format.
- `Cadmus.Import`: core components for thesauri import.
- `Cadmus.Import.Excel`: Excel (XLSX or XLS) thesauri importers.
- `Cadmus.Import.Proteus`: Proteus-based components for importing Cadmus items and parts from sources handled with Proteus readers.
- [Proteus.Rendering](Proteus.Rendering/README.md): Proteus rendering components, to be moved into the Proteus solution once stable.

## CLI

The CLI tool in this solution (`cadmus-mig`) provides the following commands:

- dump: dump Cadmus data into JSON files.
- dump thesauri: dump Cadmus thesauri into JSON files.
- export-rdf: export a Cadmus RDF graph into various standard formats.
- render-items: render Cadmus items via item composers.

>If you use the cadmus-rdf-test database as a source when running the CLI, please be sure to add all the required namespaces:

```sql
insert into namespace_lookup (id, uri) values('crm', 'http://www.cidoc-crm.org/cidoc-crm/');
```

## History

- 2025-12-18: updated packages.

### 9.0.2

- 2025-12-02: updated packages.

### 9.0.1

- 2025-11-25: ensure base text part is in item for item previewer.

### 9.0.0

- 2025-11-23: ⚠️ upgraded to NET 10.

### 8.0.13

- 2025-11-18: fix to `CadmusTextTreeBuilder` for EOL insertion.

### 8.0.12

- 2025-11-17:
  - added JSON to XML command to the CLI tool. You can use this to convert JSON files exported with the get-object command of the Cadmus CLI tool into XML files.
  - refactored JSON conversion to simplify array handling (now arrays are always rendered as arrays, even if they contain a single item).

### 8.0.11

- 2025-11-17:
  - added `JsonToXmlConverter`.
  - refactored `XsltJsonRenderer` to use the new `JsonToXmlConverter`, also changing the default behavior for arrays (render as a single-item array rather than unwrap the item). 
- 2025-11-16: added `Bcp47TextFilter`.

### 8.0.10

- 2025-10-31: updated packages.
- 2025-10-23: added RDF OWL XML export and updated version numbers for affected libraries and CLI tool.

### 8.0.9

- 2025-10-23: updated packages.

### 8.0.8

- 2025-09-28:
  - updated packages and their versions.
  - added `Cadmus.Export.Rdf` project (RDF export).

### 8.0.7

- 2025-07-23: fixed ID names in thesauri export.

### 8.0.6

- 2025-07-22:
  - added dump thesauri command.
  - updated packages.
  - added GitHub action.
- 2025-07-05:
  - fixes to JSON dumper.
  - updated packages.
- 2025-06-27: fixes to dump command and export dump function.
- 2025-06-22: fix to item status in data framer when item's part has changed.

### 8.0.2

- 2025-06-20: added dump logic to export.
- 2025-06-17:
  - updated packages (all the version numbers will have to be increased).
  - adding `CadmusMongoItemDumper` to dump Cadmus items into JSON, including their state (changed, added, deleted) and deleted items, and considering also the modification time of their parts. 
- 2025-05-21:
  - added `CompositeOptions`.
  - updated test packages.

### 8.0.1

- 2025-05-12:
  - updated packages.
  - fixed `BlockLinearTextTreeFilter` missing root node metadata.

### 8.0.0

- 2025-05-08:
  - ⚠️ moved `BlockLinearTextTreeFilter` from `Cadmus.Export` to `Proteus.Rendering`.
  - ⚠️ moved constant `F_EOL_TAIL` from `CadmusTextTreeBuilder` to `ExportedSegment`.
  - fixes to `BlockLinearTextTreeFilter`.
  - renamed `MergeLinearTextTreeFilter` and its tag.

### 7.0.0

- 2025-05-01: ⚠️ first commit of V3. Note: BREAKING change for Cadmus preview configurations:
  - use `:` instead of `|` for type ID + role ID separator, e.g. `it.vedph.token-text-layer:fr.it.vedph.apparatus` rather than `it.vedph.token-text-layer|fr.it.vedph.apparatus`.
  - component tags have been changed where uniformation was required.
  - section `RendererFilters` renamed into `TextFilters`.

---

### 6.0.1

- 2025-04-16: use IsAccepted in `TeiHelper` to distinguish between `lem` and `rdg` in `TeiAppLinearTextTreeRenderer` (and `TeiAppParallelTextTreeRenderer`).`
- 2025-04-11:
  - updated packages (including Proteus). Note that test `TeiAppParallelTextTreeRendererTest` is broken but this is due only to the fact that this code is a stub for future implementation.
  - added entry's `Tag` rendition (in `@type`) for `lem`/`rdg` in `TeiAppLinearTextTreeRenderer`.
- 2025-03-14: more tests.
- 2025-03-11: fixes to `AppParallelTextTreeFilter`.
- 2025-03-05: updated packages.

### 6.0.0

- 2025-02-26: ⚠️ refactored export models:
  - for text, introduced tree between source models and rendition; tree filters can be used to change the tree itself, or to add features to its metadata from specific layers.
  - the preview factory configuration has changed to include new components and exclude the obsolete blocks
  - the preview get blocks method has been replaced with a get spans method using a new model.
  - for TEI, a new set of components allow for simple TEI with linear trees with apparatus. Another set of components is planned to replace standoff-TEI with different strategies. Standoff will still be derived from trees, variously filtered, fragmented into ranges linked to zero or more layer fragments. The corresponding standoff entries, rendered by JSON renderers as before, will be referred to the text via a range of ranges: from the first range to the last one connected to each specific layer fragment. To this end we can use a `loc` child element like `<loc spanFrom="#a" spanTo="#c"/>`.
  - the CLI tool infrastructure has been modernized.
  - component tags have been uniformed to always have the prefix `it.vedph.`.

---

### 5.0.4

- 2024-02-14: updated packages.

### 5.0.3

- 2025-01-28: updated packages.

### 5.0.2

- 2024-11-30: updated packages.

### 5.0.1

- 2024-11-20: updated packages.

### 5.0.0

- 2024-11-13: ⚠️ upgraded to .NET 9.

### 4.0.17

- 2023-02-10: updated packages.

### 4.0.16

- 2023-01-31: updated packages.

### 4.0.14

- 2024-01-26:
  - added `CadmusEntrySetContextPatcher`.
  - added `ThesaurusEntryMap`.

### 4.0.10

- 2024-01-26: added more options to `MdDumpEntrySetExporter`.

### 4.0.9

- 2024-01-26:
  - more functions for `CadmusEntrySetContext`.
  - updated packages.

### 4.0.7

- 2024-01-19: refactored exporter to use true items.

### 4.0.6

- 2024-01-19: updated packages.

### 4.0.4

- 2024-01-18: updated packages.
- 2024-01-05: added `MdDumpEntrySetExporter`.

### 4.0.3

- 2024-01-03:
  - updated packages.
  - added `Cadmus.Import.Proteus` project for Proteus-based import. This provides a generic Cadmus entry set context to collect items with their parts, without requiring any part-specific dependencies as parts are represented as JSON objects. Also, a `MongoEntrySetExporter` is provided to export the context to a MongoDB Cadmus database.

### 4.0.2

- 2023-11-21: updated packages.

### 4.0.1

- 2023-11-18: ⚠️ Upgraded to .NET 8.

### 3.0.8

- 2023-09-04: updated packages.

### 3.0.7

- 2023-06-27: added logger to `IItemComposer`.

### 3.0.6

- 2023-06-24:
  - updated packages.
  - fixed tests.

### 3.0.5

- 2023-06-21: fixed missing options in JSON thesaurus reader.

### 3.0.3

- 2023-06-21: completed Excel thesaurus reader.
- 2023-06-20:
  - added CSV thesaurus reader.
  - adding XLS(X) thesaurus reader.
- 2023-06-19: more tests.
- 2023-06-18: adding import.

### 3.0.2

- 2023-06-16: updated packages.

### 3.0.1

- 2023-06-02: updated packages.

### 3.0.0

- 2023-05-23: updated packages (breaking change in general parts introducing [AssertedCompositeId](https://github.com/vedph/cadmus-bricks-shell/blob/master/projects/myrmidon/cadmus-refs-asserted-ids/README.md#asserted-composite-id)).

### 2.0.4

- 2023-05-12: updated packages.

### 2.0.3

- 2023-02-05: updated packages.

### 2.0.2

- 2023-02-01: migrated to new components factory. This is a breaking change for backend components, please see [this page](https://myrmex.github.io/overview/cadmus/dev/history/#2023-02-01---backend-infrastructure-upgrade). Anyway, in the end you just have to update your libraries and a single namespace reference. Benefits include:
  - more streamlined component instantiation.
  - more functionality in components factory, including DI.
  - dropped third party dependencies.
  - adopted standard MS technologies for DI.

### 1.0.0

- 2022-11-10: upgraded to NET 7.

### 0.1.1

- 2022-11-04: updated packages.

### 0.1.0

- 2022-11-04: updated packages (nullability enabled in Cadmus core).

### 0.0.21

- 2022-11-03: updated packages.

### 0.0.20

- 2022-10-24: refactored block ID for TEI standoff export (`Cadmus.Export.ML`).
- 2022-10-14: fixes to render items command.

### 0.0.19

- 2022-10-11: updated Cadmus packages.

### 0.0.18

- 2022-10-10:
  - use XSLT script settings in XSLT JSON renderer.
  - updated packages.

### 0.0.17

- 2022-10-08: fix to array wrapper in XSLT JSON renderer.

### 0.0.16

- 2022-10-08: added ISO639 filter.
- 2022-10-07: added array wrapping to XSLT JSON renderer.
- 2022-10-06:
  - fixes to TEI standoff item composer.
  - minor refactorings to renderer context.

### 0.0.15

- 2022-10-04: moved repository dependency out of database-dependent filters. The repository is now a property of the renderer context.

### 0.0.14

- 2022-10-04: added item ID to previewer and context to its filters.

### 0.0.13

- 2022-10-02: added `TextTemplate` to `MongoTokenExtractorRendererFilter`.

### 0.0.12

- 2022-10-01:
  - added `MongoTokenExtractorRendererFilter`.
  - more methods in `CadmusPreviewer`.
- 2022-08-26: completing `TeiStandoffApparatusJsonRenderer`.

### 0.0.11

- 2022-08-22: refactored sentence splitter.

### 0.0.10

- 2022-08-22: added flags matching.

### 0.0.9

- 2022-08-21: added sentence splitter filter (used to extract Sidonius Apollinaris from its [Cadmus project](https://github.com/vedph/cadmus-sidon-app)).
- 2022-08-19: adding CLI infrastructure and refactored item composer API.
- 2022-08-19: item ID collectors.
- 2022-08-17: more conceptual documentation.

### 0.0.8

- 2022-08-15: essential TEI renderer components.
- 2022-08-14:
  - adding `IItemComposer`.
  - `BuildTextBlocks` added assigned layer IDs.

### 0.0.6

- 2022-08-11:
  - refactoring JSON renderers for configurable filters.
  - added thesaurus renderer filters.

### 0.0.5

- 2022-08-08: added Markdown support.
