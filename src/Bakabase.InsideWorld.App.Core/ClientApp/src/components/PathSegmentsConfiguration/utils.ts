import { execAll } from '@/components/utils';

export function getResultFromExecAll(regex: RegExp | string, str: string): {
  groups?: string[];
  text?: string;
} | null {
  const matches = execAll(regex, str);
  if (matches) {
    // 如果有groups，优先使用groups的结果
    // 否则使用match[0]
    const valueMap: Record<string, any> = {};
    matches.forEach((m) => {
      if (m.groups) {
        Object.keys(m.groups)
          .forEach(key => {
            valueMap[m.groups![key]] = true;
          });
      }
    });
    const values = Object.keys(valueMap);
    const result: {
      groups?: string[];
      text?: string;
    } = {};
    if (values.length == 0) {
      result.text = matches.map(m => m[0]).filter(a => a)[0];
    } else {
      result.groups = values;
    }
    return result;
  }
  return null;
}
