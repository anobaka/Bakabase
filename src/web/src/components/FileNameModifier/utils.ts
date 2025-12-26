// 路径截断：保留文件名，截断中间目录
export function truncatePath(path: string, maxLength = 40): string {
  if (path.length <= maxLength) return path;

  const lastSep = Math.max(path.lastIndexOf("/"), path.lastIndexOf("\\"));
  if (lastSep === -1) {
    // 没有分隔符，直接截断
    return "..." + path.slice(-(maxLength - 3));
  }

  const fileName = path.slice(lastSep + 1);
  const dir = path.slice(0, lastSep);

  if (fileName.length >= maxLength - 4) {
    // 文件名本身就很长
    return "..." + fileName.slice(-(maxLength - 3));
  }

  const remainingLength = maxLength - fileName.length - 4; // 4 = ".../"
  if (remainingLength <= 0) {
    return ".../" + fileName;
  }

  return dir.slice(0, remainingLength) + ".../" + fileName;
}

// 公共前缀检测：找到所有路径的公共目录前缀
export function detectCommonPrefix(paths: string[]): string {
  if (paths.length === 0) return "";
  if (paths.length === 1) {
    const lastSep = Math.max(paths[0].lastIndexOf("/"), paths[0].lastIndexOf("\\"));
    return lastSep >= 0 ? paths[0].substring(0, lastSep + 1) : "";
  }

  // 将所有路径转换为目录部分
  const dirs = paths.map((p) => {
    const lastSep = Math.max(p.lastIndexOf("/"), p.lastIndexOf("\\"));
    return lastSep >= 0 ? p.substring(0, lastSep + 1) : "";
  });

  // 找到公共前缀
  let prefix = dirs[0];
  for (let i = 1; i < dirs.length; i++) {
    while (!dirs[i].startsWith(prefix) && prefix.length > 0) {
      // 退回到上一个目录分隔符
      const lastSep = Math.max(
        prefix.lastIndexOf("/", prefix.length - 2),
        prefix.lastIndexOf("\\", prefix.length - 2)
      );
      prefix = lastSep >= 0 ? prefix.substring(0, lastSep + 1) : "";
    }
    if (prefix.length === 0) break;
  }

  return prefix;
}

// 差异高亮：计算两个字符串的公共前缀和后缀
export interface DiffParts {
  commonPrefix: string;
  commonSuffix: string;
  removedPart: string;
  addedPart: string;
}

export function computeDiff(original: string, modified: string): DiffParts {
  if (original === modified) {
    return {
      commonPrefix: original,
      commonSuffix: "",
      removedPart: "",
      addedPart: "",
    };
  }

  // 找公共前缀
  let prefixLen = 0;
  const minLen = Math.min(original.length, modified.length);
  while (prefixLen < minLen && original[prefixLen] === modified[prefixLen]) {
    prefixLen++;
  }

  // 找公共后缀
  let suffixLen = 0;
  while (
    suffixLen < original.length - prefixLen &&
    suffixLen < modified.length - prefixLen &&
    original[original.length - 1 - suffixLen] === modified[modified.length - 1 - suffixLen]
  ) {
    suffixLen++;
  }

  return {
    commonPrefix: original.substring(0, prefixLen),
    commonSuffix: original.substring(original.length - suffixLen),
    removedPart: original.substring(prefixLen, original.length - suffixLen),
    addedPart: modified.substring(prefixLen, modified.length - suffixLen),
  };
}
