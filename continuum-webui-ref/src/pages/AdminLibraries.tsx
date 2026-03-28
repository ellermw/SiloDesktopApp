import { useState, useEffect, useCallback } from "react";
import type { FormEvent } from "react";
import type {
  CreateLibraryRequest,
  Library,
  LibraryMountCheckResponse,
  LibraryProviderChainEntry,
  LibrarySkippedRoot,
} from "@/api/types";
import {
  useAdminLibraries,
  useSkippedLibraryRoots,
  useCheckLibraryMount,
  useCreateLibrary,
  useUpdateLibrary,
  useDeleteLibrary,
  useScanLibrary,
  useScanAllLibraries,
  useRefreshLibraryMetadata,
  useConfirmEmptyRootCleanup,
  useLibraryProviders,
  useSetLibraryProviders,
  useUploadLibraryPoster,
  useDeleteLibraryPoster,
} from "@/hooks/queries/admin/libraries";
import { useAdminProviders } from "@/hooks/queries/admin/providers";
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
import { Link } from "react-router";
import {
  Plus,
  Pencil,
  Trash2,
  RefreshCw,
  DatabaseBackup,
  ArrowUp,
  ArrowDown,
  X,
  Wrench,
  HardDrive,
  ImageIcon,
} from "lucide-react";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { cn } from "@/lib/utils";

