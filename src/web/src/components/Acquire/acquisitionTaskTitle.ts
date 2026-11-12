export function acquisitionTaskTitle(id: number, task?: { title?: string | null; resourceName?: string | null }) {
  const name = task?.title?.trim() || task?.resourceName?.trim();
  return name ? `#${id} ${name}` : `#${id}`;
}
