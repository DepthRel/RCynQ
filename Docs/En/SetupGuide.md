# RCynQ setup guide

**English** · [Deutsch](../De/SetupGuide.md) · [Español](../Es/SetupGuide.md) · [Français](../Fr/SetupGuide.md) · [Português](../Pt/SetupGuide.md) · [Русский](../Ru/SetupGuide.md)

Three computers take part:

| Node | Computer |
|---|---|
| **Workstation** | where you edit the project |
| **Origin** | where the private repositories are |
| **Relay** | one that both of the others can reach |

Do the steps in order. Where a step has variants, pick the one that fits the computer.

## 1. Check who reaches whom

| From | To | Port |
|---|---|---|
| Workstation | Relay | 22 |
| Origin | Relay | 22 |
| Relay | Origin | 22 (variant A in step 3) or 18444 (variant B) |
| Relay | Workstation | 22 (variant A) or 18444 (variant B) |

How the computers reach each other (local network, any VPN, public address) is up to you; RCynQ only needs the addresses to answer. Write down the address of each computer as its neighbour sees it.

**No Relay.** If the Workstation and the Origin reach each other directly, skip step 4 and every mention of the Relay.

## 2. Install

On every computer, one of:

| System | Package | Where the settings and data go |
|---|---|---|
| Windows | `RCynQ-<version>-setup.exe`: installs for the current user into `%LOCALAPPDATA%\Programs\RCynQ`, no administrator rights | `config` and `data` inside the installation |
| Windows | `RCynQ-<version>-win-x64.zip`: unpack into a folder you can write to, for example `C:\RCynQ`, not into `Program Files` | `config` and `data` next to the program |
| Debian, Ubuntu | `RCynQ-<version>-linux-x64.deb`: `sudo apt install ./RCynQ-<version>-linux-x64.deb`; Git and the SSH client come with it | `~/.config/RCynQ` and `~/.local/share/RCynQ` of each user |
| Linux, macOS | `RCynQ-<version>-<platform>.tar.gz`: unpack into a folder you can write to, for example `~/rcynq` | `config` and `data` next to the program |

Then start RCynQ: from the Start menu or `RCynQ.exe` (Windows), from the application menu or `rcynq-ui` (`.deb`),
`./RCynQ.sh` (archives for Linux and macOS). The `README.txt` of the package says the same for its system.

On the Workstation and the Origin, install Git and check it: `git --version`.

Use a clean package for every computer. Do not copy a folder in which RCynQ has already run: its `config` and `data` folders are the identity of that node.

## 3. Choose how the node accepts connections

The Relay always uses variant A. For the Workstation and the Origin choose per computer.

### Variant A — SSH server

The quickest way: **Role and nodes → Export configuration → Install the SSH server** (or `rcynq ssh install-server`) and confirm the administrator prompt. RCynQ installs, starts and opens the server itself; if that is impossible, it shows the commands to run by hand. The commands below do the same by hand.

**Windows** (PowerShell as administrator):

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service -Name sshd -StartupType Automatic
Start-Service sshd
```

If the first command fails with `0x800f0954`, install `OpenSSH-Win64-*.msi` from the [Win32-OpenSSH releases](https://github.com/PowerShell/Win32-OpenSSH/releases) with `msiexec /i <file> ADDLOCAL=Server`, start the service as above and open the port:

```powershell
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

**Linux:**

```bash
sudo apt install openssh-server      # or: sudo dnf install openssh-server
sudo systemctl enable --now ssh      # the service is named sshd on Fedora and RHEL
```

**macOS:** System Settings → General → Sharing → Remote Login.

Check: `ssh -V` prints a version, and the service runs (`Get-Service sshd` / `systemctl status ssh`).

### Variant B — own TLS port, no SSH server

For a computer where an SSH server cannot be installed. Open inbound TCP port `18444`.

The quickest way: **Role and nodes → Export configuration → Open port 18444 in the firewall** (or `rcynq endpoint open-port`) and confirm the administrator prompt. By hand:

**Windows** (PowerShell as administrator):

