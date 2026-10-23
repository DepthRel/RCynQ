# Commit delivery: design

Status: approved, in implementation. A working document of the change, not part of the user documentation set
(`Docs/<Language>`); it is removed or folded into `Docs/<Language>/Architecture.md` when the work is done.

## 1. Goal

A branch is prepared on the Workstation (cherry-picks, merges) and its new commits have to reach the Origin as they
are: the same commits, the same hashes, the same order, the same authors, lying in the local branch of the Origin and
ready for the user to push from there. RCynQ still never pushes and never creates a commit.

Success:

- The Workstation lists the commits of the current branch that the Origin does not have yet and sends a contiguous
  block of them, starting from the oldest, with one button.
- The Origin fast-forwards the same branch to the last commit of the block; `git log` shows the same hashes on both
  sides.
- Nothing reaches the `.git` of the Origin when receiving commits is switched off, when the versions differ, or when
  applying fails: objects of a failed delivery are removed.
- The Workstation can roll back its own unsent commits (Soft, Mixed, Hard) when its working tree has no changes.
- Patch history on both nodes records the delivery as commits, with their number.
- The same is available from the command line.

Out of scope: rewriting the author or the committer; creating, rebasing or recreating commits; creating a branch on the
Origin; delivering to a branch that is not checked out on the Origin; Git LFS objects; submodule content; running
`pre-commit`/`commit-msg` hooks; commit signing. RCynQ delivers *changes*; this feature only carries commits that
already exist on the Workstation.

## 2. Terms

| Term | Meaning |
|---|---|
| Branch | The branch checked out on the Workstation. The Origin must have the same branch checked out. |
| Origin tip | `refs/remotes/rcynq/<branch>` on the Workstation; on the Origin, its `HEAD`. |
| Unsent commits | `git rev-list --first-parent <Origin tip>..HEAD` on the Workstation, oldest first. A merge commit is one row; the commits it brings in travel with it. |
| Block | The commits chosen to send: unsent commits 1..k, always starting from the oldest. |
| Base | The parent of the first commit of the block. It must equal the Origin tip. |
| Tip | The last commit of the block. |
| Delivery | One transfer of a block: a signed envelope plus one Git bundle. |

## 3. The rule about the Origin repository

The rule "the repository on the Origin is only read" becomes:

> RCynQ never pushes and never creates a commit, a remote or a setting on the Origin. The working tree and the
> checked out branch change only by what the user sent: the files of a patch, or, when the Origin allows it, the
> user's own commits, which fast-forward the branch.

- Receiving commits is off by default (`AcceptCommits = false` in `syncbridge.json`, a toggle in the settings of an
  Origin).
- A fast-forward leaves an entry in the reflog of the branch, so the user rolls it back on the Origin with plain Git.
- Every other operation (registering, switching, serving, inspecting, file patches) keeps its read-only guarantee and
  its end-to-end test.

## 4. Workstation: the list of unsent commits

Shown on the Sync tab of a Workstation, above "Patch history", only when the branch has unsent commits.

- Columns: date, short hash, author, subject. Newest on top, as in `git log`.
- The list is built only when `refs/remotes/rcynq/<branch>` exists and is an ancestor of `HEAD`. Otherwise the area
  shows why and offers nothing:
  - no such branch on the Origin: "The Origin has no branch `<branch>`; commits are delivered only to an existing
    branch";
  - the branch diverged from the Origin: "The branch diverged from the Origin; do a Pull first";
  - detached `HEAD`: "No branch is checked out".
- Check marks keep the block contiguous from the oldest commit: checking a commit checks every older one; clearing a
  commit clears every newer one. A check box that would break the rule cannot exist, so no error is needed.
- The header holds "Send commits" (enabled when the block is not empty) and "Roll back commits" (muted red, enabled
  when there are unsent commits).
- At most `MAX_DELIVERY_COMMITS = 1000` rows are listed; with more, the area says so and offers nothing.
- Commits that wait for applying on the Origin (section 7) are marked in the list.

The files of these commits are not listed in "Changes to send": while the branch is right on top of the Origin tip, the
changes of the working tree are counted from `HEAD` (`SyncBaseStatus.ChangesSince`), and the sync base stays the version
of the Origin, so an uncommitted edit of a committed file is still sent against what the Origin has. A branch that
diverged from the Origin cannot send its commits as commits, so their files stay among the changes to send, as before.

