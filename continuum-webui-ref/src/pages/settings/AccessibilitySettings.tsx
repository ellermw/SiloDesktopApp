import { SettingsGroup } from "@/components/settings/SettingsGroup";
import { Button } from "@/components/ui/button";
import { useTheme } from "@/hooks/useTheme";

export default function AccessibilitySettings() {
  const { textScale, setTextScale, textWeight, setTextWeight, highContrast, setHighContrast } =
    useTheme();

  return (
    <div className="space-y-6">
      <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">Accessibility</h2>

      <SettingsGroup
        title="Readability"
        description="Increase text size, strengthen type weight, and raise contrast for clearer reading."
      >
        <div className="space-y-4">
          <div className="space-y-2">
            <p className="text-sm font-medium">Text size</p>
            <div className="flex flex-wrap gap-2">
              {[
                { value: "default" as const, label: "Default" },
                { value: "large" as const, label: "Large" },
                { value: "x-large" as const, label: "Extra Large" },
              ].map((option) => (
                <Button
                  key={option.value}
                  variant={textScale === option.value ? "default" : "outline"}
                  size="sm"
                  onClick={() => setTextScale(option.value)}
                >
                  {option.label}
                </Button>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <p className="text-sm font-medium">Text weight</p>
            <div className="flex flex-wrap gap-2">
              {[
                { value: "default" as const, label: "Default" },
                { value: "strong" as const, label: "Bolder" },
              ].map((option) => (
                <Button
                  key={option.value}
                  variant={textWeight === option.value ? "default" : "outline"}
                  size="sm"
                  onClick={() => setTextWeight(option.value)}
                >
                  {option.label}
                </Button>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <p className="text-sm font-medium">Contrast</p>
            <div className="flex flex-wrap gap-2">
              <Button
                variant={!highContrast ? "default" : "outline"}
                size="sm"
                onClick={() => setHighContrast(false)}
              >
                Standard
              </Button>
              <Button
                variant={highContrast ? "default" : "outline"}
                size="sm"
                onClick={() => setHighContrast(true)}
              >
                High Contrast
              </Button>
            </div>
          </div>
        </div>
      </SettingsGroup>
    </div>
  );
}
