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

- RCynQ never pushes and never creates a commit, a remote, a setting or a ref of its own in a Git repository on an Origin, and does not refresh its index (Git runs with `GIT_OPTIONAL_LOCKS=0`).
- The working tree and the checked out branch on the Origin change only by what the user sent: the files of a patch, or, when the Origin accepts commits, the user's own commits, to which the branch is fast-forwarded. The reflog of the branch keeps that move, so plain Git undoes it.
- Everything the Origin stores (the list of repositories, the sync base, history, backups, commits waiting to be applied) lives in `syncbridge.json` and in the data folder of the node.
- Git housekeeping (creating the repository in an empty folder, the `rcynq` remote, `refs/remotes/rcynq/*`, fast-forward on Pull) happens only in the working copy on the Workstation.

End-to-end tests compare every file under `.git` before and after registering, switching, serving and inspecting a repository, and before and after a delivery of commits that is refused or has to wait.

## Channels

Both channels use the same connection and the same authorization.

**Git channel (Origin → Workstation).** The Workstation names a repository and the commits it already has. The Origin answers with an incremental Git bundle. The Workstation fetches it into `refs/remotes/rcynq/*` and fast-forwards. Nothing is ever pushed. Then the Origin lists all its branches, and `refs/remotes/rcynq/*` is brought in line with that list: a bundle carries only branches with new commits, so a branch created at a commit the Workstation has, a branch moved back and a deleted branch arrive this way. A Pull makes the current branch follow `rcynq/<branch>`, so Git compares it with the Origin and not with a remote this computer cannot reach.

**Patch channel (both directions).** A patch is the difference between the working tree and the *sync base* — what the other node is known to have. The sync base is built automatically from the Git commit both nodes share, plus the files already exchanged. Delivery: a signed manifest, then each file by its SHA-256, then a commit that applies everything or nothing. A conflict is resolved by keeping the local file, taking the incoming one (with a backup) or a three-way Git merge.

**Delivery of commits (Workstation → Origin).** Commits prepared on the Workstation (cherry-picks, merges) reach the Origin as they are: the same hashes, authors and order. The Workstation sends a block of its unsent commits that starts with the oldest one, as a Git bundle with a signed envelope; the patch session carries it. The Origin takes it only when the user switched **Accept commits from the Workstation** on, only for the branch it has checked out and only when its `HEAD` is the commit the block is built on; otherwise it refuses at once. The objects are unpacked into a quarantine beside the repository and checked against the signed list (the first-parent chain, every path a commit touches); only then are the packs moved into `.git` and the branch fast-forwarded. When the fast-forward fails, the packs are taken out again. While the working tree of the Origin has changes, the commits wait: the user applies or declines them there, and patches of files are refused meanwhile. After the Origin applied them, a Fetch makes the Workstation even with it. While a branch is right on top of the commit of the Origin, its commits are not changes to send: the patch channel offers only what is not committed, still against the version the Origin has. A Workstation rolls back unsent commits (Soft, Mixed, Hard) only while its working tree has no changes.

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

## Invitations

A node invites a neighbour with a single-use ticket: a random secret, of which the node keeps only the SHA-256, a role and an expiry. The invitation carries the card of the node, the cards of the Relays on the way, the ticket, the link token for the counterpart, and the ticket of the Relay when the node holds one.

- **Through the SSH server.** The ticket comes with a one-time Ed25519 key. Its line in `authorized_keys` is `restrict,expiry-time=…,command="rcynq enroll --config …"`: it runs nothing but the enrollment and the server refuses it after the expiry. The joining node logs in with it, through the Relays as jump hosts, and sends its card as the command; `rcynq enroll` hands it to the service on loopback. The line is removed once the ticket is used, revoked or expired.
- **Through the own TLS port.** The enrollment is posted to `/peer/enroll` of that port; the certificate fingerprint of the invitation is pinned.

The inviting node checks the ticket, uses it up in one step, imports the card and allows the key. A Relay answers an endpoint with a ticket for the opposite one, which the endpoint passes on in its own invitation. The host key or certificate of every node is pinned from the invitation before anything is sent. A Relay takes enrollments only for a way through one Relay; a longer way needs files.

## Security of the exchange

| Protection | Mechanism |
|---|---|
| Who may send | Shared link token and a known peer identifier in every request |
| Integrity and origin | ECDSA P-256 signature over the manifest, including the repository name |
| Replay | Patch identifier and age are checked; a patch is accepted once |
| Path traversal | Paths in a patch are canonical and must stay inside the working tree |
| Wrong repository | A patch for a repository the receiver does not work with is refused |
| Exposure of the Origin | A neighbour learns repository names only, never paths; a bundle is served only for a registered name |
| Commits | Off until the user allows them on the Origin; branch, base, every commit and the hash of the bundle are signed; objects stay in a quarantine until checked; only a fast-forward of the checked out branch |
| Joining | Single-use, expiring ticket (hash stored); one-time SSH key restricted to the enrollment command with `expiry-time`; host key and certificate pinned from the invitation |

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
