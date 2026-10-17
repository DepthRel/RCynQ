# Easy node setup: design

Status: draft for review. A working document of the change, not part of the user documentation set
(`Docs/<Language>`); it is removed or folded into `Docs/<Language>/Architecture.md` when the work is done.

## 1. Goal

A person who has the three computers at hand sets up RCynQ without reading the setup guide. Today the guide asks for
4–5 exports, a token copied by hand, six file transfers in a fixed order, an address chosen per export, separate
confirmations of keys and a page of PowerShell or shell commands for the SSH server.

Success:

- Direct link: **one** string moves from the Origin to the Workstation (or back), nothing else.
- Through a Relay: **two** strings move: Relay → first endpoint, first endpoint → second endpoint.
- The manual way keeps working and gets simpler: one file per node, any import order, no address to choose.
- An SSH server is installed and started with one button (Windows, Linux, macOS).
- A setup wizard leads through the scenario and shows the progress of the connection with an animation.
- Security is not weaker than today: pinned host keys and certificate fingerprints, restricted `authorized_keys`
  lines, signed patches, the link token. New secrets are single-use and expire.
- Nodes of 0.1.0 keep working with the new ones: an old export is still imported, a new export still carries the
  fields an old node reads.

Out of scope: a Relay that is set up by someone who does not have the other computers; discovery of nodes on the
network; QR codes; a change of the patch or Git channel.

## 2. Parts and order

The work is split into four parts; each one can be released on its own.

| Part | What | Depends on |
|---|---|---|
| A | Node card v2: all addresses in one export, import in any order, fewer questions | — |
| B | SSH server in one button; TLS port opened in one button | — |
| C | Invitations and enrollment | A |
| D | Setup wizard with progress animation | A, B, C |

## 3. Part A — node card v2

### 3.1 Format

`ExportedNodeConfiguration` keeps `FormatVersion = 1` and gets two optional fields:

- `Addresses`: the list of addresses the node is reachable at, in order of preference.
- `ProtocolVersion`: an integer, `1` for this release; it rises when nodes of different versions can no longer talk.
  A missing value means a node of 0.1.0.

The format version does not rise: a 0.1.0 node refuses any other version, but it ignores unknown fields, so it reads
a new file as a single-address export. `Host` stays and equals `Addresses[0]`. An old file (no `Addresses`) is
imported as before, as an address list of one. "Card v2" below means a file with these fields.

### 3.2 Export

- No address is chosen. The export takes every address of the probe except loopback and link-local, ordered by
  `NetworkAddressClassifier.Rank`. The dialog shows them as a check list (all checked) for the person who wants to hide
  one; a host name typed by hand is added at the top.
- The SSH key is created when the node has none, without asking; it and the host key fingerprint are always included
  (today they are check boxes).
- A Relay makes one export for both sides: each side takes the address it can reach (3.3).

### 3.3 Import

- Every address of the card becomes a route of the link (`LinkRoute`), priority by position (`0, 10, 20, …`). The
  existing route checks (every 15 s) find the address that answers and switch to it; through Relays the check runs
  through the SSH forward, as it does today. A card with one address creates a link without routes, as today.
- A known node is refreshed: its routes become the addresses of the new card; a pinned route stays pinned if its
  address is still in the card. The "alternative route or replace" question is asked only when the card is a
  different node of a role that already has a link (unchanged).
- A Relay that imports an endpoint allows forwarding to every address of the card (the planner already allows every
  route address).
- Import order does not matter. Tests import every permutation of the cards of a scenario and compare the resulting
  chain; whatever depends on the order today is fixed.
- After an import the node allows the key of the neighbour with one confirmation (on Windows plus the administrator
  prompt, unchanged) and then runs the diagnostics by itself.

## 4. Part B — SSH server in one button

A service `IServerSetup` (`ServerSetupService`, project `RCynQ.Ssh`, folder `Server`) that runs the plan of
`ServerSetupPlanner` for the system at hand through an `IElevatedProcessLauncher`, which is replaced by a stub in tests.

