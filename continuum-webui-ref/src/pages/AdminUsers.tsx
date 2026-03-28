import { useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router";
import type { AdminUser, CreateUserRequest, UpdateUserRequest } from "@/api/types";
import {
  useAdminUsers,
  useCreateUser,
  useUpdateUser,
  useDeleteUser,
} from "@/hooks/queries/admin/users";
import { useAdminLibraries } from "@/hooks/queries/admin/libraries";
import { useAdminServerSettings, useUpdateServerSetting } from "@/hooks/queries/admin/settings";
import { LibraryAccessSelector } from "@/components/LibraryAccessSelector";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
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
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { History, Plus, Pencil, Trash2, Settings2 } from "lucide-react";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import InviteCodesTab from "./admin-settings/InviteCodesTab";
import {
  PLAYBACK_QUALITY_OPTIONS,
  playbackQualityPresetFromValue,
  playbackQualityValueFromPreset,
  type PlaybackQualityPreset,
} from "@/lib/playback-quality";

export default function AdminUsers() {
  const { data: users = [], isLoading } = useAdminUsers();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [defaultsOpen, setDefaultsOpen] = useState(false);
  const [confirmDeleteUser, setConfirmDeleteUser] = useState<AdminUser | null>(null);
  const deleteMutation = useDeleteUser();

  function handleDelete(u: AdminUser) {
    setConfirmDeleteUser(u);
  }

  if (isLoading) return <div className="p-8">Loading users...</div>;

  return (
    <div className="space-y-6">
      <ConfirmDialog
        open={confirmDeleteUser !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteUser(null);
        }}
        title="Delete user"
        description={`Delete user "${confirmDeleteUser?.username}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteUser) deleteMutation.mutate(confirmDeleteUser.id);
          setConfirmDeleteUser(null);
        }}
      />
      <div className="page-header">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Users</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Manage access, defaults, and invite flow for the people using Continuum.
          </p>
        </div>
        <div className="flex gap-2">
          <Dialog open={defaultsOpen} onOpenChange={setDefaultsOpen}>
            <DialogTrigger asChild>
              <Button variant="outline" size="sm">
                <Settings2 className="mr-1 h-4 w-4" /> User Defaults
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Default New User Settings</DialogTitle>
              </DialogHeader>
              <UserDefaultsForm onClose={() => setDefaultsOpen(false)} />
            </DialogContent>
          </Dialog>
          <Dialog
            open={dialogOpen}
            onOpenChange={(open) => {
              setDialogOpen(open);
              if (!open) setEditingUser(null);
            }}
          >
            <DialogTrigger asChild>
              <Button size="sm">
                <Plus className="mr-1 h-4 w-4" /> Add User
              </Button>
            </DialogTrigger>
            <DialogContent className="sm:max-w-2xl">
              <DialogHeader>
                <DialogTitle>{editingUser ? "Edit User" : "Create User"}</DialogTitle>
              </DialogHeader>
              <UserForm
                user={editingUser}
                onClose={() => {
                  setDialogOpen(false);
                  setEditingUser(null);
                }}
              />
            </DialogContent>
          </Dialog>
        </div>
      </div>

      <Tabs defaultValue="users">
        <TabsList variant="line" className="border-border w-full justify-start border-b">
          <TabsTrigger value="users">Users</TabsTrigger>
          <TabsTrigger value="invite-codes">Invite Codes</TabsTrigger>
        </TabsList>
        <TabsContent value="users" className="pt-4">
          <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
            <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Username</TableHead>
                <TableHead>Email</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="w-24">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((u) => (
                <TableRow key={u.id}>
                  <TableCell>
                    <Link to={`/admin/users/${u.id}`} className="font-medium hover:underline">
                      {u.username}
                    </Link>
                  </TableCell>
                  <TableCell>{u.email}</TableCell>
                  <TableCell>
                    <Badge variant={u.role === "admin" ? "default" : "secondary"}>{u.role}</Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant={u.enabled ? "outline" : "destructive"}>
                      {u.enabled ? "Active" : "Disabled"}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <div className="flex gap-1">
                      <Button asChild variant="ghost" size="icon" className="h-7 w-7">
                        <Link
                          to={`/admin/history?user_id=${u.id}`}
                          aria-label={`View ${u.username} playback history`}
                        >
                          <History className="h-3 w-3" />
                        </Link>
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-7 w-7"
                        onClick={() => {
                          setEditingUser(u);
                          setDialogOpen(true);
                        }}
                      >
                        <Pencil className="h-3 w-3" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-7 w-7"
                        onClick={() => handleDelete(u)}
                      >
                        <Trash2 className="h-3 w-3" />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
            </Table>
          </div>
        </TabsContent>
        <TabsContent value="invite-codes" className="pt-4">
          <InviteCodesTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function UserForm({ user, onClose }: { user: AdminUser | null; onClose: () => void }) {
  const { data: settings } = useAdminServerSettings();
  const { data: libraries = [] } = useAdminLibraries();
  const [username, setUsername] = useState(user?.username ?? "");
  const [email, setEmail] = useState(user?.email ?? "");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState(user?.role ?? "user");
  const [enabled, setEnabled] = useState(user?.enabled ?? true);
  const [libraryIDs, setLibraryIDs] = useState<number[] | null>(user?.library_ids ?? null);
  const [maxStreams, setMaxStreams] = useState<number>(
    user?.max_streams ?? Number(settings?.["defaults.max_streams"] ?? "6"),
  );
  const [maxTranscodes, setMaxTranscodes] = useState<number>(
    user?.max_transcodes ?? Number(settings?.["defaults.max_transcodes"] ?? "2"),
  );
  const [maxPlaybackQualityPreset, setMaxPlaybackQualityPreset] = useState<PlaybackQualityPreset>(
    playbackQualityPresetFromValue(
      user?.max_playback_quality ?? settings?.["defaults.max_playback_quality"],
    ),
  );
  const [downloadAllowed, setDownloadAllowed] = useState(
    user?.download_allowed ?? settings?.["defaults.download_allowed"] !== "false",
  );
  const [downloadTranscodeAllowed, setDownloadTranscodeAllowed] = useState(
    user?.download_transcode_allowed ??
      settings?.["defaults.download_transcode_allowed"] === "true",
  );
  const createMutation = useCreateUser();
  const updateMutation = useUpdateUser();
  const isPending = createMutation.isPending || updateMutation.isPending;

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (user) {
      const body: UpdateUserRequest = {
        username,
        email,
        role,
        enabled,
        library_ids: libraryIDs,
        max_streams: maxStreams,
        max_transcodes: maxTranscodes,
        max_playback_quality: playbackQualityValueFromPreset(maxPlaybackQualityPreset),
        download_allowed: downloadAllowed,
        download_transcode_allowed: downloadTranscodeAllowed,
      };
      if (password) body.password = password;
      updateMutation.mutate({ id: user.id, body }, { onSuccess: onClose });
    } else {
      const body: CreateUserRequest = {
        username,
        email,
        password,
        role,
        max_streams: maxStreams,
        max_transcodes: maxTranscodes,
        max_playback_quality: playbackQualityValueFromPreset(maxPlaybackQualityPreset) || undefined,
        download_allowed: downloadAllowed,
        download_transcode_allowed: downloadTranscodeAllowed,
      };
      if (libraryIDs !== null) body.library_ids = libraryIDs;
      createMutation.mutate(body, { onSuccess: onClose });
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex max-h-[70vh] flex-col">
      <Tabs defaultValue="account" className="min-h-0 flex-1">
        <TabsList variant="line" className="border-border mb-4 w-full justify-start border-b pb-1">
          <TabsTrigger value="account" className="flex-none px-1">
            Account
          </TabsTrigger>
          <TabsTrigger value="access" className="flex-none px-1">
            Access
          </TabsTrigger>
          <TabsTrigger value="limits" className="flex-none px-1">
            Limits
          </TabsTrigger>
        </TabsList>

        <div className="min-h-0 flex-1 overflow-y-auto pr-1">
          <TabsContent value="account" className="mt-0 space-y-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-2">
                <Label>Username</Label>
                <Input value={username} onChange={(e) => setUsername(e.target.value)} required />
              </div>
              <div className="space-y-2">
                <Label>Email</Label>
                <Input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                />
              </div>
              <div className="space-y-2">
                <Label>Password {user && "(leave blank to keep current)"}</Label>
                <Input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required={!user}
                />
              </div>
              <div className="space-y-2">
                <Label>Role</Label>
                <Select value={role} onValueChange={setRole}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="user">User</SelectItem>
                    <SelectItem value="admin">Admin</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            {user && (
              <div className="border-border flex items-center justify-between rounded-md border px-3 py-2">
                <div>
                  <div className="text-sm font-medium">Account status</div>
                  <div className="text-muted-foreground text-xs">
                    Disable access without deleting the user.
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  <Label className="text-xs">Enabled</Label>
                  <Switch checked={enabled} onCheckedChange={setEnabled} />
                </div>
              </div>
            )}
          </TabsContent>

          <TabsContent value="access" className="mt-0 space-y-4">
            <LibraryAccessSelector
              libraries={libraries}
              value={libraryIDs}
              onChange={setLibraryIDs}
            />
            <div className="grid gap-2 sm:grid-cols-2">
              <div className="border-border flex items-center justify-between rounded-md border px-3 py-2">
                <Label>Downloads Allowed</Label>
                <Switch checked={downloadAllowed} onCheckedChange={setDownloadAllowed} />
              </div>
              <div className="border-border flex items-center justify-between rounded-md border px-3 py-2">
                <Label>Download Transcode Allowed</Label>
                <Switch
                  checked={downloadTranscodeAllowed}
                  onCheckedChange={setDownloadTranscodeAllowed}
                />
              </div>
            </div>
          </TabsContent>

          <TabsContent value="limits" className="mt-0 space-y-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <Label>Max Streams</Label>
                <Input
                  type="number"
                  min={0}
                  value={maxStreams}
                  onChange={(e) => setMaxStreams(Number(e.target.value))}
                />
                <p className="text-muted-foreground text-xs">0 = unlimited</p>
              </div>
              <div className="space-y-1">
                <Label>Max Transcodes</Label>
                <Input
                  type="number"
                  min={0}
                  value={maxTranscodes}
                  onChange={(e) => setMaxTranscodes(Number(e.target.value))}
                />
                <p className="text-muted-foreground text-xs">0 = unlimited</p>
              </div>
              <div className="space-y-1 sm:col-span-2">
                <Label>Max Playback Quality</Label>
                <Select
                  value={maxPlaybackQualityPreset}
                  onValueChange={(value) =>
                    setMaxPlaybackQualityPreset(value as PlaybackQualityPreset)
                  }
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PLAYBACK_QUALITY_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-muted-foreground text-xs">
                  {
                    PLAYBACK_QUALITY_OPTIONS.find(
                      (option) => option.value === maxPlaybackQualityPreset,
                    )?.description
                  }
                </p>
              </div>
            </div>
          </TabsContent>
        </div>
      </Tabs>

      <div className="border-border mt-4 border-t pt-4">
        <Button type="submit" className="w-full" disabled={isPending}>
          {isPending ? "Saving..." : "Save"}
        </Button>
      </div>
    </form>
  );
}

function UserDefaultsForm({ onClose }: { onClose: () => void }) {
  const { data: settings } = useAdminServerSettings();
  const updateSetting = useUpdateServerSetting();
  const [isSaving, setIsSaving] = useState(false);

  const [maxStreams, setMaxStreams] = useState(Number(settings?.["defaults.max_streams"] ?? "6"));
  const [maxTranscodes, setMaxTranscodes] = useState(
    Number(settings?.["defaults.max_transcodes"] ?? "2"),
  );
  const [maxPlaybackQualityPreset, setMaxPlaybackQualityPreset] = useState<PlaybackQualityPreset>(
    playbackQualityPresetFromValue(settings?.["defaults.max_playback_quality"]),
  );
  const [downloadAllowed, setDownloadAllowed] = useState(
    settings?.["defaults.download_allowed"] !== "false",
  );
  const [downloadTranscodeAllowed, setDownloadTranscodeAllowed] = useState(
    settings?.["defaults.download_transcode_allowed"] === "true",
  );

  function handleSave() {
    setIsSaving(true);
    const updates = [
      { key: "defaults.max_streams", value: String(maxStreams) },
      { key: "defaults.max_transcodes", value: String(maxTranscodes) },
      {
        key: "defaults.max_playback_quality",
        value: playbackQualityValueFromPreset(maxPlaybackQualityPreset),
      },
      { key: "defaults.download_allowed", value: String(downloadAllowed) },
      { key: "defaults.download_transcode_allowed", value: String(downloadTranscodeAllowed) },
    ];
    Promise.all(updates.map((u) => updateSetting.mutateAsync(u)))
      .then(() => onClose())
      .finally(() => setIsSaving(false));
  }

  return (
    <div className="space-y-4">
      <p className="text-muted-foreground text-sm">
        These defaults will pre-fill the form when creating new users.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label>Max Streams</Label>
          <Input
            type="number"
            min={0}
            value={maxStreams}
            onChange={(e) => setMaxStreams(Number(e.target.value))}
          />
          <p className="text-muted-foreground text-xs">0 = unlimited</p>
        </div>
        <div className="space-y-1">
          <Label>Max Transcodes</Label>
          <Input
            type="number"
            min={0}
            value={maxTranscodes}
            onChange={(e) => setMaxTranscodes(Number(e.target.value))}
          />
          <p className="text-muted-foreground text-xs">0 = unlimited</p>
        </div>
      </div>
      <div className="space-y-1">
        <Label>Max Playback Quality</Label>
        <Select
          value={maxPlaybackQualityPreset}
          onValueChange={(value) => setMaxPlaybackQualityPreset(value as PlaybackQualityPreset)}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PLAYBACK_QUALITY_OPTIONS.map((option) => (
              <SelectItem key={option.value} value={option.value}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="text-muted-foreground text-xs">
          {
            PLAYBACK_QUALITY_OPTIONS.find((option) => option.value === maxPlaybackQualityPreset)
              ?.description
          }
        </p>
      </div>
      <div className="flex items-center justify-between">
        <Label>Downloads Allowed</Label>
        <Switch checked={downloadAllowed} onCheckedChange={setDownloadAllowed} />
      </div>
      <div className="flex items-center justify-between">
        <Label>Download Transcode Allowed</Label>
        <Switch checked={downloadTranscodeAllowed} onCheckedChange={setDownloadTranscodeAllowed} />
      </div>
      <Button onClick={handleSave} className="w-full" disabled={isSaving}>
        {isSaving ? "Saving..." : "Save Defaults"}
      </Button>
    </div>
  );
}
