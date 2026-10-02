-- The name of the repository a patch belongs to. NULL for patches exchanged before the node knew the name of its
-- repository: they belong to the repository the node started with.
ALTER TABLE "PatchEnvelopes" ADD COLUMN "Repository" TEXT NULL;
