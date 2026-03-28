import { useEffect, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import type { LibraryCollection } from "@/api/types";
import { useAdminLibraries } from "@/hooks/queries/admin/libraries";
import {
  useAdminCollections,
  useDeleteAdminCollection,
  useSyncAdminCollection,
} from "@/hooks/queries/admin/collections";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { Pencil, Plus, RefreshCw, Trash2 } from "lucide-react";

import {
  buildAdminCollectionEditorPath,
  LibraryPicker,
} from "./adminCollectionsShared";

export default function AdminCollections() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: libraries = [] } = useAdminLibraries();
  const requestedLibraryId = Number(searchParams.get("libraryId"));
  const initialLibraryId =
    Number.isFinite(requestedLibraryId) && requestedLibraryId > 0 ? requestedLibraryId : null;
  const [selectedLibraryId, setSelectedLibraryId] = useState<number | null>(initialLibraryId);
  const [confirmDeleteCollection, setConfirmDeleteCollection] = useState<LibraryCollection | null>(
    null,
  );
  const effectiveLibraryId = selectedLibraryId ?? libraries[0]?.id ?? null;

  useEffect(() => {
    if (!effectiveLibraryId) {
      return;
    }
    setSearchParams((current) => {
      const next = new URLSearchParams(current);
      next.set("libraryId", String(effectiveLibraryId));
      return next;
    });
  }, [effectiveLibraryId, setSearchParams]);

  const { data: collections = [], isLoading } = useAdminCollections(effectiveLibraryId ?? undefined);
  const deleteMutation = useDeleteAdminCollection();
  const syncMutation = useSyncAdminCollection();

  if (isLoading && effectiveLibraryId === null) {
    return <div className="p-8">Loading collections...</div>;
  }

  return (
    <div className="space-y-6">
      <ConfirmDialog
        open={confirmDeleteCollection !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteCollection(null);
        }}
        title="Delete collection"
        description={`Delete collection "${confirmDeleteCollection?.title}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteCollection) {
            deleteMutation.mutate({
              id: confirmDeleteCollection.id,
              libraryId: confirmDeleteCollection.library_id,
            });
          }
          setConfirmDeleteCollection(null);
        }}
      />

      <div className="page-header gap-5">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Collections</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Curate library shelves and sync them from MDBList or TMDB trending.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <LibraryPicker
            libraries={libraries}
            value={effectiveLibraryId}
            onChange={setSelectedLibraryId}
          />
          <Button
            size="sm"
            onClick={() => navigate(buildAdminCollectionEditorPath("new", effectiveLibraryId))}
          >
            <Plus className="mr-1 h-4 w-4" /> Add Collection
          </Button>
        </div>
      </div>

      <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
        <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Title</TableHead>
            <TableHead>Source</TableHead>
            <TableHead>Items</TableHead>
            <TableHead>Sync</TableHead>
            <TableHead>Updated</TableHead>
            <TableHead className="w-32">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {collections.map((collection) => {
            const isSyncing =
              syncMutation.isPending && syncMutation.variables?.id === collection.id;

            return (
              <TableRow key={collection.id}>
                <TableCell className="max-w-[360px]">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-medium">{collection.title}</span>
                      {collection.featured ? <Badge>Featured</Badge> : null}
                      <Badge variant="secondary">{collection.visibility}</Badge>
                    </div>
                    <div className="text-muted-foreground line-clamp-2 text-xs">
                      {collection.description || "No summary provided."}
                    </div>
                  </div>
                </TableCell>
                <TableCell>
                  <div className="space-y-1 text-xs">
                    <Badge variant="outline">{collection.collection_type}</Badge>
                    {collection.source_url ? (
                      <div className="text-muted-foreground max-w-[220px] truncate">
                        {collection.source_url}
                      </div>
                    ) : (
                      <div className="text-muted-foreground">Local metadata</div>
                    )}
                  </div>
                </TableCell>
                <TableCell>{collection.collection_type === "smart" ? "\u2014" : collection.item_count}</TableCell>
                <TableCell>
                  <div className="space-y-1">
                    <Badge variant="outline">{collection.last_sync_status}</Badge>
                    <div className="text-muted-foreground max-w-[220px] truncate text-xs">
                      {collection.last_sync_message || "Not synced yet"}
                    </div>
                  </div>
                </TableCell>
                <TableCell className="text-muted-foreground text-xs">
                  {new Date(collection.updated_at).toLocaleString()}
                </TableCell>
                <TableCell>
                  <div className="flex gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      title="Sync collection"
                      disabled={isSyncing}
                      onClick={() =>
                        syncMutation.mutate({
                          id: collection.id,
                          libraryId: collection.library_id,
                        })
                      }
                    >
                      <RefreshCw className={`h-3 w-3 ${isSyncing ? "animate-spin" : ""}`} />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() =>
                        navigate(
                          buildAdminCollectionEditorPath(collection.id, collection.library_id),
                        )
                      }
                    >
                      <Pencil className="h-3 w-3" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() => setConfirmDeleteCollection(collection)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            );
          })}

          {!isLoading && collections.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className="text-muted-foreground py-10 text-center">
                No collections found for this library yet.
              </TableCell>
            </TableRow>
          ) : null}
        </TableBody>
        </Table>
      </div>
    </div>
  );
}
