export function FieldGroup({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="surface-panel rounded-[1.5rem] border-0 p-4 sm:p-5">
      <div className="text-muted-foreground mb-3 text-xs font-semibold uppercase tracking-[0.22em]">
        {label}
      </div>
      <div className="divide-border divide-y">{children}</div>
    </div>
  );
}
