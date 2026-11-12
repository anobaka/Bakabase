/** Routes are relative to the React hash router; searches belong inside the hash. */
export function dependencyManagerPath(componentId?: string, searchQuery?: string): string {
  const query = new URLSearchParams({ tab: "dependency" });
  if (componentId) query.set("component", componentId);
  if (searchQuery) query.set("q", searchQuery);
  return `/configuration?${query}`;
}

/** The former standalone page remains a bookmark alias of the configuration tab. */
export function standaloneDependencyRedirect(search: string): string {
  const query = new URLSearchParams({ tab: "dependency" });
  for (const [key, value] of new URLSearchParams(search)) {
    if (key !== "tab") query.append(key, value);
  }
  return `/configuration?${query}`;
}
