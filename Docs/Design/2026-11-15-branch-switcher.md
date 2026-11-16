# Branch switcher: design

Status: implemented. A working document of the change, not part of the user documentation set (`Docs/<Language>`); what
stays true goes into `Docs/<Language>/Architecture.md` and the user guides.

## 1. Goal

On the Branches tab of a Workstation the user switches the working tree of the current repository to another branch
without leaving RCynQ: an Origin branch to work on, or a local experiment to come back to. Only the local working copy
changes; the Origin is not touched.

Success:

- Next to the current branch (which stays as it is) a drop-down list with a search by name lists every local branch
  and every branch fetched from the Origin; choosing one switches the working tree.
- Each row shows at a glance whether the branch is on this computer and in line with the Origin, only on the Origin
  (not pulled yet), or only local.
- Nothing typed on the Workstation is lost: uncommitted changes are carried over or saved to a `.patch` file by the
  user's choice, never discarded silently; commits are never left unreachable.

Non-goals: switching the branch of the Origin (its repository is only read); creating, renaming or deleting branches;
merging or rebasing; automatic per-branch shelving of changes.

Anti-goals: a switch that silently drops uncommitted work; a switch that leaves commits reachable from no branch; a
switch that silently makes the next patch land on a different branch of the Origin.

## 2. Approaches considered

For uncommitted changes in the working tree:

1. **Refuse only**: list the files, ask the user to send or undo them first. Safe but tedious.
2. **Dialog with a choice** (chosen): carry the changes over (plain `git switch`, which refuses when Git cannot carry
   them and then changes nothing) or save them to a `.patch` file and take them out, then switch; or cancel. No
   "discard" button.
3. **Automatic shelving per branch** (stash on leave, pop on return): most convenient, but hidden state in the stash
   and conflicts on return without context. Rejected.

## 3. The list

One row per branch name:

| Status | When | Look |
|---|---|---|
| Pulled | local `X` and `rcynq/X` exist | success colour (green); `↑a ↓b` when they differ |
| On Origin, not pulled | `rcynq/X` exists, local `X` does not | accent colour (blue); choosing it creates `X` from `rcynq/X` and makes it track `rcynq/X` |
| Local only | local `X` exists, `rcynq/X` does not | muted colour (grey; experiments) |

- A filter button next to the search opens a panel beside the list with two independent axes: presence (pulled,
  meaning a local branch exists, local-only ones included; or not pulled) and location (on the Origin or only local).
  Choices of one axis add up, the axes narrow each other, an axis with nothing checked does not narrow. A check mark
  takes effect at once; Apply and Reset close only the panel, and a click outside the panel closes only the panel.
- The current branch is marked in the list. Rows are sorted by name; the search matches any part of the name, ignoring
  case.
- Branches of other remotes of the working copy (`origin/*` and the like) are not listed: this computer cannot reach
  those remotes, which is why RCynQ exists, and their copies are usually out of date. A local branch that tracks one of
  them is listed as a local branch.
- The colour never carries the meaning alone: every row has a short status text or tooltip (accessibility, and the
  same in every theme).
- The list is shown only on a Workstation with a Git working copy; Origins and Relays keep the tab as it is.

## 4. Switching

1. Take the working tree lock (`WorkingTreeLock`), as Pull does.
2. Detached HEAD with commits reachable from no branch, remote branch or tag: refuse and name the commits; the user
   creates a branch first.
3. Working tree clean (untracked files ignored by Git do not count): switch.
4. Otherwise the UI shows a dialog with the changed files and three buttons:
   - **Carry over to the new branch**: `git switch <X>` without `--force` / `--merge`. If Git refuses, nothing changed;
     the message names the files that stand in the way.
   - **Save to a .patch file and switch**: the patch of the working tree against `HEAD` (tracked and untracked files,
     the same format `Apply patch` reads), written to a file the user picks; only after it is written, the changes are
     taken out by applying that file in reverse, then the switch. If any step fails before the switch, the working
     tree is as it was. The file is picked while the dialog is still open (giving the picker up leads back to it), as
     in the dialog of saving a patch. Right after the switch a second dialog offers to bring the changes back from that
     same file, without picking it again: **Bring back** applies it whole (`git apply`, all or nothing), **Not now**
     leaves it. When it does not fit the new branch, nothing changes, the message says the branch was switched, names
     the files and the patch file, and `Apply patch` on the same tab brings it in later, whole or in part.
   - **Cancel**.
5. Leaving a branch with commits the Origin does not have: no question; the result says "branch `Y` keeps N commits
   that are not on the Origin".
6. After the switch: the branch info, the Sync tab and the sync base follow the new branch on their own
   (`GitSyncBaseLocator` reads the current branch). A new local branch made from `rcynq/X` tracks `rcynq/X`.
7. Every switch leaves a note in the service log (from, to, carried or saved, the SHA-256 of a saved patch).

Changes already delivered to the Origin are not discarded silently on a switch (unlike Pull): they stay in the
Origin's working tree, and dropping them here would hide that from the user.

## 5. Warning about the branch of the Origin

The answer of the Origin to the branch list already names its checked out branch (`GitBranchListResponse.Head`). After
every Fetch/Pull the Workstation records it as `refs/remotes/rcynq/HEAD` (a symbolic ref in the Workstation working copy,
like `origin/HEAD`; housekeeping that is allowed there). When the current branch differs from it, the Branches tab
shows under the current branch: "The Origin has `main` checked out: changes you send land there." It does not block the
switch or the sending.

## 6. Command line

- `rcynq git branches` prints the same list with the same statuses.
- `rcynq git switch <branch> [--carry | --save-patch <file> [--force]]`: without an option and with a dirty working
  tree it refuses and lists the files; `--force` replaces an existing patch file.

## 7. Constraints

- The Origin repository is not touched; everything happens in the Workstation working copy.
- Branch names from Git and from the request are validated (`GitNames.IsBranch`) before they reach a Git command; a
  name starting with `-` never reaches the command line as an option.
- Texts in all six languages; the documentation in `Docs/<Language>` describes the switcher.
- Unit tests with Git replaced by stubs (statuses, refusal on detached HEAD, carry refused, save-then-switch order,
  failure before the switch leaves the tree as it was) and an integration test against a real repository.

## 8. How it was built

- The switcher is a button with a flyout: a search box and a list. Enter takes the first match, Down moves into the
  list, a click or Enter in the list switches.
- API: `GET /git/switch` (the list, the current branch and the branch of the Origin), `POST /git/switch` (the switch:
  `IfClean`, `CarryChanges` or `SavePatch` with the bytes of the patch), `POST /git/switch/patch` (the patch of the
  working tree against `HEAD`). The UI writes the file between the last two calls and brings the changes back with
  `POST /patches/file/apply` from the bytes it wrote.
- Git: `IGitBranchService` (`RCynQ.Git`) lists, switches and records `rcynq/HEAD`; `BranchSwitchService`
  (`RCynQ.Sync`) holds the lock and the rules above; `GitChannelService` records the branch of the Origin after the
  branch list of every Fetch.
