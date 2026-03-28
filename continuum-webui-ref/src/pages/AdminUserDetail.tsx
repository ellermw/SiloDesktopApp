import { useState } from "react";
import type { FormEvent } from "react";
import { useParams, Link } from "react-router";
import {
  useAdminUser,
  useUpdateUser,
  useDeleteUser,
  useImpersonateUser,
} from "@/hooks/queries/admin/users";
import { useAdminUserProfiles } from "@/hooks/queries/admin/history";
import { useAdminPlaybackHistory } from "@/hooks/queries/admin/history";
import { useAdminLibraries } from "@/hooks/queries/admin/libraries";
import { useUserIPs } from "@/hooks/queries/admin/ips";
import type { AdminUser, AdminUserProfile, UpdateUserRequest, UserIPEntry } from "@/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { LibraryAccessSelector } from "@/components/LibraryAccessSelector";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
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
import { ArrowLeft, Pencil, UserCircle } from "lucide-react";
import { useNavigate } from "react-router";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { useAuth } from "@/hooks/useAuth";
import {
  PLAYBACK_QUALITY_OPTIONS,
  formatPlaybackQualityPreset,
  playbackQualityPresetFromValue,
  playbackQualityValueFromPreset,
  type PlaybackQualityPreset,
} from "@/lib/playback-quality";
import { toast } from "sonner";