## 5. Sending

1. The Workstation checks the block locally: the branch is checked out, the base equals `refs/remotes/rcynq/<branch>`,
   the tip is reachable from `HEAD` by first parents.
2. A Git bundle of `<base>..<tip>` is written to the temporary folder of the node. Git bundles only references, so the
   tip is named for the time of the call by `refs/rcynq/outgoing-<guid>` in the working copy, removed afterwards.
3. A delivery envelope is built and signed (ECDSA P-256, as a patch). The signed payload carries: delivery id, sender,
   receiver, time, repository name, branch, base, tip, number of commits, the list of commit hashes (first-parent
   order), SHA-256 and size of the bundle.
4. The envelope opens a session on a new peer route `POST /patch/sessions/commits`. An Origin of an older version answers 404,
   which becomes "The Origin does not accept commits: update RCynQ on it". The Origin decides everything it can at this
   point (section 6.1), so a refusal costs no upload.
5. The bundle is uploaded through the existing session routes (content by SHA-256), then the session is committed.
6. The answer is one of: applied, waiting on the Origin, rejected (with the reason).
7. Applied: the Workstation runs a Fetch by itself. `refs/remotes/rcynq/<branch>` moves to the tip; the delivered
   commits leave the list; the Workstation says "The Origin took N commits; branch `<branch>` is in step with it". No
   rollback and no Pull are needed: the hashes are the same on both sides.
8. The temporary bundle is deleted in every case.

The bundle size limit is the existing `MaxFileBytes` of the patch sender (500 MB).

## 6. Origin: receiving

### 6.1 At session open

1. Patch admission as today: role, link token, known Workstation peer with a signing key, signature, receiver,
   replay, repository name equal to the active one.
2. `AcceptCommits` is on; otherwise rejected (`CommitsNotAccepted`): "This Origin does not accept commits from the
   Workstation. Turn it on in the settings of the Origin".
3. The checked out branch equals the branch of the envelope and `HEAD` equals its base; otherwise rejected
   (`VersionMismatch`): "Versions differ: the Origin is at `<branch>` `<hash>`, the commits are built on `<branch>`
   `<hash>`". No rebase, no other branch.
4. Only one delivery may wait per repository; while one waits, a new one is rejected (`CommitsWaiting`): "Commits of
   an earlier delivery are waiting to be applied on the Origin".

### 6.2 At session commit

Under the `WorkingTreeLock`:

1. The bundle is complete and its SHA-256 matches.
2. Objects are unpacked into a **quarantine**: a temporary object directory in the data folder of the node, with the
   repository's object directory as an alternate (`GIT_OBJECT_DIRECTORY`, `GIT_ALTERNATE_OBJECT_DIRECTORIES`).
   `git bundle unbundle` refuses a bundle whose prerequisites the repository lacks. Nothing is written into `.git` yet.
3. The content is checked against the signed envelope: the bundle carries one reference, at the tip; the first-parent
   chain from base to tip is exactly the listed hashes, in order; every path the commits touch (merges included) passes
   the rules of a path of a patch (canonical, inside the tree, no `.git`). `git bundle unbundle` does not fsck and a
   full fsck would scan the whole repository, so the paths are checked instead; Git's own checkout protections apply
   on the fast-forward as well.
4. The branch and `HEAD` are checked again (step 6.1.3).
5. The working tree is checked: any change relative to `HEAD` (modified, staged, deleted, or a new file that is not
   ignored) means the delivery **waits** (6.3). Otherwise it is applied (6.4).

A failure in 1–4 rejects the delivery, the quarantine is deleted, `.git` is untouched.

### 6.3 Waiting

- The delivery gets the new status `Waiting`; the bundle stays in the patch storage of the node (not in `.git`); the
  quarantine is deleted.
- The Workstation is answered "waiting on the Origin".
- The Origin shows a banner on its Sync tab: "Commits from the Workstation are waiting: the working tree has changes
  (`<files>`). Deal with them (stash or discard) and press Apply". The history row has "Apply" and
  "Decline".