export default function AdminLibraries() {
  const { data: libraries = [], isLoading } = useAdminLibraries();
  const { data: skippedRoots = [] } = useSkippedLibraryRoots();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingLib, setEditingLib] = useState<Library | null>(null);
  const [confirmDeleteLib, setConfirmDeleteLib] = useState<Library | null>(null);
  const [confirmEmptyRootLib, setConfirmEmptyRootLib] = useState<Library | null>(null);
  const [lastMountCheckByLibraryId, setLastMountCheckByLibraryId] = useState<
    Record<number, LibraryMountCheckResponse>
  >({});
  const deleteMutation = useDeleteLibrary();
  const mountCheckMutation = useCheckLibraryMount();
  const scanMutation = useScanLibrary();
  const scanAllMutation = useScanAllLibraries();
  const refreshMutation = useRefreshLibraryMetadata();
  const confirmEmptyRootCleanupMutation = useConfirmEmptyRootCleanup();

  function handleDelete(lib: Library) {
    setConfirmDeleteLib(lib);
  }

  function handleConfirmEmptyRootCleanup(lib: Library) {
    setConfirmEmptyRootLib(lib);
  }

  function handleMountCheck(libraryId: number) {
    mountCheckMutation.mutate(libraryId, {
      onSuccess: (result) => {
        setLastMountCheckByLibraryId((current) => ({
          ...current,
          [libraryId]: result,
        }));
      },
    });
  }

  if (isLoading) return <div className="p-8">Loading libraries...</div>;

  return (
    <div className="space-y-6">
      <ConfirmDialog
        open={confirmDeleteLib !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteLib(null);
        }}
        title="Delete library"
        description={`Delete library "${confirmDeleteLib?.name}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteLib) deleteMutation.mutate(confirmDeleteLib.id);
          setConfirmDeleteLib(null);
        }}
      />
      <ConfirmDialog
        open={confirmEmptyRootLib !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmEmptyRootLib(null);
        }}
        title="Confirm empty root cleanup"
        description={`If the next scan still finds 0 media files for "${confirmEmptyRootLib?.name}", remove the library items?`}
        confirmLabel="Confirm"
        variant="destructive"
        onConfirm={() => {
          if (confirmEmptyRootLib) confirmEmptyRootCleanupMutation.mutate(confirmEmptyRootLib.id);
          setConfirmEmptyRootLib(null);
        }}
      />
      <div className="page-header">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Libraries</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Manage library roots and scans. Catalog import/export now lives under Maintenance.
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => scanAllMutation.mutate()}
            disabled={scanAllMutation.isPending}
          >
            <RefreshCw
              className={`mr-1 h-4 w-4 ${scanAllMutation.isPending ? "animate-spin" : ""}`}
            />{" "}
            Scan All
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link to="/admin/maintenance">
              <Wrench className="mr-1 h-4 w-4" />
              Catalog Maintenance
            </Link>
          </Button>
          <Dialog
            open={dialogOpen}
            onOpenChange={(open) => {
              setDialogOpen(open);
              if (!open) setEditingLib(null);
            }}
          >
            <DialogTrigger asChild>
              <Button size="sm">
                <Plus className="mr-1 h-4 w-4" /> Add Library
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>{editingLib ? "Edit Library" : "Add Library"}</DialogTitle>
              </DialogHeader>
              <LibraryForm
                library={editingLib}
                onClose={() => {
                  setDialogOpen(false);
                  setEditingLib(null);
                }}
              />
            </DialogContent>
          </Dialog>
        </div>
      </div>

      <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
        <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Paths</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Last Scanned</TableHead>
            <TableHead className="w-32">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {libraries.map((lib) => {
            const isScanning = scanMutation.isPending && scanMutation.variables === lib.id;
            const isRefreshing = refreshMutation.isPending && refreshMutation.variables === lib.id;
            const isCheckingMount =
              mountCheckMutation.isPending && mountCheckMutation.variables === lib.id;
            const mountCheck = lastMountCheckByLibraryId[lib.id];
            return (
              <TableRow key={lib.id}>
                <TableCell className="font-medium">{lib.name}</TableCell>
                <TableCell className="font-mono text-xs">
                  {lib.paths.map((p) => (
                    <div key={p}>{p}</div>
                  ))}
                </TableCell>
                <TableCell>
                  <Badge variant="secondary">{lib.type}</Badge>
                </TableCell>
                <TableCell>
                  <div className="flex flex-col gap-1">
                    <Badge variant={lib.enabled ? "outline" : "destructive"}>
                      {lib.enabled ? "Enabled" : "Disabled"}
                    </Badge>
                    {lib.scan_warning_code === "empty_root" ? (
                      <Badge variant="destructive">Empty root guarded</Badge>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell className="text-muted-foreground text-xs">
                  <div className="space-y-1">
                    <div>
                      {lib.last_scanned_at
                        ? new Date(lib.last_scanned_at).toLocaleString()
                        : "Never"}
                    </div>
                    {lib.scan_warning_at ? (
                      <div className="text-destructive text-[11px]">
                        Warning: {new Date(lib.scan_warning_at).toLocaleString()}
                      </div>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      title="Check mount"
                      disabled={isCheckingMount}
                      onClick={() => handleMountCheck(lib.id)}
                    >
                      <HardDrive className={`h-3 w-3 ${isCheckingMount ? "animate-pulse" : ""}`} />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      title="Scan Library"
                      disabled={isScanning}
                      onClick={() => scanMutation.mutate(lib.id)}
                    >
                      <RefreshCw className={`h-3 w-3 ${isScanning ? "animate-spin" : ""}`} />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      title="Refresh metadata"
                      disabled={isRefreshing}
                      onClick={() => refreshMutation.mutate(lib.id)}
                    >
                      <DatabaseBackup className={`h-3 w-3 ${isRefreshing ? "animate-spin" : ""}`} />
                    </Button>
                    {lib.scan_warning_code === "empty_root" ? (
                      <Button
                        variant="ghost"
                        size="icon"
                        className="text-destructive h-7 w-7"
                        title="Confirm deletion for the next empty-root scan"
                        disabled={
                          confirmEmptyRootCleanupMutation.isPending &&
                          confirmEmptyRootCleanupMutation.variables === lib.id
                        }
                        onClick={() => handleConfirmEmptyRootCleanup(lib)}
                      >
                        <Trash2 className="h-3 w-3" />
                      </Button>
                    ) : null}
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() => {
                        setEditingLib(lib);
                        setDialogOpen(true);
                      }}
                    >
                      <Pencil className="h-3 w-3" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() => handleDelete(lib)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                  {mountCheck ? (
                    <MountCheckInlineResult result={mountCheck} warning={false} />
                  ) : null}
                </TableCell>
              </TableRow>
            );
          })}
          {libraries
            .filter((lib) => lib.scan_warning_code === "empty_root")
            .map((lib) => {
              const mountCheck = lastMountCheckByLibraryId[lib.id];
              return (
                <TableRow key={`${lib.id}-warning`}>
                  <TableCell colSpan={6} className="bg-destructive/5 text-sm">
                    <div className="flex flex-col gap-2 py-1">
                      <div className="text-destructive font-medium">
                        Scan found 0 media files for this library. Cleanup was paused to avoid
                        accidental deletion.
                      </div>
                      <div className="text-muted-foreground">
                        {lib.scan_warning_message ??
                          "Run another scan after storage returns, or confirm deletion before the next empty-root scan."}
                      </div>
                      <div>
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={
                            mountCheckMutation.isPending && mountCheckMutation.variables === lib.id
                          }
                          onClick={() => handleMountCheck(lib.id)}
                        >
                          <HardDrive
                            className={cn(
                              "mr-1 h-3.5 w-3.5",
                              mountCheckMutation.isPending &&
                                mountCheckMutation.variables === lib.id &&
                                "animate-pulse",
                            )}
                          />
                          Check Mount
                        </Button>
                      </div>
                      {mountCheck ? (
                        <MountCheckInlineResult result={mountCheck} warning={true} />
                      ) : null}
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
        </TableBody>
        </Table>
      </div>

      {skippedRoots.length > 0 ? <SkippedRootsSection skippedRoots={skippedRoots} /> : null}
    </div>
  );
}

function MountCheckInlineResult({
  result,
  warning,
}: {
  result: LibraryMountCheckResponse;
  warning: boolean;
}) {
  const failingRoots = result.roots.filter((root) => !root.reachable);

  return (
    <div
      className={cn(
        "mt-2 space-y-1 rounded-md border px-2 py-1.5 text-xs",
        result.healthy
          ? "border-emerald-500/25 bg-emerald-500/5 text-emerald-700 dark:text-emerald-300"
          : "border-destructive/25 bg-destructive/5 text-destructive",
      )}
    >
      <div className="font-medium">{result.summary}</div>
      {result.healthy && warning ? (
        <div className="text-foreground/80 dark:text-foreground/70">
          Storage looks available again. Run Scan Library to verify contents and clear the
          warning.
        </div>
      ) : null}
      {!result.healthy
        ? failingRoots.map((root) => (
            <div key={root.path} className="text-foreground/80 dark:text-foreground/70">
              <span className="font-mono">{root.path}</span>
              {root.error_message ? `: ${root.error_message}` : ""}
            </div>
          ))
        : null}
      <div className="text-foreground/60 dark:text-foreground/50">
        Checked {new Date(result.checked_at).toLocaleString()}
      </div>
    </div>
  );
}

function SkippedRootsSection({ skippedRoots }: { skippedRoots: LibrarySkippedRoot[] }) {
  return (
    <section className="rounded-lg border border-border/70 bg-muted/20 p-4">
      <div className="mb-3 space-y-1">
        <h2 className="text-sm font-semibold tracking-wide">Troubleshooting</h2>
        <p className="text-muted-foreground text-sm">
          Skipped roots are listed here for low-key admin visibility when scanner matching is
          gated by missing folder IDs.
        </p>
      </div>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Skipped roots</TableHead>
            <TableHead>Library</TableHead>
            <TableHead>Reason</TableHead>
            <TableHead>Sample</TableHead>
            <TableHead>First seen</TableHead>
            <TableHead>Last seen</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {skippedRoots.map((root) => (
            <TableRow key={`${root.library_id}:${root.root_path}`}>
              <TableCell className="font-mono text-xs">{root.root_path}</TableCell>
              <TableCell>{root.library_name}</TableCell>
              <TableCell>
                <Badge variant="outline">{root.reason}</Badge>
              </TableCell>
              <TableCell className="text-muted-foreground max-w-[28rem] truncate text-xs">
                {root.sample_file_path}
              </TableCell>
              <TableCell className="text-muted-foreground text-xs">
                {new Date(root.first_seen_at).toLocaleString()}
              </TableCell>
              <TableCell className="text-muted-foreground text-xs">
                {new Date(root.last_seen_at).toLocaleString()}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </section>
  );
}

interface ChainItem {
  provider_id: number;
  provider_slug: string;
}

function LibraryForm({ library, onClose }: { library: Library | null; onClose: () => void }) {
  const [name, setName] = useState(library?.name ?? "");
  const [paths, setPaths] = useState<string[]>(library?.paths?.length ? library.paths : [""]);
  const [type, setType] = useState(library?.type ?? "movies");
  const [enabled, setEnabled] = useState(library?.enabled ?? true);
  const [chain, setChain] = useState<ChainItem[]>([]);
  const [chainDirty, setChainDirty] = useState(false);

  const createMutation = useCreateLibrary();
  const updateMutation = useUpdateLibrary();
  const setChainMutation = useSetLibraryProviders();
  const { data: allProviders = [] } = useAdminProviders();
  const { data: currentChain } = useLibraryProviders(library?.id ?? null);

  const isPending =
    createMutation.isPending || updateMutation.isPending || setChainMutation.isPending;

  // Initialize chain from server data when it loads.
  useEffect(() => {
    if (currentChain && !chainDirty) {
      setChain(
        currentChain.map((e: LibraryProviderChainEntry) => ({
          provider_id: e.provider_id,
          provider_slug: e.provider_slug,
        })),
      );
    }
  }, [currentChain, chainDirty]);

  function updatePath(index: number, value: string) {
    const next = [...paths];
    next[index] = value;
    setPaths(next);
  }

  function addPath() {
    setPaths([...paths, ""]);
  }

  function removePath(index: number) {
    setPaths(paths.filter((_, i) => i !== index));
  }

  const moveChainItem = useCallback(
    (index: number, direction: -1 | 1) => {
      const next = [...chain];
      const target = index + direction;
      if (target < 0 || target >= next.length) return;
      const currentItem = next[index];
      const targetItem = next[target];
      if (!currentItem || !targetItem) return;
      next[index] = targetItem;
      next[target] = currentItem;
      setChain(next);
      setChainDirty(true);
    },
    [chain],
  );

  function removeChainItem(index: number) {
    setChain(chain.filter((_, i) => i !== index));
    setChainDirty(true);
  }

  function addProviderToChain(providerId: string) {
    const id = Number(providerId);
    const provider = allProviders.find((p) => p.id === id);
    if (!provider || chain.some((c) => c.provider_id === id)) return;
    setChain([...chain, { provider_id: id, provider_slug: provider.slug }]);
    setChainDirty(true);
  }

  // Providers not yet in the chain.
  const availableProviders = allProviders.filter((p) => !chain.some((c) => c.provider_id === p.id));

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const filteredPaths = paths.filter((p) => p.trim());
    if (filteredPaths.length === 0) return;

    const body: CreateLibraryRequest = {
      name,
      paths: filteredPaths,
      type,
      enabled,
    };

    if (library) {
      updateMutation.mutate(
        { id: library.id, body },
        {
          onSuccess: () => {
            if (chainDirty) {
              setChainMutation.mutate(
                {
                  id: library.id,
                  body: {
                    entries: chain.map((c, i) => ({
                      provider_id: c.provider_id,
                      priority: i,
                    })),
                  },
                },
                { onSuccess: onClose },
              );
            } else {
              onClose();
            }
          },
        },
      );
    } else {
      createMutation.mutate(body, { onSuccess: onClose });
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div className="grid grid-cols-[1fr_auto] items-end gap-3">
        <div className="space-y-1.5">
          <Label>Name</Label>
          <Input value={name} onChange={(e) => setName(e.target.value)} required />
        </div>
        <div className="flex items-center gap-2 pb-0.5">
          <Switch id="enabled-switch" checked={enabled} onCheckedChange={setEnabled} />
          <Label htmlFor="enabled-switch" className="text-muted-foreground text-xs">
            Enabled
          </Label>
        </div>
      </div>
      <div className="space-y-1.5">
        <Label>Paths</Label>
        {paths.map((p, i) => (
          <div key={i} className="flex gap-1">
            <Input
              value={p}
              onChange={(e) => updatePath(i, e.target.value)}
              placeholder="/mnt/media/movies"
              required
            />
            {paths.length > 1 && (
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="h-9 w-9 shrink-0"
                onClick={() => removePath(i)}
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        ))}
        <Button type="button" variant="outline" size="sm" onClick={addPath}>
          <Plus className="mr-1 h-3.5 w-3.5" /> Add Path
        </Button>
      </div>
      <div className="grid grid-cols-2 items-end gap-3">
        <div className="space-y-1.5">
          <Label>Type</Label>
          <Select value={type} onValueChange={setType}>
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="movies">Movies</SelectItem>
              <SelectItem value="series">Series</SelectItem>
              <SelectItem value="mixed">Mixed</SelectItem>
            </SelectContent>
          </Select>
        </div>
        {library && <LibraryPosterSection library={library} />}
      </div>

      {/* Provider Priority Chain - only shown when editing */}
      {library && (
        <div className="space-y-1.5">
          <Label>Metadata Provider Priority</Label>
          {chain.length > 0 && (
            <div className="space-y-0.5 rounded-md border p-1.5">
              {chain.map((item, i) => (
                <div
                  key={item.provider_id}
                  className="bg-muted/50 flex items-center gap-1 rounded px-1.5 py-0.5 text-sm"
                >
                  <span className="text-muted-foreground w-4 text-center text-[11px]">{i + 1}</span>
                  <span className="flex-1 font-mono text-xs">{item.provider_slug}</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-5 w-5"
                    disabled={i === 0}
                    onClick={() => moveChainItem(i, -1)}
                  >
                    <ArrowUp className="h-2.5 w-2.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-5 w-5"
                    disabled={i === chain.length - 1}
                    onClick={() => moveChainItem(i, 1)}
                  >
                    <ArrowDown className="h-2.5 w-2.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-5 w-5"
                    onClick={() => removeChainItem(i)}
                  >
                    <X className="h-2.5 w-2.5" />
                  </Button>
                </div>
              ))}
            </div>
          )}
          {availableProviders.length > 0 && (
            <Select onValueChange={addProviderToChain} value="">
              <SelectTrigger>
                <SelectValue placeholder="Add provider..." />
              </SelectTrigger>
              <SelectContent>
                {availableProviders.map((p) => (
                  <SelectItem key={p.id} value={String(p.id)}>
                    {p.slug} ({p.provider_type})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </div>
      )}

      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? "Saving..." : "Save"}
      </Button>
    </form>
  );
}

function LibraryPosterSection({ library }: { library: Library }) {
  const uploadMutation = useUploadLibraryPoster();
  const deleteMutation = useDeleteLibraryPoster();
  const fileInputId = `poster-upload-${library.id}`;

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    uploadMutation.mutate({ id: library.id, file });
    e.target.value = "";
  }

  return (
    <div className="space-y-1.5">
      <Label>Poster</Label>
      <div className="flex items-center gap-2">
        {library.poster_url ? (
          <img
            src={library.poster_url}
            alt={`${library.name} poster`}
            className="border-border h-14 flex-shrink-0 rounded border object-cover"
            style={{ aspectRatio: "16/9" }}
          />
        ) : (
          <div
            className="border-border bg-muted/30 flex h-14 flex-shrink-0 items-center justify-center rounded border border-dashed"
            style={{ aspectRatio: "16/9" }}
          >
            <ImageIcon className="text-muted-foreground/40 h-4 w-4" />
          </div>
        )}
        <input
          id={fileInputId}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          className="hidden"
          onChange={handleFileChange}
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="h-8 text-xs"
          onClick={() => document.getElementById(fileInputId)?.click()}
          disabled={uploadMutation.isPending}
        >
          {uploadMutation.isPending ? "..." : library.poster_url ? "Replace" : "Upload"}
        </Button>
        {library.poster_url && (
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="text-muted-foreground hover:text-destructive h-8 w-8"
            onClick={() => deleteMutation.mutate(library.id)}
            disabled={deleteMutation.isPending}
            title="Remove poster"
          >
            <Trash2 className="h-3 w-3" />
          </Button>
        )}
      </div>
    </div>
  );
}
