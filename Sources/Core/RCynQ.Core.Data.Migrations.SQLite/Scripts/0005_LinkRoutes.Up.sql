-- The addresses the peer of a link is reachable at, as a JSON array; '[]' when it has only its own address.
ALTER TABLE "ChainLinks" ADD COLUMN "RoutesJson" TEXT NOT NULL DEFAULT '[]';

-- The route chosen by hand; NULL when the node chooses the route itself.
ALTER TABLE "ChainLinks" ADD COLUMN "PinnedRouteId" TEXT NULL;