export default function AdminUserDetail() {
  const { id } = useParams<{ id: string }>();
  const userId = Number(id);
  const navigate = useNavigate();
  const { beginImpersonation } = useAuth();
  const { data: user, isLoading, error } = useAdminUser(userId);
  const [editOpen, setEditOpen] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [confirmImpersonateOpen, setConfirmImpersonateOpen] = useState(false);
  const deleteMutation = useDeleteUser();
  const impersonateMutation = useImpersonateUser();

  if (isLoading) return <div className="page-shell py-8">Loading user...</div>;
  if (error || !user) return <div className="page-shell text-destructive py-8">User not found.</div>;

  const impersonationDisabled = user.role === "admin" || !user.enabled;

  function handleDelete() {
    setConfirmDeleteOpen(true);
  }

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <div className="page-header gap-5">
        <Button asChild variant="ghost" size="icon" className="h-8 w-8">
          <Link to="/admin/users">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="min-w-0 flex-1 space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">{user.username}</h1>
            <Badge variant={user.role === "admin" ? "default" : "secondary"}>{user.role}</Badge>
            <Badge variant={user.enabled ? "outline" : "destructive"}>
              {user.enabled ? "Active" : "Disabled"}
            </Badge>
          </div>
          <p className="page-subtitle text-sm sm:text-base">{user.email}</p>
        </div>
        <div className="flex w-full flex-wrap gap-2 sm:w-auto">
          <Button
            variant="outline"
            size="sm"
            className="flex-1 sm:flex-none"
            onClick={() => setConfirmImpersonateOpen(true)}
            disabled={impersonationDisabled || impersonateMutation.isPending}
          >
            Impersonate
          </Button>
          <Dialog open={editOpen} onOpenChange={setEditOpen}>
            <DialogTrigger asChild>
              <Button variant="outline" size="sm" className="flex-1 sm:flex-none">
                <Pencil className="mr-1 h-3.5 w-3.5" /> Edit
              </Button>
            </DialogTrigger>
            <DialogContent className="sm:max-w-2xl">
              <DialogHeader>
                <DialogTitle>Edit User</DialogTitle>
              </DialogHeader>
              <EditUserForm user={user} onClose={() => setEditOpen(false)} />
            </DialogContent>
          </Dialog>
          <Button
            variant="destructive"
            size="sm"
            className="flex-1 sm:flex-none"
            onClick={handleDelete}
            disabled={deleteMutation.isPending}
          >
            Delete
          </Button>
        </div>
      </div>

      <Tabs defaultValue="overview">
        <TabsList
          variant="line"
          className="surface-panel-subtle h-auto w-full justify-start gap-1 overflow-x-auto rounded-[1.2rem] border-0 bg-transparent p-1"
        >
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="profiles">Profiles</TabsTrigger>
          <TabsTrigger value="history">Watch History</TabsTrigger>
          <TabsTrigger value="ips">IP History</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="mt-4">
          <OverviewTab user={user} />
        </TabsContent>
        <TabsContent value="profiles" className="mt-4">
          <ProfilesTab userId={userId} />
        </TabsContent>
        <TabsContent value="history" className="mt-4">
          <WatchHistoryTab userId={userId} />
        </TabsContent>
        <TabsContent value="ips" className="mt-4">
          <IPHistoryTab userId={userId} />
        </TabsContent>
      </Tabs>
      <ConfirmDialog
        open={confirmImpersonateOpen}
        onOpenChange={(open) => {
          if (!open) setConfirmImpersonateOpen(false);
        }}
        title="Impersonate user"
        description={`Continue as "${user.username}"? Admin access will be removed until you end impersonation.`}
        confirmLabel="Impersonate"
        onConfirm={() => {
          setConfirmImpersonateOpen(false);
          void impersonateMutation
            .mutateAsync(user.id)
            .then((result) => {
              beginImpersonation(result, `/admin/users/${user.id}`);
              navigate("/profiles");
            })
            .catch((error: unknown) => {
              toast.error(error instanceof Error ? error.message : "Failed to start impersonation");
            });
        }}
      />
      <ConfirmDialog
        open={confirmDeleteOpen}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteOpen(false);
        }}
        title="Delete user"
        description={`Delete user "${user.username}"? This cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          deleteMutation.mutate(user.id, {
            onSuccess: () => navigate("/admin/users"),
          });
        }}
      />
    </div>
  );
}

function OverviewTab({ user }: { user: AdminUser }) {
  const { data: libraries = [] } = useAdminLibraries();

  const libraryNames =
    user.library_ids === null
      ? "All libraries"
      : user.library_ids.length === 0
        ? "None"
        : user.library_ids
            .map((id) => {
              const lib = libraries.find((l) => l.id === id);
              return lib ? lib.name : `#${id}`;
            })
            .join(", ");

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <div className="surface-panel overflow-hidden rounded-[1.6rem] border-0">
        <div className="border-border border-b px-4 py-3">
          <h3 className="text-sm font-medium">Account</h3>
        </div>
        <div className="divide-border divide-y">
          <DetailRow label="Username" value={user.username} />
          <DetailRow label="Email" value={user.email} />
          <DetailRow label="Role" value={user.role} />
          <DetailRow label="Status" value={user.enabled ? "Active" : "Disabled"} />
          <DetailRow label="Created" value={formatDate(user.created_at)} />
          <DetailRow label="Updated" value={formatDate(user.updated_at)} />
        </div>
      </div>

      <div className="surface-panel overflow-hidden rounded-[1.6rem] border-0">
        <div className="border-border border-b px-4 py-3">
          <h3 className="text-sm font-medium">Permissions & Limits</h3>
        </div>
        <div className="divide-border divide-y">
          <DetailRow label="Library Access" value={libraryNames} />
          <DetailRow
            label="Max Playback Quality"
            value={formatPlaybackQualityPreset(user.max_playback_quality)}
          />
          <DetailRow
            label="Max Streams"
            value={user.max_streams === 0 ? "Unlimited" : String(user.max_streams)}
          />
          <DetailRow
            label="Max Transcodes"
            value={user.max_transcodes === 0 ? "Unlimited" : String(user.max_transcodes)}
          />
          <DetailRow label="Downloads" value={user.download_allowed ? "Allowed" : "Not allowed"} />
          <DetailRow
            label="Download Transcode"
            value={user.download_transcode_allowed ? "Allowed" : "Not allowed"}
          />
        </div>
      </div>
    </div>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between px-4 py-2.5">
      <span className="text-muted-foreground text-sm">{label}</span>
      <span className="text-sm font-medium">{value}</span>
    </div>
  );
}

function ProfilesTab({ userId }: { userId: number }) {
  const { data: profiles, isLoading } = useAdminUserProfiles(userId);

  if (isLoading)
    return (
      <div className="text-muted-foreground py-8 text-center text-sm">Loading profiles...</div>
    );

  if (!profiles || profiles.length === 0)
    return (
      <div className="surface-panel text-muted-foreground rounded-[1.6rem] py-10 text-center text-sm">
        No profiles found for this user.
      </div>
    );

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {profiles.map((profile: AdminUserProfile) => (
        <ProfileCard key={profile.id} profile={profile} />
      ))}
    </div>
  );
}

function ProfileCard({ profile }: { profile: AdminUserProfile }) {
  return (
    <div className="surface-panel flex items-center gap-3 rounded-[1.4rem] border-0 px-4 py-3">
      <UserCircle className="text-muted-foreground h-8 w-8" />
      <div>
        <div className="text-sm font-medium">{profile.name}</div>
        <div className="text-muted-foreground text-xs">{profile.id}</div>
      </div>
    </div>
  );
}

