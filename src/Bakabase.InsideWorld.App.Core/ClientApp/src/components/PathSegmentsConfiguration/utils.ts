import { execAll } from '@/components/utils';

export function getResultFromExecAll(regex: RegExp | string, str: string): {
  groups?: string[];
  text?: string;
} | null {
  const matches = execAll(regex, str, 50);
  console.log(matches);
  if (matches) {
    // 如果有groups，优先使用groups的结果
    // 否则使用match[0]
    const capturedMap: Record<string, any> = {};
    let firstMatch: string | undefined;
    for (const m of matches) {
      if (m.groups) {
        Object.keys(m.groups)
          .forEach(key => {
            capturedMap[m.groups![key]] = true;
          });
      }
      for (let i = 1; i < m.length; i++) {
        capturedMap[m[i]] = true;
      }
      if (firstMatch == undefined) {
        firstMatch = m[0];
      }
    }
    const values = Object.keys(capturedMap);
    const result: {
      groups?: string[];
      text?: string;
    } = {};
    if (values.length == 0) {
      if (firstMatch == undefined) {
        return null;
      }
      result.text = firstMatch;
    } else {
      result.groups = values;
    }
    return result;
  }
  return null;
}
