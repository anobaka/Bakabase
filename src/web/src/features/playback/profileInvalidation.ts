/** Same-window editors notify active playback controls; returning from another window also reloads. */
export const RESOURCE_PROFILES_CHANGED = "bakabase-resource-profiles-changed";
export const notifyResourceProfilesChanged = () =>
  window.dispatchEvent(new Event(RESOURCE_PROFILES_CHANGED));
