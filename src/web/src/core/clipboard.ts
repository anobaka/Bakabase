/**
 * Copies text to the clipboard, falling back to a hidden textarea when the async Clipboard API is
 * unavailable or refuses. The async API only exists in a secure context, and a browser reaching the
 * server over plain HTTP on the LAN is not one — there `navigator.clipboard` is simply undefined.
 *
 * Rejects when neither route worked, so callers can tell the user instead of claiming success.
 */
export async function copyTextToClipboard(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const textarea = document.createElement("textarea");

    textarea.value = text;
    textarea.style.position = "fixed";
    textarea.style.opacity = "0";
    document.body.appendChild(textarea);
    textarea.select();
    try {
      if (!document.execCommand("copy")) throw new Error("Copy failed");
    } finally {
      textarea.remove();
    }
  }
}
