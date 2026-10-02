# RCynQ architecture

**English** · [Deutsch](../De/Architecture.md) · [Español](../Es/Architecture.md) · [Français](../Fr/Architecture.md) · [Português](../Pt/Architecture.md) · [Русский](../Ru/Architecture.md)

## Nodes

One program serves three roles; the role is chosen in the configuration.

| Role | What it is | What it does |
|---|---|---|
| **Workstation** | The computer you work on | Keeps working copies, pulls branches, sends and receives patches |
| **Origin** | The computer with the private repositories | Offers its repositories, receives and sends patches |
| **Relay** | A computer both ends can reach | Forwards SSH connections. Has no repository, sees no content |

A chain is `Workstation → Relay(s) → Origin`. With no Relay the two ends connect directly.

## Rule: the repository on the Origin is only read

- RCynQ writes nothing into a Git repository on an Origin: no remote, no setting, no ref, no commit, no index refresh (Git runs with `GIT_OPTIONAL_LOCKS=0`).
- Working files on the Origin change only when a patch the user sent is applied.
- Everything the Origin stores (the list of repositories, the sync base, history, backups) lives in `syncbridge.json` and in the data folder of the node.
- Git housekeeping (creating the repository in an empty folder, the `rcynq` remote, `refs/remotes/rcynq/*`, fast-forward on Pull) happens only in the working copy on the Workstation.

An end-to-end test compares every file under `.git` before and after registering, switching, serving and inspecting a repository.

## Channels

Both channels use the same connection and the same authorization.

**Git channel (Origin → Workstation).** The Workstation names a repository and the commits it already has. The Origin answers with an incremental Git bundle. The Workstation fetches it into `refs/remotes/rcynq/*` and fast-forwards. Nothing is ever pushed.

**Patch channel (both directions).** A patch is the difference between the working tree and the *sync base* — what the other node is known to have. The sync base is built automatically from the Git commit both nodes share, plus the files already exchanged. Delivery: a signed manifest, then each file by its SHA-256, then a commit that applies everything or nothing. A conflict is resolved by keeping the local file, taking the incoming one (with a backup) or a three-way Git merge.

## How a node accepts connections

| Mode | Path | Peer is verified by |
|---|---|---|
| SSH server | `ssh -L` to the loopback port of the node, through the Relays as jump hosts | Pinned SSH host key |
| Own TLS port (default `18444`) | Straight to the port, or through an SSH forward that ends on the last Relay | Pinned certificate fingerprint |

A Relay always uses its SSH server. Keys in `authorized_keys` are restricted to one port forward and cannot run commands.

The local API of a node listens on loopback only (default `18443`) and requires a token stored in the data folder.

## Routes to a node

A node may be reachable at several addresses: for example through a tunnel over UDP and through a second tunnel over TCP. Each such address is a *route* of the link. The keys and fingerprints of the node are the same for all routes; only the address differs.

- **Storage.** Routes are kept in the link. The address of the node in the link is always the address of the route in use, so patches, the Git channel, the ping and the diagnostics take the chosen route without knowing about routes.
- **Check.** Every 15 seconds, and at once after a failed ping, the node checks every route. A neighbour is checked with a TCP connection. A node behind Relays is checked through an SSH forward that ends at the address of the route: the banner of an SSH server or a TLS handshake counts as an answer.
- **Choice.** The answering route with the lowest priority wins; among equals the fastest one, when it is clearly faster than the route in use. When a better route answers again the node returns to it. While none answers the node stays where it is.
- **Choice by hand.** A pinned route is used whether it answers or not, until the choice is handed back to the node.

A link with one address has no routes: it is neither checked nor changed, and the node works as before.

The way through several Relays is set by the endpoint: it lists them in order and passes them over SSH itself. A Relay stays passive; it only lets the keys of the endpoints forward to the nodes of the opposite role and to the SSH servers of the neighbouring Relays, at every address it knows them by.

## Security of the exchange

| Protection | Mechanism |
|---|---|
| Who may send | Shared link token and a known peer identifier in every request |
| Integrity and origin | ECDSA P-256 signature over the manifest, including the repository name |
| Replay | Patch identifier and age are checked; a patch is accepted once |
| Path traversal | Paths in a patch are canonical and must stay inside the working tree |
| Wrong repository | A patch for a repository the receiver does not work with is refused |
| Exposure of the Origin | A neighbour learns repository names only, never paths; a bundle is served only for a registered name |

## Repositories

- The Origin keeps a catalog: name → folder. A name is 1–64 characters of `A–Z a–z 0–9 . _ -`.
- The Workstation keeps name → local folder, and may add only names the Origin offers.
- Each node works with one repository at a time. Switching needs no restart: it waits for the running send, receive or Pull and then changes the working tree for everything at once.
- Every ping carries the name of the repository the node works with, in both directions. A node that learns the other one switched tells the user and does not follow by itself.
- The node keeps a sync base per repository. Patch history is shown for the current repository.

## Storage

```
<program folder>/
  config/syncbridge.json     node settings, links, repositories
  data/
    rcynq.db                 SQLite: peers, links, patches, history, encrypted secrets (signing key)
    keys/                    SSH key, TLS certificate, API token, known_hosts
    patches/  backups/       patch content and files replaced by a patch
    snapshots/               sync base (one folder per additional repository)
    logs/
```

A configuration file written by an earlier version is converted at start; the original is kept as `syncbridge.json.bak`.

## Projects

| Project | Purpose |
|---|---|
| `RCynQ.Core` | Contracts, configuration, API records |
| `RCynQ.Localization` | Texts, one JSON file per language |
| `RCynQ.Core.Data`, `…Migrations`, `…Migrations.SQLite` | SQLite storage and schema migrations |
| `RCynQ.Git` | Git commands, manifests, patch building |
| `RCynQ.Ssh` | SSH commands, port forwards, key and host key handling |
| `RCynQ.Transports.*` | Links to neighbours and their state |
| `RCynQ.Sync` | Patches, sync base, Git channel, repositories, diagnostics |
| `RCynQ.Service` | Local HTTP API and SignalR events |
| `RCynQ.UI` | Desktop application (Avalonia); starts the service |
| `RCynQ.Cli` | `rcynq` command line |
| `RCynQ.Dbs.SQLite.DbMigrator` | Console database migrator |
| `RCynQ.Pages` | Static project page for GitHub Pages (no build step) |

Tests live in `Tests/<Project>.Tests`; `RCynQ.Conventions.Tests` checks the code rules of `AGENTS.md`. How to
build, test and package: [Development](Development.md).