- **Apply** repeats 6.2 from step 2 in full: quarantine, content, branch, `HEAD`, working tree. Clean and unchanged:
  applied (6.4). Still not possible (changes in the tree, another branch, `HEAD` moved): the reason is shown and the
  delivery keeps waiting.
- **Decline** deletes the stored bundle and records the delivery as rejected by the user.
- A waiting delivery does not expire and data retention keeps it. It belongs to its repository: while another
  repository is active, Apply says so and nothing happens.
- File patches are refused while commits wait (`CommitsWaiting`): "Commits from the Workstation are waiting to be
  applied on the Origin; patches are taken again once they are applied or declined". A patch would change the working
  tree the commits wait to be applied on.

### 6.4 Applying

1. The pack files of the quarantine are moved into `.git/objects/pack` (the `.pack` before its `.idx`); their names are
   remembered.
2. `git merge --ff-only <tip>`: the branch, the index and the working tree move to the tip; the reflog records it.
3. If step 2 fails, the moved pack files are deleted (nothing refers to them yet): `.git` is as before, and the
   delivery is recorded as failed with the output of Git.
4. The quarantine is deleted in every case.
5. The sync base of the Origin is rebuilt from the new `HEAD`; history records "Commits received: N".

## 7. Workstation: after "waiting"

- The history record of the delivery shows "Waiting on the Origin"; the commits of the block are marked in the list.
- Each Fetch closes such records: when the tip is reachable from `refs/remotes/rcynq/<branch>` the record becomes
  applied; when a later delivery of the same branch was applied and the tip is still not reachable, the record becomes
  "not applied on the Origin".
- Sending again while one waits is refused by the Origin (6.1.4), with the message above.

## 8. Rolling back commits (Workstation)

- A dialog: the commit that becomes `HEAD` (any unsent commit, or the Origin tip to roll back all of them) and the mode:
  - **Soft**: changes of the dropped commits stay staged;
  - **Mixed**: they stay in the working tree, unstaged;
  - **Hard**: they are discarded.
  The dialog explains each mode; Hard asks for a second confirmation.
- Refused when the working tree has any change relative to `HEAD` (modified, staged, deleted, or a new file that is
  not ignored): "Deal with the changes in the Git working tree first (commit, stash or discard them), then roll back".
  The button stays available; the check runs again on every press.
- Never below the Origin tip: commits the Origin has are not rolled back here.
- Runs under the `WorkingTreeLock` and checks that `HEAD` is still the one the dialog showed; the old `HEAD` stays in
  the reflog.
- After Soft or Mixed the changes appear in "Changes to send", as any local change does.

## 9. History and storage

- The patch record gets the commits it delivered (branch, base, the list with hash, subject, author and date, bundle hash
  and size) as one JSON column `CommitsJson`; a record without it is a patch of files. Migration 0007 adds the column.
- `PatchStatus` gets `Waiting` ("received, waits until the working tree of the receiver allows applying").
- `PatchRejectionReason` gets `CommitsNotAccepted`, `VersionMismatch`, `CommitsWaiting`.
- History shows "N commits" in the contents column of a commits record; "Show diff" opens the list of commits instead
  of a diff.
- Rejections are recorded as for patches: the Workstation records the delivery as failed with the reason of the Origin;
  a delivery the Origin refuses before storing it (toggle off, versions differ, commits waiting) leaves no record there,
  only a line in its log.
- `GET /commits` answers with a state (`UnsentCommitsState`: Ready, NotWorkstation, NoRepository, Detached,
  NoOriginBranch, Diverged, TooMany), so the UI shows the area only when there are local commits.

## 10. Local API and command line

| Local API | What |
|---|---|
| `GET /commits` | Unsent commits and why there are none (Workstation) |
| `POST /commits/send` | Send the block up to a tip (Workstation) |
| `POST /commits/reset` | Roll back to a commit with a mode and the expected `HEAD` (Workstation) |
| `POST /patches/{id}/apply` | Apply a waiting delivery (Origin) |
| `POST /patches/{id}/decline` | Decline a waiting delivery (Origin) |
| `PUT /config/accept-commits` | Switch `AcceptCommits` now and in `syncbridge.json` (Origin); `GET /config` reports it |

