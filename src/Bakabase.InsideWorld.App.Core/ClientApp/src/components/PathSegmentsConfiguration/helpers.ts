import type PscProperty from './models/PscProperty';
import { PscPropertyType } from './models/PscPropertyType';
import { execAll } from '@/components/utils';
import { ResourceProperty } from '@/sdk/constants';


export function getResultFromExecAll(regex: RegExp | string, str: string): {
  groups?: string[];
  text?: string;
  // Available when it's a text result
  index?: number;
} | null {
  const matches = execAll(regex, str, 50);
  console.log(matches);
  if (matches) {
    // 如果有groups，优先使用groups的结果
    // 否则使用match[0]
    const capturedMap: Record<string, any> = {};
    let firstMatch: string | undefined;
    let index: number | undefined;
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
        index = m.index;
      }
    }
    const values = Object.keys(capturedMap);
    const result: {
      groups?: string[];
      text?: string;
      index?: number;
    } = {};
    if (values.length == 0) {
      if (firstMatch == undefined) {
        return null;
      }
      result.text = firstMatch;
      result.index = index;
    } else {
      result.groups = values;
    }
    return result;
  }
  return null;
}
