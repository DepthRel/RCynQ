-- The choices of a part of the changes of files: the version that goes to the other node instead of the working file,
-- the hashes of the base and working files it was chosen on, and the chosen changes (a comma-separated list of their
-- indexes, NULL when the version was edited in an external tool).
CREATE TABLE "PartialChanges" (
    "RepositoryId"  TEXT    NOT NULL,
    "Path"          TEXT    NOT NULL,
    "BaseSha256"    TEXT    NULL,
    "WorkdirSha256" TEXT    NOT NULL,
    "Content"       BLOB    NOT NULL,
    "Blocks"        TEXT    NULL,
    "ChosenAtUtc"   INTEGER NOT NULL,
    CONSTRAINT "PK_PartialChanges" PRIMARY KEY ("RepositoryId", "Path")
);