| System | Command, run elevated | Elevation |
|---|---|---|
| Windows | `Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0`, `Set-Service sshd -StartupType Automatic`, `Start-Service sshd`, the firewall rule for port 22 | `powershell -Verb RunAs` (UAC) |
| Linux | install `openssh-server` with the package manager found in `/etc/os-release` (`apt-get`, `dnf`, `zypper`, `pacman`), then `systemctl enable --now ssh` or `sshd` | `pkexec` |
| macOS | `systemsetup -setremotelogin on` | `osascript … with administrator privileges` |

- The commands are fixed strings inside the program; nothing typed by the user goes into them.
- When elevation is refused, `pkexec` is missing, the package manager is unknown or Windows answers `0x800f0954`,
  the result names the reason and carries the commands to run by hand (the ones of the setup guide); the UI shows them
  with a copy button. macOS also offers to open the Sharing settings.
- The same service opens the TLS port of variant B in the Windows firewall (`New-NetFirewallRule`, port of the
  configuration) and on Linux with `ufw` or `firewall-cmd` when one of them is active.
- Afterwards the probe runs again; the button turns into a green check when `sshd` answers.
- The commands run in the desktop process (the application or the CLI), never in the service: only a process of the
  user's session can show the prompt for administrator rights. The planner (`ServerSetupPlanner`) is a pure function of
  the system and the task; the runner is behind `IElevatedProcessLauncher`.
- Entry points: the export dialog (when the SSH server or the TLS port does not answer), the setup wizard (Part D), the
  CLI `rcynq ssh install-server` and `rcynq endpoint open-port`.
- Nothing runs unnoticed: every run passes the prompt for administrator rights and its outcome is shown.

## 5. Part C — invitations and enrollment

### 5.1 Idea

A node *invites* a neighbour: it creates a single-use **ticket** and shows an **invitation** — one line of text (also
savable as a `.rcynq-invite` file). The invited node *enrolls*: it connects to the inviter with the ticket, sends its
own card and receives the card of the inviter. Both sides store each other and allow each other's keys. No file goes
back.

### 5.2 The invitation

`rcynq-invite:` followed by base64url of the deflated UTF-8 JSON:

| Field | Meaning |
|---|---|
| `v` | format version, `1` |
| `protocol` | `ProtocolVersion` of the inviter |
| `inviter` | the card v2 of the inviter (public data, including host key or TLS fingerprint: the invited node pins them) |
| `path` | cards of the Relays the invited node needs to reach the inviter, in order (empty for a direct link) |
| `relayTicket` | a ticket of the Relay for the opposite role, when the inviter got one at its own enrollment (5.5) |
| `ticket` | the ticket of the inviter: `id`, `secret` (32 random bytes), `expiresAt`, and for an SSH-server inviter the private part of a one-time Ed25519 key |
| `linkToken` | the link token, only in an invitation from a Workstation or an Origin to its counterpart |
| `role` | the role the invited node must have |

The invitation is a secret: whoever has it may join once until it expires. The UI says so next to the copy button.
Expiry: 1 hour, 24 hours (default) or 7 days. A ticket is used once; a used, expired or revoked ticket is refused with
a clear message. Open invitations are listed and can be revoked.

### 5.3 How the invited node reaches the inviter

No new port is opened anywhere.

- **Inviter with an SSH server (every Relay, an endpoint of variant A).** The ticket comes with a one-time Ed25519 key.
  Its public part is written to `authorized_keys` as

  `restrict,expiry-time="<local time>",command="<path to rcynq> enroll --config <configuration>" <key> rcynq:enroll-<ticket id>`

  The invited node logs in with the private part (through the Relays of `path` as jump hosts, pinning the host key from
  the card) and sends the request, Base64url of its JSON, as the command, which the server passes on in
  `SSH_ORIGINAL_COMMAND`. The command hands the request to `POST /peer/enroll` of the running service on loopback and
  prints the answer. The line is removed when the ticket is used, expires or is
  revoked; `expiry-time` makes the SSH server refuse it even if the program never runs again.
