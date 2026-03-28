import { useState } from "react";
import { useNavigate } from "react-router";
import { Pencil, Plus, Trash2 } from "lucide-react";

import type { Collection } from "@/api/types";
import { useCollections, useDeleteCollection } from "@/hooks/queries/collections";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardHeader, CardTitle } from "@/components/ui/card";

import {
  buildUserCollectionCatalogHref,
  buildUserCollectionEditorPath,
} from "./userCollectionsShared";

export default function Collections() {
  return <CollectionList />;
}

function CollectionList() {
  const { data: collections = [], isLoading } = useCollections();
  const [confirmDeleteCollection, setConfirmDeleteCollection] = useState<Collection | null>(null);
  const navigate = useNavigate();
  const deleteMutation = useDeleteCollection();

  useDocumentTitle("Collections");

  if (isLoading) return <div className="page-shell py-8">Loading collections...</div>;

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <ConfirmDialog
        open={confirmDeleteCollection !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteCollection(null);
        }}
        title="Delete collection"
        description={`Delete collection "${confirmDeleteCollection?.name}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteCollection) deleteMutation.mutate(confirmDeleteCollection.id);
          setConfirmDeleteCollection(null);
        }}
      />

      <div className="page-header">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,5vw,3.25rem)]">Collections</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Build personal or shared shelves around moods, series arcs, or anything else worth grouping.
          </p>
        </div>
        <Button size="sm" onClick={() => navigate(buildUserCollectionEditorPath("new"))}>
          <Plus className="mr-1 h-4 w-4" /> New Collection
        </Button>
      </div>

      {collections.length === 0 ? (
        <div className="surface-panel text-muted-foreground rounded-[2rem] py-16 text-center">
          No collections yet. Create one to get started.
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {collections.map((collection) => (
            <Card
              key={collection.id}
              className="surface-panel hover:border-primary group cursor-pointer rounded-[1.6rem] border-0 transition-all hover:-translate-y-1"
              onClick={() => navigate(buildUserCollectionCatalogHref(collection.id, collection.name))}
            >
              <CardHeader className="flex-row items-center justify-between space-y-0">
                <div className="space-y-2">
                  <CardTitle className="text-base">{collection.name}</CardTitle>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="secondary">{collection.collection_type}</Badge>
                    {collection.is_shared ? <Badge variant="outline">Shared</Badge> : null}
                  </div>
                </div>
                <div className="flex gap-1 opacity-0 group-hover:opacity-100">
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    onClick={(event) => {
                      event.stopPropagation();
                      navigate(buildUserCollectionEditorPath(collection.id));
                    }}
                  >
                    <Pencil className="h-3 w-3" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="h-7 w-7"
                    onClick={(event) => {
                      event.stopPropagation();
                      setConfirmDeleteCollection(collection);
                    }}
                  >
                    <Trash2 className="h-3 w-3" />
                  </Button>
                </div>
              </CardHeader>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
