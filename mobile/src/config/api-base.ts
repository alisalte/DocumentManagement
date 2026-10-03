/** Runtime server address, set after the user saves one on the device. */
let override: string | null = null;

export function apiBaseOverride(): string | null {
  return override;
}

export function setApiBaseOverride(url: string | null): void {
  override = url;
}