- **Inviter with its own TLS port (variant B).** The request goes to `POST /peer/enroll` of that port (straight, or through
  the SSH forward of the last Relay); the certificate fingerprint of the card is pinned.

### 5.4 The exchange

Request: `ticket id`, the ticket secret itself, the card v2 of the invited node and its language. The secret travels
only over a channel already encrypted to the pinned inviter (SSH with the pinned host key, TLS with the pinned
certificate), so an HMAC adds nothing; the inviter keeps only the SHA-256 of the secret and compares in constant time.
Answer: `accepted` and a message in the language of the request; for a Relay a `relayTicket` for the opposite role
(valid 7 days); `accessPending` when the inviter could not write `authorized_keys` without administrator rights.

The inviter checks: the ticket exists, is unused, unrevoked and unexpired, the secret matches (one answer for all of
these, so a refusal does not tell whether a ticket exists); the role of the card is the one of the ticket; `protocol`
is equal; the card is valid (the checks of an import). It then uses the ticket up atomically (a conditional UPDATE),
imports the card (a Relay keeps a second endpoint of a role as one more hop, an endpoint refuses), and applies the
access lines, which also removes the line of the used key.

The invited node imports the cards of the invitation first, enrolls, then stores the link token (running options and
configuration file) and applies its own access lines. A refusal of the Relay does not stop it: on a second run with
the same invitation the Relay ticket is used up although the Relay knows the node; when it does not, the next stage
fails with the reason. A Relay that took the node but waits for administrator rights stops it.

### 5.5 The scenarios

**Direct.** The Origin invites a Workstation (or the other way round). One string.

**Through a Relay.**

1. The Relay creates an invitation for an endpoint (either role).
2. The first endpoint (say the Origin) pastes it and enrolls at the Relay. The Relay answers with a `relayTicket` for
   the opposite role.
3. The Origin creates an invitation for the Workstation. It carries the Origin card, the Relay card in `path`, the
   `relayTicket`, the link token and the Origin ticket.
4. The Workstation pastes it, enrolls at the Relay with the `relayTicket`, then reaches the Origin through the Relay
   and enrolls there with the Origin ticket.

Two strings. A way through several Relays is not enrolled automatically: every Relay on it has to know the joining
node, and only the nearest Relay of the inviter hands out a ticket. The invitation says so in a note; the files remain
for such a chain.

### 5.6 Limits and what was checked

- On Windows the keys of an administrator account live in `C:\ProgramData\ssh\administrators_authorized_keys`, which
  needs administrator rights to change. Creating an invitation then reports `accessPending` and the application asks
  for the administrator prompt (`rcynq ssh allow`, as for an import). An enrollment over SSH on such a node is taken,
  but the key of the joined node waits for the same prompt; a Relay without a person at it should run RCynQ under a
  non-administrator account.
- `expiry-time` is in the local time of the inviter, without the `Z` suffix older servers do not know.
- The forced command names the command line tool: the application passes its path to the service (`--cli`,
  `--cli-arg`); a service started without the application looks for `rcynq` in `../cli` and beside itself; the CLI
  names itself. Words with a double quote, control characters, `%` or `!` (Windows) or a single quote (POSIX) are
  refused.
- Checked against a running Windows OpenSSH server (a debug instance on loopback): the line with `restrict`,
  `expiry-time` and the forced command is accepted, `SSH_ORIGINAL_COMMAND` carries the request, a program in a folder
  with spaces starts, and the answer comes back to `EnrollmentChannel` through the real client. The server puts the
  command in quotes itself before `cmd.exe /c`, so only words with spaces get quotes; one more outer pair breaks it. The
  temporary key file with owner-only permissions is accepted by the Windows client.
- Not checked yet on a Linux sshd and on the live Relay.

## 6. Part D — the setup wizard

