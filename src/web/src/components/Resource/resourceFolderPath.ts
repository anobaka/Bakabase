/** Keep the server's path syntax, regardless of the platform displaying the page. */
export const resourceFolderPath = (path: string, isFile?: boolean): string => {
  if (!isFile) return path;

  // A backslash is a valid character in a POSIX filename. Only Windows paths
  // treat it as a separator; neither representation is rewritten for display.
  const windows = /^[a-z]:[/\\]/i.test(path) || path.startsWith("\\\\");
  const separator = windows
    ? Math.max(path.lastIndexOf("/"), path.lastIndexOf("\\"))
    : path.lastIndexOf("/");

  if (separator < 0) return path;
  if (separator === 0 || (windows && separator === 2 && path[1] === ":")) {
    return path.slice(0, separator + 1);
  }

  return path.slice(0, separator);
};
