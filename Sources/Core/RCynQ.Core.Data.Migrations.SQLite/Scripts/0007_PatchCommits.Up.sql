-- The commits a patch delivers instead of files (a JSON object), NULL for a patch of files.
ALTER TABLE "PatchEnvelopes" ADD COLUMN "CommitsJson" TEXT NULL;