Command line, with the services of the node in process like the other commands:

- `rcynq commits list`
- `rcynq commits send [--up-to <hash>]` (all unsent commits by default)
- `rcynq commits reset --to <hash> --mode Soft|Mixed|Hard` (a short name is matched to the full one)
- `rcynq commits apply <id>` and `rcynq commits decline <id>` (Origin)

## 11. Parts

| Part | What |
|---|---|
| `RCynQ.Git` | Listing unsent commits, bundle of a range, unbundle into a quarantine, range check, clean tree check, fast-forward, reset |
| `RCynQ.Core` | Envelope kind and fields, statuses and reasons, API records, `AcceptCommits` |
| `RCynQ.Core.Data*` | Migration and mapping of the new columns |
| `RCynQ.Sync` | Commit delivery sender, receiver and applier (reusing admission, sessions, storage, history), rollback service |
| `RCynQ.Service` | Peer route, local endpoints, events for the banner |
| `RCynQ.UI` | Area of unsent commits, rollback dialog, banner and buttons on the Origin, toggle in settings |
| `RCynQ.Cli` | `commits` commands |

## 12. Security

- A new write capability on the Origin: off by default, switched on by the user on the Origin itself.
- Every fact the Origin acts on (branch, base, tip, list, bundle hash, repository) is signed by the Workstation key;
  the content of the bundle is checked against it before anything leaves the quarantine.
- Every path the commits touch is checked like a path of a patch before anything leaves the quarantine; Git's own
  checkout protections (`.git` paths, NTFS names) apply on the fast-forward.
- Replay protection and the link token as for patches; the peer route accepts only a known Workstation, and each kind
  of delivery only by its own route.
- Every stored delivery, apply and decline is in the history of the Origin; every refusal is in its log.

## 13. Testing

- Unit: selection rule of the list; local checks of the block; admission with the toggle off; version mismatch; one
  waiting delivery; apply and decline of a waiting delivery; reset refused on a dirty tree and on a changed `HEAD`;
  history mapping; envelope canonical payload. Git, files and processes are stubs.
- Integration with real Git in temporary folders: a bundle of a range is unbundled into a quarantine and nothing
  appears in `.git`; a range that does not start at the base is refused; a failed fast-forward leaves `.git` as it was.
- End to end:
  - toggle off: every file under `.git` of the Origin is byte-identical before and after;
  - clean tree: the Origin branch has the same hashes as the Workstation; a Fetch makes the Workstation even with the
    Origin;
  - dirty tree: the delivery waits, `.git` is unchanged; after cleaning, Apply fast-forwards;
  - `HEAD` moved on the Origin: rejected, `.git` unchanged;
  - merge commit in the block arrives with its side commits.
- Migration test of the new columns.

## 14. Documentation and texts

- New texts in all six language files.
- `Docs/<Language>/Architecture.md`: the rule (section 3), the channels (commits as a third kind of delivery), the
  security table; in all six languages.
- `Docs/<Language>/SetupGuide.md`: where the toggle is and what it allows; in all six languages.

## 15. Decisions

Agreed with the owner:

1. Commits are delivered as they are; authors and hashes are never changed.
2. Only to the branch checked out on the Origin, with the same name, when its `HEAD` is the parent of the first
   commit; anything else is refused at once. No rebase, no branch creation.
3. `AcceptCommits` toggle, off by default; a refusal is recorded in patch history.
4. History records that commits were received and how many.
5. Rollback on the Workstation: Soft, Mixed, Hard; refused while the working tree has any change; can be tried again.
6. A changed working tree on the Origin makes the delivery wait; Apply checks everything again; Decline drops it.
7. Merge commits are allowed: one row per first-parent commit, the side commits travel with it.
8. Failed deliveries leave no objects in `.git` (quarantine, removal of moved packs).
9. Command line commands are part of the work.
10. One waiting delivery per repository; a second one is refused while it waits.
11. A waiting delivery does not expire; only Apply or Decline ends it.
12. File patches are refused while commits wait.
13. Rollback never goes below the Origin tip.
14. Limit of 1000 commits per delivery; bundle limited to 500 MB like a file of a patch.
15. An older Origin is detected by the missing peer route (404) and reported as "update RCynQ on the Origin".
