import { useEffect, useState } from "react";
import type { Collection, CreateCollectionRequest, UpdateCollectionRequest } from "@/api/types";
import { useProfiles } from "@/hooks/queries/profiles";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import {
  useCreateCollection,
  useUpdateCollection,
} from "@/hooks/queries/collections";
import { buildUserCollectionCatalogHref as buildCatalogHrefForUserCollection } from "@/pages/catalogSearchParams";
import CollectionBuilder, {
  createCollectionBuilderValue,
  type CollectionBuilderValue,
} from "@/components/collections/CollectionBuilder";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export function buildUserCollectionEditorPath(id: "new" | string) {
  return id === "new" ? "/collections/new" : `/collections/${id}/edit`;
}

export function buildUserCollectionCatalogHref(id: string, title?: string) {
  return buildCatalogHrefForUserCollection(id, title);
}

export function toUserCollectionBuilderValue(
  collection: Collection | null,
): CollectionBuilderValue {
  return createCollectionBuilderValue({
    title: collection?.name ?? "",
    collection_type: collection?.collection_type ?? "smart",
    query_definition: collection?.query_definition,
    sort_config: collection?.sort_config ?? {},
    access: {
      is_shared: collection?.is_shared ?? false,
      allowed_profile_ids: collection?.allowed_profile_ids ?? [],
    },
  });
}

export function toCreateCollectionBody(value: CollectionBuilderValue): CreateCollectionRequest {
  return {
    name: value.title,
    collection_type: value.collection_type,
    is_shared: value.access.is_shared,
    allowed_profile_ids: value.access.allowed_profile_ids,
    query_definition: value.collection_type === "smart" ? value.query_definition : undefined,
    sort_config: value.collection_type === "smart" ? value.sort_config : undefined,
  };
}

export function toUpdateCollectionBody(value: CollectionBuilderValue): UpdateCollectionRequest {
  return {
    name: value.title,
    is_shared: value.access.is_shared,
    allowed_profile_ids: value.access.allowed_profile_ids,
    query_definition: value.collection_type === "smart" ? value.query_definition : undefined,
    sort_config: value.collection_type === "smart" ? value.sort_config : undefined,
  };
}

export function isCollectionReadOnly(
  collection: Collection | null,
  currentProfileId?: string | null,
): boolean {
  if (!collection || !currentProfileId) {
    return false;
  }
  return collection.creator_profile_id !== currentProfileId;
}

function UserCollectionSummary({
  draft,
  collection,
}: {
  draft: CollectionBuilderValue;
  collection: Collection | null;
}) {
  const profileSummary =
    draft.access.allowed_profile_ids.length > 0
      ? `${draft.access.allowed_profile_ids.length} selected`
      : "All profiles";

  return (
    <Card className="surface-panel gap-0 rounded-[1.5rem] border-0 shadow-none">
      <CardHeader>
        <CardTitle>Collection Summary</CardTitle>
        <CardDescription>Preview and sharing stay visible while you edit.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <SummaryRow label="Mode" value={draft.collection_type === "smart" ? "Smart" : "Manual"} />
        <SummaryRow label="Shared" value={draft.access.is_shared ? "Yes" : "No"} />
        <SummaryRow label="Profiles" value={profileSummary} />
        {collection ? <SummaryRow label="Collection" value={collection.name} /> : null}
      </CardContent>
    </Card>
  );
}

function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-4 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right font-medium">{value}</span>
    </div>
  );
}

export function UserCollectionForm({
  collection,
  onClose,
}: {
  collection: Collection | null;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState(() => toUserCollectionBuilderValue(collection));
  const createMutation = useCreateCollection();
  const updateMutation = useUpdateCollection();
  const { data: profiles = [] } = useProfiles();
  const { profile } = useCurrentProfile();
  const isPending = createMutation.isPending || updateMutation.isPending;
  const readOnly = isCollectionReadOnly(collection, profile?.id);

  useEffect(() => {
    setDraft(toUserCollectionBuilderValue(collection));
  }, [collection]);

  function handleSubmit() {
    if (collection) {
      updateMutation.mutate(
        { id: collection.id, body: toUpdateCollectionBody(draft) },
        { onSuccess: onClose },
      );
    } else {
      createMutation.mutate(toCreateCollectionBody(draft), { onSuccess: onClose });
    }
  }

  return (
    <CollectionBuilder
      mode="user"
      value={draft}
      onChange={setDraft}
      onSubmit={handleSubmit}
      submitLabel="Save Collection"
      profiles={profiles.map((entry) => ({ id: entry.id, name: entry.name }))}
      allowAccessControls
      allowLibrarySelection={false}
      isPending={isPending}
      readOnly={readOnly}
      creatorProfileId={collection?.creator_profile_id ?? null}
      previewLayout="sidebar"
      sidebarContent={<UserCollectionSummary draft={draft} collection={collection} />}
    />
  );
}