As built. The first-run setup window stays (role, folder, connection mode, before the service runs); the
**Connect a node** wizard (`ConnectWizardViewModel`, window `ConnectWizard`) needs the running service and takes over
from there. It opens by itself after the first setup of a node without links, and from **Role and nodes → Connect a
node** at any time; the separate invite and join dialogs of Part C are its pages now.

1. **Scenario** (not for a Relay): through a Relay or directly.
2. **This computer**: the probe says whether the SSH server or the TLS port answers; when not, the panel of Part B
   (`ServerSetupPanelViewModel`, shared with the export dialog) offers to install the server or open the port.
3. **Action** (not for a Relay): "I have an invitation" or "Invite a node", with a hint for the role and the way.
4. **Invite**: role (a Relay chooses), lifetime, create, copy or save; then the page looks at the list of invitations
   every 3 seconds until the invitation is used and shows "*node* joined". A one-time key that needs administrator
   rights is passed to the prompt of the page.
5. **Join**: paste or open a file, join; every stage is listed with its outcome.
6. **Done**: after joining the invitation of a Relay the wizard offers to invite the counterpart next; a Workstation
   is pointed to Branches → Pull.

Above the pages `ChainAnimationView` draws the chain (this node on the left) in the colours of the logo: bytes run
along the links while an invitation waits or a joining runs, finished links turn green, the failed one red. It is drawn
in `Render` with a 30 fps timer that runs only while something moves and the control is shown.

Live progress: `JoinService` reports the start and the end of every stage to an awaited callback, so the order holds;
the service publishes them as `JoinProgressEvent` (SignalR `JoinProgress`) with the role of the inviting node once the
invitation is read. The main window passes them to the open wizard: the stage appears as running in the list, the
bytes move to the link it works on (the Relay is the first link; the inviting node is reached over the first link when
it is the Relay itself, otherwise over the last one), and the link turns green or red when the stage ends. The answer
of `POST /config/join` still carries every stage, so a lost event changes nothing in the end.

Administrator account: the probe reports `EnvironmentWarning.AccessNeedsAdministrator` when this node uses its SSH
server and its `authorized_keys` cannot be written without administrator rights. The export dialog lists it with the
other warnings; the readiness page of the wizard explains it, and on a Relay advises to run RCynQ under an ordinary
account, since nobody may sit there to confirm the prompt.

## 7. Errors

Every refusal has a localized key and says what to do: invitation damaged or for another role, expired, already used,
revoked; inviter not reachable (with the address tried); host key or certificate does not match the invitation;
protocol versions differ (which side to update); elevation refused; the SSH server could not be installed (with the
commands by hand).

## 8. Testing

- Card v2: serialize/parse, v1 files, a v2 file read as v1.
- Import: routes from addresses, refresh of a known node, every import order of the scenarios gives the same chain.
- SSH server setup: per system with a stubbed runner and file system (commands, refusal, unknown package manager).
- Invitation: encode/decode, damage, wrong role, expiry and single use with `FakeTimeProvider`, revocation.
- Enrollment: the check of the secret against its stored hash, the restricted ephemeral line (`restrict`, `expiry-time`, forced command, quoting, rejection
  of unsafe paths), removal of the line, the one-step import and access, the relay ticket.
- An integration test of enrollment over the local OpenSSH where available (skipped otherwise), like the existing jump
  host test.
- Wizard view models: the steps of every scenario, the progress states.
- Localization parity and conventions as for every change.

## 9. Documentation

`Docs/<Language>/SetupGuide.md` is rewritten around the wizard and the invitations, with the manual way as a second
section; `Architecture.md` gets the enrollment and its protections. All six languages at once.

## 10. Decisions to confirm

1. The invitation carries the one-time SSH key, the ticket secret and the link token: it is a secret, single-use,
   24 hours by default.
2. Enrollment runs over the SSH port (forced command) or the TLS port of the inviter; no new port.
3. The inviter does not have to confirm a joining node by hand; it is notified and the event is audited. A leaked
   invitation shows itself: the rightful node is refused with "already used".
4. A new card of a known node replaces its routes.
5. Part order A → B → C → D, each part released on its own.
