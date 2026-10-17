-- The tickets of the invitations this node created: the hash of the secret, never the secret, and the state.
CREATE TABLE "EnrollmentTickets" (
    "Id"            TEXT    NOT NULL CONSTRAINT "PK_EnrollmentTickets" PRIMARY KEY,
    "Role"          INTEGER NOT NULL,
    "SecretHash"    BLOB    NOT NULL,
    "CreatedAtUtc"  INTEGER NOT NULL,
    "ExpiresAtUtc"  INTEGER NOT NULL,
    "EnrollmentKey" TEXT    NULL,
    "UsedAtUtc"     INTEGER NULL,
    "UsedBy"        TEXT    NULL,
    "RevokedAtUtc"  INTEGER NULL
);
