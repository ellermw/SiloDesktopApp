import { useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { ArrowLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useAdminLibraries } from "@/hooks/queries/admin/libraries";
import { useAdminCollections } from "@/hooks/queries/admin/collections";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";

import {
  buildAdminCollectionsReturnPath,
  CollectionEditForm,
  CollectionForm,
  MDBListImportForm,
  SourceTypeSelector,
  TMDBPresetForm,
  type CollectionSourceType,
} from "./adminCollectionsShared";

function inferCollectionSourceType(collectionType?: string): CollectionSourceType {
  if (collectionType === "mdblist") return "mdblist";
  if (collectionType === "tmdb") return "tmdb";
  return "manual";
}

export default function AdminCollectionEditor() {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const initialLibraryId = Number(searchParams.get("libraryId")) || null;
  const returnPath = buildAdminCollectionsReturnPath(initialLibraryId);
  const isCreate = !id;
  const { data: libraries = [] } = useAdminLibraries();
  const { data: collections = [], isLoading } = useAdminCollections();
  const collection = useMemo(
    () => collections.find((entry) => entry.id === id) ?? null,
    [collections, id],
  );
  const [sourceType, setSourceType] = useState<CollectionSourceType | null>(null);

  const activeSourceType = collection
    ? inferCollectionSourceType(collection.collection_type)
    : sourceType;

  const title = collection
    ? `Edit ${collection.title}`
    : activeSourceType === null
      ? "Add Collection"
      : activeSourceType === "manual"
        ? "New Manual Collection"
        : activeSourceType === "mdblist"
          ? "Import MDBList Collection"
          : "Import TMDB Collection";

  const description = collection
    ? "Collections now open in a dedicated workspace so rules, artwork, and preview can stay visible."
    : activeSourceType === null
      ? "Choose how this collection should be created."
      : "Build the collection in a full-page editor instead of a cramped dialog.";

  useDocumentTitle(title);

  if (isLoading && libraries.length === 0) {
    return <div className="page-shell py-8">Loading collection editor...</div>;
  }

  if (!isCreate && !collection && !isLoading) {
    return (
      <div className="page-shell space-y-4 py-4 sm:py-6">
        <Button asChild variant="ghost" className="w-fit px-0">
          <Link to={returnPath}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to Collections
          </Link>
        </Button>
        <Card className="surface-panel rounded-[1.7rem] border-0 shadow-none">
          <CardHeader>
            <CardTitle>Collection not found</CardTitle>
            <CardDescription>The selected collection could not be loaded.</CardDescription>
          </CardHeader>
        </Card>
      </div>
    );
  }

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <div className="page-header gap-5">
        <div className="space-y-3">
          <Button asChild variant="ghost" className="w-fit px-0">
            <Link to={returnPath}>
              <ArrowLeft className="mr-2 h-4 w-4" />
              Back to Collections
            </Link>
          </Button>
          <div>
            <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">{title}</h1>
            <p className="page-subtitle mt-1 text-sm sm:text-base">{description}</p>
          </div>
        </div>

        {!collection && activeSourceType !== null ? (
          <Button variant="outline" onClick={() => setSourceType(null)}>
            Change Source Type
          </Button>
        ) : null}
      </div>

      {!collection && activeSourceType === null ? (
        <Card className="surface-panel rounded-[1.7rem] border-0 shadow-none">
          <CardHeader>
            <CardTitle>Choose a Collection Type</CardTitle>
            <CardDescription>
              Smart/manual collections open the full query builder. Imports keep their
              source-specific setup.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <SourceTypeSelector onSelect={setSourceType} />
          </CardContent>
        </Card>
      ) : null}

      {collection ? (
        <CollectionEditForm
          libraries={libraries}
          collection={collection}
          initialLibraryId={initialLibraryId}
          onClose={() => navigate(returnPath)}
        />
      ) : null}

      {!collection && activeSourceType === "manual" ? (
        <CollectionForm
          libraries={libraries}
          collection={null}
          initialLibraryId={initialLibraryId}
          onClose={() => navigate(returnPath)}
        />
      ) : null}

      {!collection && activeSourceType === "mdblist" ? (
        <MDBListImportForm
          libraries={libraries}
          initialLibraryId={initialLibraryId}
          onClose={() => navigate(returnPath)}
        />
      ) : null}

      {!collection && activeSourceType === "tmdb" ? (
        <TMDBPresetForm
          libraries={libraries}
          initialLibraryId={initialLibraryId}
          onClose={() => navigate(returnPath)}
        />
      ) : null}
    </div>
  );
}