```powershell
New-NetFirewallRule -Name rcynq -DisplayName 'RCynQ' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 18444
```

**Linux:** `sudo ufw allow 18444/tcp` or `sudo firewall-cmd --permanent --add-port=18444/tcp && sudo firewall-cmd --reload`.

The SSH *client* is still needed on this computer when a Relay is used: `ssh -V`.

## The quickest way: invitations

Do steps 1–3 and set the role of each computer in the setup window. Then two lines of text set up the whole chain.

After the first setup the **Connect a node** wizard opens by itself; later it is under **Role and nodes → Connect a node**. It checks that the computer accepts neighbours, offers to install the SSH server when it does not, and draws the chain while it works.

1. On the Relay: in the wizard **Next**, then **Create the invitation** for the **Origin**, and copy it.
2. On the Origin: **Through a Relay → Next → I have an invitation**, paste it, **Join**. The wizard then offers **Invite a node**: create the invitation for the Workstation and copy it.
3. On the Workstation: **Through a Relay → Next → I have an invitation**, paste it, **Join**.

Without a Relay choose **Directly**: the Origin invites, the Workstation joins with its invitation. Each stage of joining is shown with its outcome; then go on with step 9.

An invitation is a secret: whoever has it may join once until it expires (1 hour, 24 hours or 7 days). Pass it privately. It carries the link token, so nothing has to be copied by hand. Command line: `rcynq invite --role Workstation`, `rcynq join <invitation>`, `rcynq invitation list`, `rcynq invitation revoke <id>`.

Steps 4–8 below are the same setup with files, for when an invitation cannot be used.

## 4. Set up the Relay

1. Start RCynQ. In the setup window set the node identifier (for example `relay-1`), role **Relay**, and save.
2. **Role and nodes → Export configuration.** Every address of the computer goes into the file; type a host name in as well if the Relay is reached under a name or behind NAT. Export `relay.rcynq.json`. Both endpoints import this one file, and each uses the address that answers it.

## 5. Set up the Origin

1. Start RCynQ. In the setup window set:
   - node identifier, for example `origin-1`;
   - role **Origin**;
   - the folder of the Git repository (the one that contains `.git`);
   - **How neighbours connect to this node** — *Through the SSH server of this computer* (variant A) or *Without an SSH server: own port with TLS* (variant B).
2. Copy the generated **shared link token**. The Workstation needs the same one.
3. Save.
4. **Role and nodes → Export configuration.** The key is created by itself if the computer has none. Export `origin.rcynq.json`.

The repository is named after its folder. That name is what the Workstation will see.

## 6. Set up the Workstation

1. Start RCynQ. In the setup window set:
   - node identifier, for example `workstation-1`;
   - role **Workstation**;
   - a folder for the working copy. An empty folder is fine: the first Pull fills it;
   - the **shared link token** copied from the Origin;
   - **How neighbours connect to this node** — variant A or B as in step 3.
2. Save.
3. **Role and nodes → Export configuration.** Export `workstation.rcynq.json`.

## 7. Exchange the node files

On each computer: **Role and nodes → Import configuration**, in any order. After each import RCynQ runs the check and says whether the link works.

| On | Import |
|---|---|
| Workstation | `relay.rcynq.json` and `origin.rcynq.json` |
| Origin | `relay.rcynq.json` and `workstation.rcynq.json` |
| Relay | `workstation.rcynq.json` and `origin.rcynq.json` |

Without a Relay: the Workstation imports `origin.rcynq.json`, the Origin imports `workstation.rcynq.json`.

The files hold no private keys. Pass them over a channel you trust.

## 8. Allow the connections

After each import RCynQ offers to allow the neighbour's key.

- **Variant A:** confirm. On Windows also confirm the administrator prompt. The key is limited to forwarding one port and cannot run commands.
- **Variant B:** nothing to do; there is no SSH server to configure.

If you declined, press **Role and nodes → Allow node connections**.

## 9. Check and pull