function WatchHistoryTab({ userId }: { userId: number }) {
  const {
    data: rows = [],
    isLoading,
    error,
  } = useAdminPlaybackHistory({
    userId,
    limit: 50,
  });

  if (isLoading)
    return (
      <div className="text-muted-foreground py-8 text-center text-sm">Loading watch history...</div>
    );

  if (error)
    return (
      <div className="text-destructive py-8 text-center text-sm">Failed to load watch history.</div>
    );

  if (rows.length === 0)
    return (
      <div className="surface-panel text-muted-foreground rounded-[1.6rem] py-10 text-center text-sm">
        No playback history for this user.
      </div>
    );

  return (
    <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Media</TableHead>
            <TableHead>Profile</TableHead>
            <TableHead>Method</TableHead>
            <TableHead>Watch Time</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Ended</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const title = row.media_title || row.media_item_id || `File #${row.media_file_id}`;
            return (
              <TableRow key={row.session_id}>
                <TableCell>
                  <div className="font-medium">{title}</div>
                  <div className="text-muted-foreground text-xs">{row.media_type || "unknown"}</div>
                </TableCell>
                <TableCell>{row.profile_name || row.profile_id}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{row.play_method}</Badge>
                </TableCell>
                <TableCell>
                  <div>{formatDuration(row.watched_seconds)}</div>
                  <div className="text-muted-foreground text-xs">
                    of {formatDuration(row.duration_seconds)}
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant={row.completed ? "default" : "outline"}>
                    {row.completed ? "Completed" : "Partial"}
                  </Badge>
                </TableCell>
                <TableCell>
                  <div>{formatDateTime(row.ended_at)}</div>
                  <div className="text-muted-foreground text-xs">
                    started {formatRelative(row.started_at)}
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}

function IPHistoryTab({ userId }: { userId: number }) {
  const { data: ips = [], isLoading } = useUserIPs(userId);

  if (isLoading)
    return (
      <div className="text-muted-foreground py-8 text-center text-sm">Loading IP history...</div>
    );

  if (ips.length === 0)
    return (
      <div className="surface-panel text-muted-foreground rounded-[1.6rem] py-10 text-center text-sm">
        No IP history found for this user.
      </div>
    );

  return (
    <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>IP Address</TableHead>
            <TableHead>First Seen</TableHead>
            <TableHead>Last Seen</TableHead>
            <TableHead className="text-right">Requests</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {ips.map((entry: UserIPEntry) => (
            <TableRow key={entry.client_ip}>
              <TableCell className="font-mono text-sm">{entry.client_ip}</TableCell>
              <TableCell>{formatDateTime(entry.first_seen)}</TableCell>
              <TableCell>{formatDateTime(entry.last_seen)}</TableCell>
              <TableCell className="text-right">{entry.request_count.toLocaleString()}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function EditUserForm({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const { data: libraries = [] } = useAdminLibraries();
  const [username, setUsername] = useState(user.username);
  const [email, setEmail] = useState(user.email);
  const [password, setPassword] = useState("");
  const [role, setRole] = useState(user.role);
  const [enabled, setEnabled] = useState(user.enabled);
  const [libraryIDs, setLibraryIDs] = useState<number[] | null>(user.library_ids);
  const [maxStreams, setMaxStreams] = useState(user.max_streams);
  const [maxTranscodes, setMaxTranscodes] = useState(user.max_transcodes);
  const [maxPlaybackQualityPreset, setMaxPlaybackQualityPreset] = useState<PlaybackQualityPreset>(
    playbackQualityPresetFromValue(user.max_playback_quality),
  );
  const [downloadAllowed, setDownloadAllowed] = useState(user.download_allowed);
  const [downloadTranscodeAllowed, setDownloadTranscodeAllowed] = useState(
    user.download_transcode_allowed,
  );
  const updateMutation = useUpdateUser();

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
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
                <Label>Password (leave blank to keep current)</Label>
                <Input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
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
        <Button type="submit" className="w-full" disabled={updateMutation.isPending}>
          {updateMutation.isPending ? "Saving..." : "Save"}
        </Button>
      </div>
    </form>
  );
}

function formatDuration(seconds: number | null) {
  if (!seconds || Number.isNaN(seconds)) return "0m";
  const rounded = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const secs = rounded % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${secs}s`;
  return `${secs}s`;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function formatDateTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

function formatRelative(value: string) {
  const date = new Date(value);
  const diff = Date.now() - date.getTime();
  if (Number.isNaN(date.getTime())) return value;
  const minutes = Math.max(0, Math.floor(diff / 60_000));
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}
