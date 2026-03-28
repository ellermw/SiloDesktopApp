import { useEffect, useMemo, useState } from "react";
import type { FormEvent } from "react";
import { SECTION_TYPES, FILTER_SECTION_TYPES, sectionTypeLabel } from "@/lib/sectionTypes";
import {
  queryDefinitionFromSectionConfig,
  queryDefinitionToSectionConfig,
  type PageSectionConfig,
} from "@/api/types";
import type { Library } from "@/api/types";
import {
  useAdminSections,
  useCreateSection,
  useUpdateSection,
  useDeleteSection,
  useReorderSections,
  useRestoreDefaultSections,
} from "@/hooks/queries/sections";
import { useAdminCollections } from "@/hooks/queries/admin/collections";
import { useAdminLibraries } from "@/hooks/queries/admin/libraries";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import CollectionRulesEditor from "@/components/collections/CollectionRulesEditor";
import LibraryMultiSelect from "@/components/LibraryMultiSelect";
import { Plus, Pencil, Trash2, Star, GripVertical, RotateCcw } from "lucide-react";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { toast } from "sonner";
import { buildSectionReorderEntries } from "./adminSectionOrder";
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  KeyboardSensor,
  closestCenter,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import type { DragStartEvent, DragEndEvent } from "@dnd-kit/core";
import {
  SortableContext,
  verticalListSortingStrategy,
  useSortable,
  arrayMove,
} from "@dnd-kit/sortable";
import { sortableKeyboardCoordinates } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";

function getSectionCollectionId(section: PageSectionConfig | null): string {
  const value = section?.config?.library_collection_id;
  return typeof value === "string" ? value : "";
}

function getSectionFilterLibraryIds(section: PageSectionConfig | null): number[] {
  return queryDefinitionFromSectionConfig(section?.config).library_ids;
}

function getSectionMediaScope(section: PageSectionConfig | null): string | undefined {
  return queryDefinitionFromSectionConfig(section?.config).media_scope;
}

function formatCollectionOptionLabel(
  collection: { title: string; library_id: number },
  libraries: Library[],
): string {
  const library = libraries.find((entry) => entry.id === collection.library_id);
  return library ? `${collection.title} (${library.name})` : collection.title;
}

