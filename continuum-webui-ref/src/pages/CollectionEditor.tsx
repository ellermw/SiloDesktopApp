import { Link, useNavigate, useParams } from "react-router";
import { ArrowLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardHeader, CardDescription, CardTitle } from "@/components/ui/card";
import { useCollections } from "@/hooks/queries/collections";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";

import { UserCollectionForm } from "./userCollectionsShared";

export default function CollectionEditor() {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const { data: collections = [], isLoading } = useCollections();
  const collection = id ? collections.find((entry) => entry.id === id) ?? null : null;

  useDocumentTitle(collection ? `Edit ${collection.name}` : id ? "Edit Collection" : "New Collection");

  if (isLoading && id) {
    return <div className="page-shell py-8">Loading collection editor...</div>;
  }

  if (id && !collection && !isLoading) {
    return (
      <div className="page-shell space-y-4 py-4 sm:py-6">
        <Button asChild variant="ghost" className="w-fit px-0">
          <Link to="/collections">
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
          <Link to="/collections">
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to Collections
          </Link>
        </Button>
        <div>
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">
            {collection ? `Edit ${collection.name}` : "New Collection"}
          </h1>
          <p className="page-subtitle mt-1 text-sm sm:text-base">
            Build collections in a full-page editor so sharing, rules, and preview stay visible.
          </p>
        </div>
        </div>
      </div>

      <UserCollectionForm
        collection={collection}
        onClose={() => navigate("/collections")}
      />
    </div>
  );
}
