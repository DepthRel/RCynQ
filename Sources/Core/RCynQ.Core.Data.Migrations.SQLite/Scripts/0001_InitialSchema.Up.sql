-- RCynQ SQLite schema, version 0001.
-- Conventions shared with the EF Core model (RCynQ.Core.Data):
--   * GUIDs are TEXT, enums are INTEGER;
--   * DateTimeOffset values are INTEGER UTC ticks;
--   * patch payloads are stored as files, only metadata lives here.

CREATE TABLE "Nodes" (
    "Id"                TEXT    NOT NULL CONSTRAINT "PK_Nodes" PRIMARY KEY,
    "Name"              TEXT    NOT NULL,
    "Role"              INTEGER NOT NULL,
    "RepoPath"          TEXT    NULL,
    "WorkdirPath"       TEXT    NULL,
    "LocalStateHash"    TEXT    NULL,
    "SyncBaseHash"      TEXT    NULL,
    "LastSentAtUtc"     INTEGER NULL,
    "LastReceivedAtUtc" INTEGER NULL,
    "UpdatedAtUtc"      INTEGER NOT NULL
);

CREATE TABLE "Peers" (
    "Id"                 TEXT    NOT NULL CONSTRAINT "PK_Peers" PRIMARY KEY,
    "Name"               TEXT    NOT NULL,
    "Role"               INTEGER NOT NULL,
    "Host"               TEXT    NOT NULL,
    "SshPort"            INTEGER NOT NULL DEFAULT 0,
    "SshUser"            TEXT    NOT NULL,
    "HostKeyFingerprint" TEXT    NULL,
    "SigningPublicKey"   TEXT    NULL,
    "GitRepoPath"        TEXT    NULL,
    "UpdatedAtUtc"       INTEGER NOT NULL
);

CREATE TABLE "ChainLinks" (
    "Id"                     TEXT    NOT NULL CONSTRAINT "PK_ChainLinks" PRIMARY KEY,
    "Name"                   TEXT    NOT NULL,
    "Transport"              INTEGER NOT NULL,
    "FallbackTransportsJson" TEXT    NOT NULL,
    "TransportSettingsJson"  TEXT    NOT NULL,
    "PeerId"                 TEXT    NOT NULL,
    "LocalPort"              INTEGER NOT NULL DEFAULT 0,
    "RemotePort"             INTEGER NOT NULL DEFAULT 0,
    "Priority"               INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT "FK_ChainLinks_Peers" FOREIGN KEY ("PeerId") REFERENCES "Peers" ("Id")
);

CREATE UNIQUE INDEX "IX_ChainLinks_Name" ON "ChainLinks" ("Name");
CREATE INDEX "IX_ChainLinks_PeerId" ON "ChainLinks" ("PeerId");

CREATE TABLE "Snapshots" (
    "Id"           TEXT    NOT NULL CONSTRAINT "PK_Snapshots" PRIMARY KEY,
    "NodeId"       TEXT    NOT NULL,
    "CreatedAtUtc" INTEGER NOT NULL,
    "StateHash"    TEXT    NOT NULL,
    "Label"        TEXT    NULL
);

CREATE INDEX "IX_Snapshots_NodeId" ON "Snapshots" ("NodeId");

CREATE TABLE "PatchEnvelopes" (
    "Id"                TEXT    NOT NULL CONSTRAINT "PK_PatchEnvelopes" PRIMARY KEY,
    "Direction"         INTEGER NOT NULL,
    "SourceNode"        TEXT    NOT NULL,
    "TargetNode"        TEXT    NOT NULL,
    "CreatedAtUtc"      INTEGER NOT NULL,
    "BaseStateHash"     TEXT    NOT NULL,
    "IncomingStateHash" TEXT    NOT NULL,
    "Status"            INTEGER NOT NULL,
    "AppliedAtUtc"      INTEGER NULL,
    "ManifestJson"      TEXT    NOT NULL,
    "ConflictsJson"     TEXT    NOT NULL,
    "Error"             TEXT    NULL,
    "Signature"         BLOB    NULL
);

CREATE INDEX "IX_PatchEnvelopes_CreatedAtUtc" ON "PatchEnvelopes" ("CreatedAtUtc");

CREATE TABLE "SyncHistory" (
    "Id"               TEXT    NOT NULL CONSTRAINT "PK_SyncHistory" PRIMARY KEY,
    "TimestampUtc"     INTEGER NOT NULL,
    "Direction"        INTEGER NOT NULL,
    "SourceNode"       TEXT    NOT NULL,
    "TargetNode"       TEXT    NOT NULL,
    "PatchId"          TEXT    NOT NULL,
    "Status"           INTEGER NOT NULL,
    "AppliedFileCount" INTEGER NOT NULL DEFAULT 0,
    "ConflictCount"    INTEGER NOT NULL DEFAULT 0,
    "Details"          TEXT    NULL
);

CREATE INDEX "IX_SyncHistory_TimestampUtc" ON "SyncHistory" ("TimestampUtc");

CREATE TABLE "Secrets" (
    "Name"         TEXT    NOT NULL CONSTRAINT "PK_Secrets" PRIMARY KEY,
    "Nonce"        BLOB    NOT NULL,
    "CipherText"   BLOB    NOT NULL,
    "Tag"          BLOB    NOT NULL,
    "UpdatedAtUtc" INTEGER NOT NULL
);

CREATE TABLE "DiagnosticRuns" (
    "Id"           TEXT    NOT NULL CONSTRAINT "PK_DiagnosticRuns" PRIMARY KEY,
    "TimestampUtc" INTEGER NOT NULL,
    "Role"         INTEGER NOT NULL,
    "AllPassed"    INTEGER NOT NULL,
    "ResultsJson"  TEXT    NOT NULL
);

CREATE INDEX "IX_DiagnosticRuns_TimestampUtc" ON "DiagnosticRuns" ("TimestampUtc");