function LibraryPicker({
  libraries,
  value,
  onChange,
}: {
  libraries: Library[];
  value: number | null;
  onChange: (libraryId: number) => void;
}) {
  return (
    <Select
      value={value ? String(value) : undefined}
      onValueChange={(next) => onChange(Number(next))}
    >
      <SelectTrigger className="w-[220px]">
        <SelectValue placeholder="Choose library" />
      </SelectTrigger>
      <SelectContent>
        {libraries.map((library) => (
          <SelectItem key={library.id} value={String(library.id)}>
            {library.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

interface SortableRowProps {
  section: PageSectionConfig;
  canReorder: boolean;
  libraries: Library[];
  collectionLabels: Map<string, string>;
  onEdit: (section: PageSectionConfig) => void;
  onDelete: (section: PageSectionConfig) => void;
}

function SortableRow({
  section,
  canReorder,
  libraries,
  collectionLabels,
  onEdit,
  onDelete,
}: SortableRowProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: section.id,
    disabled: !canReorder,
  });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
  };

  return (
    <TableRow ref={setNodeRef} style={style}>
      <TableCell>
        {canReorder ? (
          <button type="button" className="cursor-grab touch-none" {...attributes} {...listeners}>
            <GripVertical className="text-muted-foreground h-4 w-4" />
          </button>
        ) : (
          <GripVertical className="text-muted-foreground h-4 w-4" />
        )}
      </TableCell>
      <TableCell className="font-medium">{section.title}</TableCell>
      <TableCell>
        <div className="flex flex-wrap gap-1">
          <Badge variant="secondary">{sectionTypeLabel(section.section_type)}</Badge>
          {getSectionMediaScope(section) === "movie" && <Badge variant="outline">Movies</Badge>}
          {getSectionMediaScope(section) === "series" && <Badge variant="outline">Series</Badge>}
          {getSectionFilterLibraryIds(section).map((libraryId) => {
            const lib = libraries.find((entry) => entry.id === libraryId);
            return lib ? (
              <Badge key={libraryId} variant="outline">
                {lib.name}
              </Badge>
            ) : null;
          })}
          {section.section_type === "collection" &&
            typeof section.config?.library_collection_id === "string" &&
            (() => {
              const label = collectionLabels.get(section.config.library_collection_id);
              return label ? <Badge variant="outline">{label}</Badge> : null;
            })()}
        </div>
      </TableCell>
      <TableCell>{section.item_limit}</TableCell>
      <TableCell>
        {section.featured && <Star className="h-4 w-4 fill-yellow-500 text-yellow-500" />}
      </TableCell>
      <TableCell>
        <Badge variant={section.enabled ? "default" : "secondary"}>
          {section.enabled ? "On" : "Off"}
        </Badge>
      </TableCell>
      <TableCell>
        <div className="flex gap-1">
          <Button variant="ghost" size="sm" className="h-7 w-7 p-0" onClick={() => onEdit(section)}>
            <Pencil className="h-3.5 w-3.5" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-destructive h-7 w-7 p-0"
            onClick={() => onDelete(section)}
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      </TableCell>
    </TableRow>
  );
}

function DragOverlayRow({ section }: { section: PageSectionConfig }) {
  return (
    <div className="surface-panel flex items-center gap-2 rounded-xl border-0 px-3 py-2 shadow-lg">
      <GripVertical className="text-muted-foreground h-4 w-4" />
      <span className="font-medium">{section.title}</span>
      <Badge variant="secondary" className="ml-2">
        {sectionTypeLabel(section.section_type)}
      </Badge>
    </div>
  );
}

export default function AdminSections() {
  const [scope, setScope] = useState("home");
  const { data: librariesData } = useAdminLibraries();
  const librariesList = useMemo(() => librariesData ?? [], [librariesData]);
  const [selectedLibraryId, setSelectedLibraryId] = useState<number | null>(null);
  const activeLibraryId = scope === "library" ? selectedLibraryId : null;
  const { data, isLoading } = useAdminSections(scope, activeLibraryId ?? undefined);
  const { data: collectionsData = [] } = useAdminCollections();
  const collectionLabels = new Map(
    collectionsData.map((collection) => [
      collection.id,
      formatCollectionOptionLabel(collection, librariesList),
    ]),
  );
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingSection, setEditingSection] = useState<PageSectionConfig | null>(null);
  const [orderedSections, setOrderedSections] = useState<PageSectionConfig[]>([]);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [confirmDeleteSection, setConfirmDeleteSection] = useState<PageSectionConfig | null>(null);
  const deleteMutation = useDeleteSection();
  const reorderMutation = useReorderSections();
  const restoreDefaultsMutation = useRestoreDefaultSections();
  const [confirmRestoreOpen, setConfirmRestoreOpen] = useState(false);
  const [resetProfiles, setResetProfiles] = useState(false);

  const sections = useMemo(() => data?.sections ?? [], [data?.sections]);
  const isHomeScope = scope === "home";
  const canManageLibrarySections = librariesList.length > 0 && selectedLibraryId !== null;
  const canDrag = !reorderMutation.isPending && (isHomeScope || canManageLibrarySections);

  useEffect(() => {
    if (librariesList.length === 0) {
      setSelectedLibraryId(null);
      return;
    }

    setSelectedLibraryId((current) => {
      if (current && librariesList.some((library) => library.id === current)) {
        return current;
      }
      return librariesList[0]?.id ?? null;
    });
  }, [librariesList]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  useEffect(() => {
    setOrderedSections(sections);
  }, [sections]);

  function handleDelete(section: PageSectionConfig) {
    setConfirmDeleteSection(section);
  }

  function handleEdit(section: PageSectionConfig) {
    setEditingSection(section);
    setDialogOpen(true);
  }

  function handleDragStart(event: DragStartEvent) {
    setActiveId(event.active.id as string);
  }

  function handleDragEnd(event: DragEndEvent) {
    setActiveId(null);

    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const oldIndex = orderedSections.findIndex((s) => s.id === active.id);
    const newIndex = orderedSections.findIndex((s) => s.id === over.id);
    if (oldIndex === -1 || newIndex === -1) return;

    const nextSections = arrayMove(orderedSections, oldIndex, newIndex);
    setOrderedSections(nextSections);
    reorderMutation.mutate(buildSectionReorderEntries(nextSections), {
      onError: () => {
        setOrderedSections(sections);
      },
    });
  }

  function handleDragCancel() {
    setActiveId(null);
  }

  const activeSection = activeId ? (orderedSections.find((s) => s.id === activeId) ?? null) : null;

  if (isLoading) return <div className="p-4">Loading sections...</div>;

  return (
    <div className="space-y-6">
      <ConfirmDialog
        open={confirmDeleteSection !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteSection(null);
        }}
        title="Delete section"
        description={`Delete section "${confirmDeleteSection?.title}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteSection) deleteMutation.mutate(confirmDeleteSection.id);
          setConfirmDeleteSection(null);
        }}
      />
      <Dialog
        open={confirmRestoreOpen}
        onOpenChange={(open) => {
          setConfirmRestoreOpen(open);
          if (!open) setResetProfiles(false);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Restore Default Sections</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <p className="text-muted-foreground text-sm">
              This will replace all {scope === "home" ? "home" : "library"} sections with the
              defaults. Any custom sections will be removed.
            </p>
            <div className="flex items-center gap-2">
              <Switch
                id="resetProfiles"
                size="sm"
                checked={resetProfiles}
                onCheckedChange={(checked) => setResetProfiles(checked === true)}
              />
              <Label htmlFor="resetProfiles" className="text-sm font-normal">
                Also reset all user customizations for this scope
              </Label>
            </div>
            <div className="flex justify-end gap-2">
              <Button
                variant="outline"
                onClick={() => {
                  setConfirmRestoreOpen(false);
                  setResetProfiles(false);
                }}
              >
                Cancel
              </Button>
              <Button
                variant="destructive"
                disabled={restoreDefaultsMutation.isPending}
                onClick={() => {
                  restoreDefaultsMutation.mutate(
                    {
                      scope,
                      ...(activeLibraryId != null ? { library_id: activeLibraryId } : {}),
                      reset_profiles: resetProfiles,
                    },
                    {
                      onSuccess: () => {
                        toast.success("Sections restored to defaults");
                        setConfirmRestoreOpen(false);
                        setResetProfiles(false);
                      },
                      onError: () => {
                        toast.error("Failed to restore defaults");
                      },
                    },
                  );
                }}
              >
                Restore Defaults
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
      <div className="page-header gap-5">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Sections</h1>
        </div>
        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={
              (!isHomeScope && !canManageLibrarySections) || restoreDefaultsMutation.isPending
            }
            onClick={() => setConfirmRestoreOpen(true)}
          >
            <RotateCcw className="mr-1 h-4 w-4" /> Restore Defaults
          </Button>
          <Dialog
            open={dialogOpen}
            onOpenChange={(open) => {
              setDialogOpen(open);
              if (!open) setEditingSection(null);
            }}
          >
            <DialogTrigger asChild>
              <Button size="sm" disabled={!isHomeScope && !canManageLibrarySections}>
                <Plus className="mr-1 h-4 w-4" /> Add Section
              </Button>
            </DialogTrigger>
            <DialogContent className="max-h-[85vh] max-w-xl overflow-y-auto">
              <DialogHeader>
                <DialogTitle>{editingSection ? "Edit Section" : "Add Section"}</DialogTitle>
              </DialogHeader>
              <SectionForm
                section={editingSection}
                scope={scope}
                currentLibraryId={activeLibraryId}
                libraries={librariesList}
                collections={collectionsData}
                onClose={() => {
                  setDialogOpen(false);
                  setEditingSection(null);
                }}
              />
            </DialogContent>
          </Dialog>
        </div>
      </div>

      <Tabs value={scope} onValueChange={setScope}>
        <TabsList>
          <TabsTrigger value="home">Home</TabsTrigger>
          <TabsTrigger value="library">Library</TabsTrigger>
        </TabsList>
      </Tabs>

      {!isHomeScope && (
        <div className="flex items-center gap-3">
          <Label>Library</Label>
          {librariesList.length > 0 ? (
            <LibraryPicker
              libraries={librariesList}
              value={selectedLibraryId}
              onChange={setSelectedLibraryId}
            />
          ) : (
            <p className="text-muted-foreground text-sm">
              Create a library before configuring Library sections.
            </p>
          )}
        </div>
      )}

      {canDrag && (
        <p className="text-muted-foreground text-sm">
          Drag and drop sections to change their order.
        </p>
      )}

      <DndContext
        sensors={canDrag ? sensors : []}
        collisionDetection={closestCenter}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
        onDragCancel={handleDragCancel}
      >
      <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
        <Table>
        <TableHeader>
            <TableRow>
              <TableHead className="w-8"></TableHead>
              <TableHead>Title</TableHead>
              <TableHead>Type</TableHead>
              <TableHead className="w-20">Items</TableHead>
              <TableHead className="w-20">Featured</TableHead>
              <TableHead className="w-20">Enabled</TableHead>
              <TableHead className="w-24">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <SortableContext
            items={orderedSections.map((s) => s.id)}
            strategy={verticalListSortingStrategy}
          >
            <TableBody>
              {orderedSections.map((section) => (
                <SortableRow
                  key={section.id}
                  section={section}
                  canReorder={canDrag}
                  libraries={librariesList}
                  collectionLabels={collectionLabels}
                  onEdit={handleEdit}
                  onDelete={handleDelete}
                />
              ))}
              {orderedSections.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} className="text-muted-foreground py-8 text-center">
                    No sections configured for {scope} scope.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </SortableContext>
        </Table>
      </div>
        <DragOverlay>
          {activeSection ? <DragOverlayRow section={activeSection} /> : null}
        </DragOverlay>
      </DndContext>
    </div>
  );
}

interface SectionFormProps {
  section: PageSectionConfig | null;
  scope: string;
  currentLibraryId: number | null;
  libraries: Library[];
  collections: Array<{
    id: string;
    title: string;
    library_id: number;
  }>;
  onClose: () => void;
}

function SectionForm({
  section,
  scope,
  currentLibraryId,
  libraries,
  collections,
  onClose,
}: SectionFormProps) {
  const createMutation = useCreateSection();
  const updateMutation = useUpdateSection();
  const initialFormState = useMemo(
    () => ({
      title: section?.title ?? "",
      sectionType: section?.section_type ?? "recently_added",
      itemLimit: section?.item_limit ?? 20,
      featured: section?.featured ?? false,
      enabled: section?.enabled ?? true,
      queryDefinition: queryDefinitionFromSectionConfig(section?.config),
      selectedCollectionId: getSectionCollectionId(section),
    }),
    [section],
  );
  const [formState, setFormState] = useState(initialFormState);

  const showFilter = FILTER_SECTION_TYPES.has(formState.sectionType);
  const showCollectionPicker = formState.sectionType === "collection";
  const availableCollections =
    scope === "library" && currentLibraryId != null
      ? collections.filter((collection) => collection.library_id === currentLibraryId)
      : collections;

  useEffect(() => {
    setFormState(initialFormState);
  }, [initialFormState]);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();

    const configPayload: Record<string, unknown> = showCollectionPicker
      ? { library_collection_id: formState.selectedCollectionId }
      : queryDefinitionToSectionConfig(formState.queryDefinition);

    const data: Partial<PageSectionConfig> = {
      scope,
      ...(scope === "library" && currentLibraryId != null ? { library_id: currentLibraryId } : {}),
      title: formState.title,
      section_type: formState.sectionType,
      item_limit: formState.itemLimit,
      featured: formState.featured,
      enabled: formState.enabled,
      config: configPayload,
    };

    if (section) {
      updateMutation.mutate({ id: section.id, ...data }, { onSuccess: onClose });
    } else {
      createMutation.mutate(data, { onSuccess: onClose });
    }
  }

  const isSubmitting = createMutation.isPending || updateMutation.isPending;
  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="title">Title</Label>
        <Input
          id="title"
          value={formState.title}
          onChange={(e) => setFormState((current) => ({ ...current, title: e.target.value }))}
          placeholder="Section title"
          required
        />
      </div>

      <div className="space-y-2">
        <Label>Section Type</Label>
        <Select
          value={formState.sectionType}
          onValueChange={(value) => setFormState((current) => ({ ...current, sectionType: value }))}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SECTION_TYPES.map((t) => (
              <SelectItem key={t.value} value={t.value}>
                {t.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="itemLimit">Item Limit</Label>
        <Input
          id="itemLimit"
          type="number"
          value={formState.itemLimit}
          onChange={(e) =>
            setFormState((current) => ({ ...current, itemLimit: Number(e.target.value) }))
          }
          min={1}
          max={100}
        />
      </div>

      <div className="flex items-center justify-between">
        <Label htmlFor="featured">Featured (Hero Banner)</Label>
        <Switch
          id="featured"
          checked={formState.featured}
          onCheckedChange={(checked) =>
            setFormState((current) => ({ ...current, featured: checked === true }))
          }
        />
      </div>

      <div className="flex items-center justify-between">
        <Label htmlFor="enabled">Enabled</Label>
        <Switch
          id="enabled"
          checked={formState.enabled}
          onCheckedChange={(checked) =>
            setFormState((current) => ({ ...current, enabled: checked === true }))
          }
        />
      </div>

      {showCollectionPicker ? (
        <div className="space-y-2">
          <Label>Collection</Label>
          <Select
            value={formState.selectedCollectionId || "_none"}
            onValueChange={(value) =>
              setFormState((current) => ({
                ...current,
                selectedCollectionId: value === "_none" ? "" : value,
              }))
            }
          >
            <SelectTrigger>
              <SelectValue placeholder="Choose collection" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="_none">Choose collection</SelectItem>
              {availableCollections.map((collection) => (
                <SelectItem key={collection.id} value={collection.id}>
                  {formatCollectionOptionLabel(collection, libraries)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      ) : (
        <>
          {!showFilter ? (
            <>
              <div className="space-y-2">
                <Label>Filter by Type</Label>
                <Select
                  value={formState.queryDefinition.media_scope ?? "_all"}
                  onValueChange={(value) =>
                    setFormState((current) => ({
                      ...current,
                      queryDefinition: {
                        ...current.queryDefinition,
                        media_scope: value === "_all" ? undefined : (value as "movie" | "series"),
                      },
                    }))
                  }
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="_all">All Types</SelectItem>
                    <SelectItem value="movie">Movies</SelectItem>
                    <SelectItem value="series">Series</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-2">
                <Label>Filter by Library</Label>
                <LibraryMultiSelect
                  libraries={libraries}
                  value={formState.queryDefinition.library_ids}
                  onChange={(libraryIds) =>
                    setFormState((current) => ({
                      ...current,
                      queryDefinition: {
                        ...current.queryDefinition,
                        library_ids: libraryIds,
                      },
                    }))
                  }
                />
              </div>
            </>
          ) : null}
        </>
      )}

      {showFilter && (
        <div className="space-y-2">
          <Label>Filter Rules</Label>
          <CollectionRulesEditor
            value={formState.queryDefinition}
            onChange={(value) => setFormState((current) => ({ ...current, queryDefinition: value }))}
            libraries={libraries}
          />
        </div>
      )}

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button
          type="submit"
          disabled={
            isSubmitting ||
            (scope === "library" && currentLibraryId == null) ||
            (showCollectionPicker && !formState.selectedCollectionId)
          }
        >
          {section ? "Update" : "Create"}
        </Button>
      </div>
    </form>
  );
}
