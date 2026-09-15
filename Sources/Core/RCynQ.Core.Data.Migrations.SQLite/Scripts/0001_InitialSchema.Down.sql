-- Reverts RCynQ SQLite schema version 0001 in reverse dependency order.

DROP TABLE IF EXISTS "DiagnosticRuns";
DROP TABLE IF EXISTS "Secrets";
DROP TABLE IF EXISTS "SyncHistory";
DROP TABLE IF EXISTS "PatchEnvelopes";
DROP TABLE IF EXISTS "Snapshots";
DROP TABLE IF EXISTS "ChainLinks";
DROP TABLE IF EXISTS "Peers";
DROP TABLE IF EXISTS "Nodes";