1. On the Workstation and the Origin: **Diagnostics → Run check**. Fix what is red; each failed check says how.
2. On the Workstation: **Branches → Pull from Origin**. The folder now holds the project.
3. Edit a file, open **Synchronization**, press **Send changes**. The file appears on the Origin.

From now on: **Pull** to get new commits, **Send changes** to deliver your edits. Changes made on the Origin are sent the same way from there.

## 10. More repositories

1. On the Origin: **Branches → Add a repository**, pick the folder of another Git repository.
2. On either node pick the repository in the list at the top of the window. The node switches without a restart.
3. On the Workstation, a repository that is not on the computer yet asks for a folder, then pulls itself.
4. The other node shows a notice: **Switch too** or **Stay**. Changes are exchanged only while both nodes work with the same repository.

Command line: `rcynq repo list`, `rcynq repo add <name> <path>`, `rcynq repo use <name>`, `rcynq repo remove <name>`.

Removing a repository from the list never deletes its folder or files.

## 11. Spare routes and several Relays

When a node of that role is in the chain already, an import changes nothing silently: it asks what the file is.

| What is imported | Choices |
|---|---|
| The same node at another address | **Another route to the same node** — the address is added to the routes of the node. **Replacement** — the new address takes the place of the one in use |
| Another Relay | **One more node** — the Relay joins the chain before the endpoint. **Replacement** — it takes the place of the chosen Relay |
| Another endpoint | **Replacement** only: a chain ends at one node |

A replaced node is forgotten together with its keys. After a replacement press **Allow node connections**, so its key loses its access.

**A spare route.** Example: two computers are joined by an AmneziaWG tunnel over UDP, and a tunnel over TCP runs next to it.

1. On the neighbour export the configuration once more, picking the address of the second tunnel.
2. Import the file and choose **Another route to the same node**.
3. Under the node in **Chain nodes** its routes appear with their state and latency.

**Automatic choice** is on by default: the most preferred route that answers is used, and among equals the fastest. When a route stops answering the traffic moves to the next one; when the better one answers again the node returns to it. The arrows change the priority (lower is preferred). The mark next to a route pins it by hand until you return to the automatic choice.

If the way leads through a Relay, import the second address there too: a Relay forwards only to addresses it knows.

**A chain of two Relays** (`Workstation → Relay 1 → Relay 2 → Origin`).

1. On the Workstation and the Origin import both Relays; for the second one choose **One more node**. The order is the order of import, counted from this node.
2. On each Relay import the neighbouring Relay and both endpoints.
3. On every node press **Allow node connections**.

Command line: `rcynq import <file> --as route|replace|hop [--link <link>]`. Without `--as` such an import changes nothing and lists the choices.

In `syncbridge.json` the spare addresses of a link are listed in `Routes` (`Host`, optional `Name`, `SshPort`, `SshUser`, `Priority`); the priority of the address of the link itself is `RoutePriority`.

## Updating from an earlier version

Earlier versions called the roles Source, Mediator and Dest. They are now Workstation, Relay and Origin.

1. Update **all** nodes together: old and new versions do not understand each other.
2. Start each node. Its configuration is converted by itself; the previous file is kept as `syncbridge.json.bak`.
3. On nodes with an SSH server press **Role and nodes → Allow node connections** once. It rewrites the old key lines, which allowed more than is needed now.

## If something does not work

| Symptom | Check |
|---|---|
| A link is offline | Run **Diagnostics** on that node. From the neighbour: `Test-NetConnection <address> -Port 22` (Windows) or `nc -vz <address> 22`. For variant B use port `18444`. |
| "The link token is missing or invalid" | The shared link token differs between the Workstation and the Origin. Copy it again on the **Role and nodes** tab. |
| The key of a node is missing | Export its file again with **Include the node's public SSH key** ticked, and import it. |
| "works with repository … and the patch is for …" | The nodes work with different repositories. Switch one of them. |
| Pull refuses because of local changes | The listed files were edited and not sent. Send them first, or undo the edits. |
| The address of a node changed | Export its file with the new address, import it at the neighbours, press **Allow node connections**. |

Logs are on the **Logs** tab and in `data/logs`.
