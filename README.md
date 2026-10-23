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
  "title": "Archive",
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
  "title": "First",
  "item": {
    "title": "First",
    "country": "Denmark",
    "description": "Details here.",
    "genres": ["Drama", "Comedy"],
    "audio": ["English", "Danish"],
    "subtitles": ["English"],
    "size": "1.4 GB",
    "reference": "files/1.bin"
  }
}
```

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
| `WebLoad:Detail:Fields` | Fields read from a single-item page (default: `title`, `country`, `description`, `genres[]`, `audio[]`, `subtitles[]`, `size`, `reference`). |
| `Ready:Country` | ISO 3166-1 alpha-2 code, e.g. `DK`. |
| `Ready:GeoIpUrl` | JSON geo-IP endpoint (default `https://ipinfo.io/json`). |
| `Ready:CountryProperty` | JSON property that holds the country code (default `country`). |

Each field has these settings:

```json
"link": { "Selector": "a[href]", "Attribute": "href", "Multiple": false, "Separator": null }
```

- `Selector`: CSS selector, evaluated inside the list item (for list pages) or the whole document (for item pages).
- `Attribute`: read this attribute instead of the element's text. `href` and `src` values are resolved to paths relative to the base URL.
- `Multiple`: return every match as an array instead of only the first match.
- `Separator`: characters that split each value into list entries, e.g. `",/"` turns `"Drama, Comedy / Crime"` into `["Drama", "Comedy", "Crime"]`. Setting it makes the field a list.

A list field with no matches is `[]`. A single-value field with no match is `null`.

> The default CSS selectors in `appsettings.json` are placeholders until real page examples are available. Adjusting them should only need configuration changes, not code changes.

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
