# WebLoadMcp

An [MCP](https://modelcontextprotocol.io) server in C# (.NET 10) that loads pages from one predefined web server, parses the HTML, and returns either a list of items (`get_list`) or a single item with details (`get_item`). It also has a `ready` check that reports whether the machine's public IP is outside a given country.

## Tools

Both page tools take a path relative to a prefix from the settings. The base server's address is never shown to the MCP client: results and error messages contain only these relative paths. Absolute URLs, `//host` URLs and paths that use `..` to leave the prefix are rejected before any request is sent.

### `get_list(path)`

Loads `BaseUrl + WebLoad:List:PathPrefix + path`. For example, `1/0/0/0` loads `<site>/browse/1/0/0/0`. Each row matching `WebLoad:List:ItemSelector` becomes an item:

```json
{
  "kind": "list",
  "path": "1/0/0/0",
  "items": [
    { "year": "2018", "title": "Wildling", "link": "1045132/wildling-2018-bdremux-1080p" },
    { "year": "1989", "title": "Mystery Train", "link": "1045135/mystery-train-1989-bdremux-1080p" }
  ]
}
```

`link` is relative to the item prefix (`torrent/` is removed), so you can pass it straight to `get_item`. Links that point to another server stay absolute. A page with no matching rows gives an empty `items` list.

### `get_item(path)`

Loads `BaseUrl + WebLoad:Detail:PathPrefix + path`. For example, `1045135/mystery-train-1989-bdremux-1080p` loads `<site>/torrent/1045135/…`. The fields come from `WebLoad:Detail:Fields`:

```json
{
  "kind": "item",
  "path": "1045135/mystery-train-1989-bdremux-1080p",
  "item": {
    "title": "First",
    "year": "2023",
    "country": ["United States", "Japan"],
    "description": "Details here.",
    "genres": ["Drama", "Comedy"],
    "audio": ["Russian", "English"],
    "subtitles": ["Russian"],
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
| `WebLoad:List:PathPrefix` | Path of list pages under `BaseUrl` (`browse/`). `get_list` paths are relative to it. |
| `WebLoad:Detail:PathPrefix` | Path of item pages under `BaseUrl` (`torrent/`). `get_item` paths, and list items' `link`, are relative to it. |
| `WebLoad:List:ItemSelector` | CSS selector for each item on a list page. |
| `WebLoad:List:Fields` | Fields read from each list item (default: `year`, `title`, `link`). |
| `WebLoad:Detail:Fields` | Fields read from a single-item page (default: `title`, `year`, `country`, `description`, `genres[]`, `audio[]`, `subtitles[]`, `size`, `image`, `reference`). |
| `WebLoad:Languages` | English language names by word stem, used by `Language` fields (`audio`, `subtitles`). |
| `WebLoad:Countries` | Country names as written on the site, each with `Name` (English) and `Language` (the country's main film language). Used by the `country` field and to resolve original-language tracks. |
| `Logging:File:Path` | Log file to write in addition to stderr. A relative path is relative to the application folder. Empty (the default) turns it off. Levels are set under `Logging:File:LogLevel`. |
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
- `Label`: keep only elements whose text starts with this label, and return the text that *follows* the element up to the next line break, e.g. `{ "Selector": "#details b", "Label": "Страна" }` reads `США, Япония` from `<b>Страна: </b>США, Япония<br>` or `<b>Страна</b>: США, Япония<br>`. Several labels can be given as alternatives, separated by `|` (`"Страна|Выпущено"`). Latin letters that look like Cyrillic ones are treated as Cyrillic, so `Cубтитры` typed with a Latin `C` still matches `Субтитры`. If the label is directly followed by a line break, the next line is used. It also works for table rows like `<td class="header">Размер</td><td>28.35 GB</td>`.
- `Pattern`: regular expression applied to the value; the first capture group is returned (or the whole match), e.g. `\((\d{4})` (written `"\\((\\d{4})"` in JSON) takes `2018` from `Wildling (2018) BDRemux`.
- `ItemLink`: the value is a link to an item page. It's returned relative to `WebLoad:Detail:PathPrefix`, ready for `get_item`.
- `Multiple`: return every match as an array instead of only the first match.
- `Separator`: characters that split each value into list entries, e.g. `",/"` turns `"Drama, Comedy / Crime"` into `["Drama", "Comedy", "Crime"]`. Setting it makes the field a list.

- `Country`: keep only values that are known countries in `WebLoad:Countries`, replaced by their English names. Anything else, such as a studio in `Выпущено: США, Columbia Pictures`, is dropped.
- `Language`: replace each value with the English name of the language it mentions, found through `WebLoad:Languages` (word stem → name, e.g. `"русск": "Russian"`, `"eng": "English"`). Stems longer than three letters match the start of a word (`англ` matches `Английский`). Stems of three letters or fewer must match a whole word (`rus`, `рус.`), so `per` doesn't match `performance`. Two special values:
  - A stem mapped to `""` (e.g. `"нет"`) means "none", and the value is dropped.
  - A stem mapped to `"@original"` (e.g. `"оригинал"`) means the original-language track. It becomes the language of the first production country, from `WebLoad:Countries`, or `Original` if that's unknown.
- `Default`: the value used when `Language` finds no language name. It's `Russian` for audio and subtitles.
- `Distinct`: remove duplicates from a list field.

A list field with no matches is `[]`. A single-value field with no match is `null`.

Values that don't match are written to the server log (stderr) at Information level, so the lists can be extended:

- unknown countries;
- values that name no known language, which fall back to `Default`;
- original-language tracks whose country, or its language, is unknown.

The shipped `appsettings.json` matches the sample pages in `data/` (`list.html`, `item.html` to `item4.html`), and `SampleDataTests` checks this. If the site's markup changes, update the selectors and the samples together.

## Viewing the server log

The simplest way is to set `Logging:File:Path`, e.g. `"webload.log"` in `appsettings.json` or `Logging__File__Path=/tmp/webload.log` as an environment variable. The log is then written to that file, wherever the server runs.

The server also writes its log to stderr, because stdout carries the MCP protocol. Where that output ends up depends on the client:

- **MCP Inspector:** `npx @modelcontextprotocol/inspector dotnet run --project src/WebLoadMcp` opens a web UI where you can call the tools and see the stderr output.
- **Claude Desktop:** each server gets its own log file, `mcp-server-<name>.log`. It's in `~/Library/Logs/Claude/` on macOS and `%APPDATA%\Claude\logs\` on Windows.
- **Claude Code:** start it with `claude --debug` to see MCP server output.
- **Directly:** run `dotnet run --project src/WebLoadMcp 2> webload.log` and paste JSON-RPC messages into it.

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
