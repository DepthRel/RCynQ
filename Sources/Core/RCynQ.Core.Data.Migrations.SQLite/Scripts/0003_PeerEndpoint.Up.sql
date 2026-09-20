-- How the peer accepts connections: 'SshTunnel' (its SSH server) or 'Direct' (its own TLS port).
ALTER TABLE "Peers" ADD COLUMN "EndpointMode" TEXT NOT NULL DEFAULT 'SshTunnel';

-- The SHA-256 fingerprint of the peer's TLS certificate, pinned when it accepts connections directly.
ALTER TABLE "Peers" ADD COLUMN "TlsFingerprint" TEXT NULL;

-- The TCP port of the peer's TLS endpoint; 0 when it uses its SSH server.
ALTER TABLE "Peers" ADD COLUMN "EndpointPort" INTEGER NOT NULL DEFAULT 0;
