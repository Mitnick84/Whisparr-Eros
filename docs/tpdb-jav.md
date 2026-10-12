# ThePornDB JAV metadata

This optional movie metadata source uses ThePornDB's authenticated `/jav` API.
It does not require a corresponding StashDB scene. Existing TMDB/TPDB movie
lookups and StashDB scene lookups retain their own catalogs.

## Setup and lookup

1. Obtain a ThePornDB API token with access to the JAV catalog.
2. In Settings > General, enter it in **ThePornDB API key** and save.
3. Select **ThePornDB JAV** as the movie metadata source for product-code or
   title searches in Movies > Add New. This chooses the catalog for ordinary
   movie searches; switch back to TMDB or TPDB to discover western movies.
4. Alternatively, use an explicit `https://theporndb.net/jav/<slug>` URL or
   `tpdbjav:<UUID or numeric ID>` in Movies > Add New. These lookups identify
   the JAV catalog even when a different default movie source is selected.

Generic `tpdb:` and `tpdbid:` identifiers still refer to the TPDB movie catalog.
They are not interchangeable with JAV identifiers. Searching a JAV URL in
Scenes > Add New explains that it belongs in Movies > Add New.

To discover western movies and JAV in the same search, select **TPDB** and enable
**Include JAV in TPDB movie searches**. This option is disabled by default and
requires the same ThePornDB API key. Ordinary title/code queries return TPDB movie
results followed by JAV results, retaining their separate source IDs. Explicit
URLs/IDs target only their own catalog. If the additional JAV request fails,
TPDB movie results remain available and a warning is logged; explicit JAV lookups
still report errors. This combined mode is an optional extension to the proposed
mutually exclusive source design and requires maintainer agreement.

The adapter preserves each record's canonical UUID as `tpdbjav:<UUID>` and its
TPDB UUID separately. Refresh and library import use the JAV endpoint regardless
of the current default source. TPDB studio/performer IDs are never used as
StashDB IDs. Available title, description, studio/network, performer names,
release date, tags, runtime and artwork are mapped from TPDB. Runtime is converted
from seconds to minutes.

## Release search and naming

For a JAV movie with a product code, indexer searches use that code without a
year or studio prefix. Common leading codes such as `DBIF-001`, `H113SL-013AI`
and `FC2-PPV-1234567` are recognized even when a title/year follows them. A unique
case-insensitive library code can identify the release. Multiple library entries
with the same code are refused as ambiguous.

The new **`{Movie Code}`** naming token is available for movie filenames and
folders. For example:

```text
{Movie Code} - {Movie Title} {(Release Year)} {[Quality Title]}
```

Keep the code at the beginning when retaining a descriptive title. It also
provides an identifier that external JAV scrapers can search; automatic matching
in Stash still depends on the scraper and its configuration. A TPDB JAV UUID is
not a StashDB UUID. No automatic cross-provider ID conversion is performed.

## Limits

- Title/code discovery displays the first 50 API results; use an explicit URL/ID
  or a more specific query when needed.
- Indexer availability, release quality and import restrictions still determine
  whether a discovered movie can be downloaded.
- JAVLibrary, JAVDB, JavBus, R18.dev and cross-provider enrichment are separate
  integrations, outside this adapter.
- The API key is masked in settings responses, preserved when saving the masked
  value, sent only as a Bearer header to `https://api.theporndb.net`, and omitted
  from configuration trace output. Removing the value clears the stored key.
- The adapter does not follow API redirects while sending credentials.

API contract: [ThePornDB documentation](https://api.theporndb.net/docs).
