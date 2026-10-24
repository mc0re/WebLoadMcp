# WebLoadMcp

An [MCP](https://modelcontextprotocol.io) server in C# (.NET 10) that loads pages from one predefined web server, parses the HTML, and returns either a list of items or a single item with details. It also has a `ready` check that reports whether the machine's public IP is outside a given country.

## Tools

### `load_page(path)`

Loads `BaseUrl + path`. `path` must be relative. The base server's address is never shown to the MCP client: results and error messages contain only paths relative to it. Absolute URLs, `//host` URLs and paths that use `..` to climb above the base URL are rejected before any request is sent.

If `WebLoad:List:ItemSelector` matches one or more elements, the page is a **list**:

```json
{
  "kind": "list",
  "path": "archive/",
  "items": [
    { "year": "2023", "title": "First", "link": "archive/items/1" },
    { "year": "2024", "title": "Second", "link": "archive/items/2" }
  ]
}
```

`link` is a path relative to the base URL. Pass it back to `load_page` to get that item's details. Links that point to another server stay absolute.

If the selector matches nothing, the page is a single **item**, and the fields come from `WebLoad:Detail:Fields`:

```json
{
  "kind": "item",
  "path": "archive/items/1",
  "item": {
    "title": "First",
    "year": "2023",
    "country": "Denmark",
    "description": "Details here.",
    "genres": ["Drama", "Comedy"],
    "audio": ["Русский", "Английский"],
    "subtitles": ["Русский"],
    "size": "1.4 GB",
    "image": "https://images.example.org/poster.jpg",
    "reference": "magnet:?xt=urn:btih:73e4ebea578584f332dbf64fb1c302b57210b1c4&dn=First%20%282023%29"
  }
}
```

A `warnings` array is added when something looks off, e.g. a magnet link whose trackers differ from the configured ones. The warnings are also written to the server log.

#### Magnet links

When the `reference` field holds a magnet link, it is rewritten:

- `dn` (display name) is replaced with `title (year)`, built from the item's own fields, or just `title` when there is no year.
- `tr` (trackers) are removed. The site's trackers are listed in `WebLoad:Magnet:Trackers` instead, so they can be added back when the link is used. If a page's trackers differ from that list, a warning names the missing and extra ones.
- Other parameters, such as `xt` (the info-hash), are kept as they are.

### `ready()`

Asks a geo-IP service which country the public IP is in. Returns `true` if that country is **not** `Ready:Country`, and `false` if it is. If `Ready:Country` is not set, or the country cannot be found out (network error, unexpected response), the tool returns an MCP error rather than a guess.

## Configuration

Settings are in `src/WebLoadMcp/appsettings.json`. You can override any of them with environment variables, using `__` as the separator (for example `WebLoad__BaseUrl`, `Ready__Country`), or with command-line arguments (`--WebLoad:BaseUrl=...`).

| Setting | Meaning |
|---|---|
| `WebLoad:BaseUrl` | **Required.** Absolute http(s) URL of the server. |
| `WebLoad:TimeoutSeconds`, `WebLoad:UserAgent` | HTTP client settings. |
| `WebLoad:List:ItemSelector` | CSS selector for each item on a list page. |
| `WebLoad:List:Fields` | Fields read from each list item (default: `year`, `title`, `link`). |
| `WebLoad:Detail:Fields` | Fields read from a single-item page (default: `title`, `year`, `country`, `description`, `genres[]`, `audio[]`, `subtitles[]`, `size`, `image`, `reference`). |
| `WebLoad:Languages` | Language names by word stem, used by `Language` fields (`audio`, `subtitles`). |
| `WebLoad:Magnet:Field` | Item field holding the magnet link to rewrite (default `reference`; empty turns the rewrite off). |
| `WebLoad:Magnet:Trackers` | Trackers expected in the site's magnet links. |
| `Ready:Country` | ISO 3166-1 alpha-2 code, e.g. `DK`. |
| `Ready:GeoIpUrl` | JSON geo-IP endpoint (default `https://ipinfo.io/json`). |
| `Ready:CountryProperty` | JSON property that holds the country code (default `country`). |

Each field has these settings:

```json
"link": { "Selector": "a[href]", "Attribute": "href", "Multiple": false, "Separator": null }
```

- `Selector`: CSS selector, evaluated inside the list item (for list pages) or the whole document (for item pages).
- `Attribute`: read this attribute instead of the element's text. `href` and `src` values that point to the base server become relative paths; other links (e.g. `magnet:`) are returned exactly as written.
- `Label`: keep only elements whose text starts with this label, and return the text that *follows* the element up to the next line break, e.g. `{ "Selector": "#details b", "Label": "Страна" }` reads `США, Япония` from `<b>Страна: </b>США, Япония<br>` or `<b>Страна</b>: США, Япония<br>`. Several labels can be given as alternatives, separated by `|` (`"Страна|Выпущено"`). If the label is directly followed by a line break, the next line is used. It also works for table rows like `<td class="header">Размер</td><td>28.35 GB</td>`.
- `Pattern`: regular expression applied to the value; the first capture group is returned (or the whole match), e.g. `\((\d{4})` (written `"\\((\\d{4})"` in JSON) takes `2018` from `Wildling (2018) BDRemux`.
- `Multiple`: return every match as an array instead of only the first match.
- `Separator`: characters that split each value into list entries, e.g. `",/"` turns `"Drama, Comedy / Crime"` into `["Drama", "Comedy", "Crime"]`. Setting it makes the field a list.

- `Language`: replace each value with the language it names, looked up in `WebLoad:Languages` (word stem → name, e.g. `"рус": "Русский"`, `"eng": "Английский"`). A value that names no language becomes `Default`. A stem mapped to `""` (e.g. `"нет"`) means "none", and the value is dropped.
- `Default`: the value used when `Language` finds no language name. It's `Русский` for audio and subtitles.
- `Distinct`: remove duplicates from a list field.

A list field with no matches is `[]`. A single-value field with no match is `null`.

The shipped `appsettings.json` matches the sample pages in `data/` (`list.html`, `item.html`, `item2.html`), and `SampleDataTests` checks this. If the site's markup changes, update the selectors and the samples together.

## Build and test

```sh
dotnet build
dotnet test
```

## Using it from an MCP client

The server uses the stdio transport. Example for Claude Code / Claude Desktop:

```json
{
  "mcpServers": {
    "webload": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/WebLoadMcp/src/WebLoadMcp", "--no-build"],
      "env": {
        "WebLoad__BaseUrl": "https://example.com/",
        "Ready__Country": "DK"
      }
    }
  }
}
```

Or publish it once with `dotnet publish src/WebLoadMcp -c Release -o out`, and set `"command": "/path/to/out/WebLoadMcp"`.
