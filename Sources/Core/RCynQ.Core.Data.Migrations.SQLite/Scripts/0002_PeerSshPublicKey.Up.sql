-- The OpenSSH public key line of a peer, taken from its exported configuration.
ALTER TABLE "Peers" ADD COLUMN "SshPublicKey" TEXT NULL;
