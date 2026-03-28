import { useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router";
import { useAuth } from "@/hooks/useAuth";
import type { Profile, CreateProfileRequest } from "@/api/types";
import {
  useProfiles,
  useCreateProfile,
  useUpdateProfile,
  useDeleteProfile,
} from "@/hooks/queries/profiles";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Switch } from "@/components/ui/switch";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { Plus, Pencil, Trash2, Lock } from "lucide-react";
export default function Profiles() {
  const { data: profiles = [], isLoading } = useProfiles();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingProfile, setEditingProfile] = useState<Profile | null>(null);
  const [pinProfile, setPinProfile] = useState<Profile | null>(null);
  const [confirmDeleteProfile, setConfirmDeleteProfile] = useState<Profile | null>(null);
  const { selectProfile, verifyProfilePin, logout } = useAuth();
  const navigate = useNavigate();
  const deleteMutation = useDeleteProfile();

  useDocumentTitle("Profiles");

  function handleSelect(p: Profile) {
    if (p.has_pin) {
      setPinProfile(p);
      return;
    }
    selectProfile(p);
    navigate("/");
  }

  function handleDelete(p: Profile) {
    setConfirmDeleteProfile(p);
  }

  if (isLoading) return <div className="page-shell p-8">Loading profiles...</div>;

  return (
    <div className="auth-shell flex-col gap-8">
      <div className="relative z-10 text-center">
        <h1 className="page-title text-[clamp(2.2rem,7vw,4.5rem)]">Who&apos;s watching?</h1>
      </div>
      <div className="relative z-10 flex max-w-5xl flex-wrap justify-center gap-5">
        {profiles.map((p) => (
          <div key={p.id} className="group relative">
            <button
              onClick={() => handleSelect(p)}
              className="surface-panel hover:border-primary flex w-[148px] flex-col items-center gap-3 rounded-[1.75rem] p-5 transition-all duration-150 hover:-translate-y-1"
            >
              <Avatar className="ring-border group-hover:ring-primary/30 h-20 w-20 ring-2 transition-colors">
                <AvatarFallback className="bg-surface text-primary text-2xl font-bold">
                  {p.name.charAt(0).toUpperCase()}
                </AvatarFallback>
              </Avatar>
              <span className="text-sm font-medium">{p.name}</span>
              {p.has_pin && <Lock className="text-muted-foreground h-3 w-3" />}
              {p.is_child && <span className="text-muted-foreground text-xs">Kids</span>}
            </button>
            <div className="absolute top-1 right-1 flex gap-1 opacity-0 group-hover:opacity-100">
              <Button
                variant="ghost"
                size="icon"
                className="h-6 w-6"
                onClick={() => {
                  setEditingProfile(p);
                  setDialogOpen(true);
                }}
              >
                <Pencil className="h-3 w-3" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                className="h-6 w-6"
                onClick={() => handleDelete(p)}
              >
                <Trash2 className="h-3 w-3" />
              </Button>
            </div>
          </div>
        ))}
        <Dialog
          open={dialogOpen}
          onOpenChange={(open) => {
            setDialogOpen(open);
            if (!open) setEditingProfile(null);
          }}
        >
          <DialogTrigger asChild>
            <button className="surface-panel hover:border-primary flex w-[148px] flex-col items-center gap-3 rounded-[1.75rem] p-5 transition-all duration-150 hover:-translate-y-1">
              <div className="border-border flex h-20 w-20 items-center justify-center rounded-full border-2 border-dashed">
                <Plus className="text-muted-foreground h-8 w-8" />
              </div>
              <span className="text-muted-foreground text-sm">Add Profile</span>
            </button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>{editingProfile ? "Edit Profile" : "New Profile"}</DialogTitle>
            </DialogHeader>
            <ProfileForm
              profile={editingProfile}
              onClose={() => {
                setDialogOpen(false);
                setEditingProfile(null);
              }}
            />
          </DialogContent>
        </Dialog>
      </div>
      <Button variant="outline" size="sm" onClick={logout} className="relative z-10 mt-2">
        Sign out
      </Button>
      <PinDialog
        profile={pinProfile}
        onClose={() => setPinProfile(null)}
        onVerified={(p, token) => {
          setPinProfile(null);
          selectProfile(p, token);
          navigate("/");
        }}
        verifyPin={verifyProfilePin}
      />
      <ConfirmDialog
        open={confirmDeleteProfile !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteProfile(null);
        }}
        title="Delete profile"
        description={`Delete profile "${confirmDeleteProfile?.name}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteProfile) deleteMutation.mutate(confirmDeleteProfile.id);
          setConfirmDeleteProfile(null);
        }}
      />
    </div>
  );
}

function PinDialog({
  profile,
  onClose,
  onVerified,
  verifyPin,
}: {
  profile: Profile | null;
  onClose: () => void;
  onVerified: (profile: Profile, token: string) => void;
  verifyPin: (
    profileId: string,
    pin: string,
  ) => Promise<{ valid: boolean; profile_token?: string }>;
}) {
  const [pin, setPin] = useState("");
  const [error, setError] = useState("");
  const [verifying, setVerifying] = useState(false);

  function handleClose() {
    setPin("");
    setError("");
    onClose();
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!profile || !pin) return;
    setError("");
    setVerifying(true);
    try {
      const res = await verifyPin(profile.id, pin);
      if (res.valid && res.profile_token) {
        setPin("");
        setError("");
        onVerified(profile, res.profile_token);
      } else {
        setError("Incorrect PIN");
        setPin("");
      }
    } catch {
      setError("Verification failed");
    } finally {
      setVerifying(false);
    }
  }

  return (
    <Dialog
      open={!!profile}
      onOpenChange={(open) => {
        if (!open) handleClose();
      }}
    >
      <DialogContent className="sm:max-w-xs">
        <DialogHeader>
          <DialogTitle>Enter PIN for {profile?.name}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <Input
            type="password"
            inputMode="numeric"
            maxLength={4}
            placeholder="Enter 4-digit PIN"
            value={pin}
            onChange={(e) => setPin(e.target.value)}
            autoFocus
          />
          {error && <p className="text-destructive text-sm">{error}</p>}
          <div className="flex gap-2">
            <Button type="button" variant="outline" className="flex-1" onClick={handleClose}>
              Cancel
            </Button>
            <Button type="submit" className="flex-1" disabled={verifying || pin.length === 0}>
              {verifying ? "Verifying..." : "Confirm"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function ProfileForm({ profile, onClose }: { profile: Profile | null; onClose: () => void }) {
  const [name, setName] = useState(profile?.name ?? "");
  const [pin, setPin] = useState("");
  const [isChild, setIsChild] = useState(profile?.is_child ?? false);
  const createMutation = useCreateProfile();
  const updateMutation = useUpdateProfile();
  const isPending = createMutation.isPending || updateMutation.isPending;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const body: CreateProfileRequest = { name, is_child: isChild };
    if (pin) body.pin = pin;

    if (profile) {
      updateMutation.mutate({ id: profile.id, body }, { onSuccess: onClose });
    } else {
      createMutation.mutate(body, { onSuccess: onClose });
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="profile-name">Name</Label>
        <Input id="profile-name" value={name} onChange={(e) => setName(e.target.value)} required />
      </div>
      <div className="space-y-2">
        <Label htmlFor="profile-pin">PIN (optional)</Label>
        <Input
          id="profile-pin"
          value={pin}
          onChange={(e) => setPin(e.target.value)}
          maxLength={4}
          placeholder="4 digits"
        />
      </div>
      <div className="flex items-center gap-2">
        <Switch checked={isChild} onCheckedChange={setIsChild} id="is-child" />
        <Label htmlFor="is-child">Kids profile</Label>
      </div>
      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? "Saving..." : "Save"}
      </Button>
    </form>
  );
}
