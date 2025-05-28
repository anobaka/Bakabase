/**
 * Created by S on 2018/6/21.
 */

import React, { useEffect, useRef } from 'react';
import i18n from 'i18next';
import ReactDOM from 'react-dom/client';
import isUncPath from 'is-unc-path';
import hoistNonReactStatic from 'hoist-non-react-statics';
import chalk from 'chalk';
import { Time } from '@internationalized/date';
import type { Duration } from 'dayjs/plugin/duration';
import dayjs from 'dayjs';
import { reservedResourceFileTypes, reservedResourceProperties, UiTheme } from '@/sdk/constants';
import store from '@/store';
import BusinessConstants from '@/components/BusinessConstants';
import type { BakabaseInfrastructuresComponentsConfigurationsAppAppOptions } from '@/sdk/Api';

export const convertDurationToTime = (duration: Duration): Time => {
  return new Time(duration.hours(), duration.minutes(), duration.seconds(), duration.milliseconds());
};

export const convertTimeToDuration = (time: Time): Duration => {
  return dayjs.duration({
    hours: time.hour,
    minutes: time.minute,
    seconds: time.second,
    milliseconds: time.millisecond,
  });
};

export default { // 工具集
  formatDate(date) { // 标准化时间格式
    if (date === null) return '';
    date = new Date(date);

    function fillZero(num) {
      num = +num;
      return num <= 9 ? `0${num}` : `${num}`;
    }

    const [Y, M, D] = [
      date.getFullYear(),
      fillZero(date.getMonth() + 1),
      fillZero(date.getDate()),
    ];

    const [h, m, s] = [
      fillZero(date.getHours()),
      fillZero(date.getMinutes()),
      fillZero(date.getSeconds()),
    ];
    return `${Y}-${M}-${D} ${h}:${m}:${s}`;
  },
  date2Str(dt) {
    dt = new Date(dt);
    const [month, date, day] = [dt.getMonth() + 1, dt.getDate(), dt.getDay()];
    return `${month < 10 ? `0${month}` : month}-${date < 10 ? `0${date}` : date}/星期${'日一二三四五六'[day]}`;
  },
};

export const groupBy = (xs, key) => {
  return xs.reduce((rv, x) => {
    (rv[x[key]] = rv[x[key]] || []).push(x);
    return rv;
  }, {});
};

/* 重置对象的属性值为初始值 */
export const resetObj = (obj) => {
  if (obj == undefined) {
    return undefined;
  }
  Object.keys(obj)
    .map((prop) => {
      if (typeof obj[prop] === 'string') {
        obj[prop] = '';
      } else if (typeof obj[prop] === 'number') {
        obj[prop] = '';
      } else if (typeof obj[prop] === 'boolean') {
        obj[prop] = false;
      } else if (typeof obj[prop] === 'object') {
        if (obj[prop] instanceof Array) {
          obj[prop] = [];
        } else {
          obj[prop] = resetObj(obj[prop]);
        }
      }
    });
  return obj;
};

export const deepClone = function (item) {
  if (!item) {
    return item;
  } // null, undefined values check

  const types = [Number, String, Boolean];
  let result;

  // normalizing primitives if someone did new String('aaa'), or new Number('444');
  types.forEach((type) => {
    if (item instanceof type) {
      result = type(item);
    }
  });

  if (typeof result === 'undefined') {
    if (Object.prototype.toString.call(item) === '[object Array]') {
      result = [];
      item.forEach((child, index, array) => {
        result[index] = deepClone(child);
      });
    } else if (typeof item === 'object') {
      // testing that this is DOM
      if (item.nodeType && typeof item.cloneNode === 'function') {
        result = item.cloneNode(true);
      } else if (!item.prototype) { // check that this is a literal
        if (item instanceof Date) {
          result = new Date(item);
        } else {
          // it is an object literal
          result = {};
          for (const i in item) {
            result[i] = deepClone(item[i]);
          }
        }
      } else {
        // depends on what you would like here,
        // just keep the reference, or create new object
        if (false && item.constructor) {
          // would not advice to do that, reason? Read below
          result = new item.constructor();
        } else {
          result = item;
        }
      }
    } else {
      result = item;
    }
  }

  return result;
};

export function camelize(str) {
  return str.replace(/(?:^\w|[A-Z]|\b\w)/g, (word, index) => {
    return index === 0 ? word.toLowerCase() : word.toUpperCase();
  })
    .replace(/\s+/g, '');
}

export function findLongestPrefix(list) {
  const prefix = list[0];
  let prefixLen = prefix.length;
  for (let i = 1; i < list.length && prefixLen > 0; i++) {
    const word = list[i];
    // The next line assumes 1st char of word and prefix always match.
    // Initialize matchLen to -1 to test entire word.
    let matchLen = 0;
    const maxMatchLen = Math.min(word.length, prefixLen);
    while (++matchLen < maxMatchLen) {
      if (word.charAt(matchLen) != prefix.charAt(matchLen)) {
        break;
      }
    }
    prefixLen = matchLen;
  }
  return prefix.substring(0, prefixLen);
}

export function usePrevious(value) {
  // The ref object is a generic container whose current property is mutable ...
  // ... and can hold any value, similar to an instance property on a class
  const ref = useRef();
  // Store current value in ref
  useEffect(() => {
    ref.current = value;
  }, [value]); // Only re-run if value changes
  // Return previous value (happens before update in useEffect above)
  return ref.current;
}

export function bytesToSize(bytes) {
  const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
  if (bytes == 0) return '0 Byte';
  const i = Math.floor(Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${Math.round(bytes / Math.pow(1024, i))} ${sizes[i]}`;
}

export function useTraceUpdate(props, logPrefix) {
  const prev = useRef(props);
  useEffect(() => {
    const changedProps = Object.entries(props)
      .reduce((ps, [k, v]) => {
        if (prev.current[k] !== v) {
          ps[k] = [prev.current[k], v];
        }
        return ps;
      }, {});
    if (Object.keys(changedProps).length > 0) {
      console.log(`${logPrefix}Changed props:`, changedProps);
    }
    prev.current = props;
  });
}

export function shadeColor(color, percent, opacity) {
  if (color == undefined) {
    return;
  }

  // console.log(color, percent);

  let R = parseInt(color.substring(1, 3), 16);
  let G = parseInt(color.substring(3, 5), 16);
  let B = parseInt(color.substring(5, 7), 16);

  R = Math.floor(R * (100 + percent) / 100);
  G = Math.floor(G * (100 + percent) / 100);
  B = Math.floor(B * (100 + percent) / 100);

  R = (R < 255) ? R : 255;
  G = (G < 255) ? G : 255;
  B = (B < 255) ? B : 255;

  return `rgba(${R}, ${G}, ${B}, ${opacity})`;
}

export function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Format bytes as human-readable text.
 *
 * @param bytes Number of bytes.
 * @param si True to use metric (SI) units, aka powers of 1000. False to use
 *           binary (IEC), aka powers of 1024.
 * @param dp Number of decimal places to display.
 *
 * @return Formatted string.
 */
export function humanFileSize(bytes, si = false, dp = 1) {
  const thresh = si ? 1000 : 1024;

  if (Math.abs(bytes) < thresh) {
    return `${bytes} B`;
  }

  const units = si
    ? ['kB', 'MB', 'GB', 'TB', 'PB', 'EB', 'ZB', 'YB']
    : ['KiB', 'MiB', 'GiB', 'TiB', 'PiB', 'EiB', 'ZiB', 'YiB'];
  let u = -1;
  const r = 10 ** dp;

  do {
    bytes /= thresh;
    ++u;
  } while (Math.round(Math.abs(bytes) * r) / r >= thresh && u < units.length - 1);


  return `${bytes.toFixed(dp)} ${units[u]}`;
}

const layerRegex = '[^\\/]+';
const pathSeparatorInRegexStr = '\\/';

export function buildLayerBasedPathRegexString(layer, extensions?: string[]): string {
  let reg = '^';
  for (let i = 0; i < layer; i++) {
    reg += layerRegex;
    if (i < layer - 1) {
      reg += pathSeparatorInRegexStr;
    } else {
      if (extensions) {
        if (extensions.length > 0) {
          reg += `\\.(?:${extensions.map(e => e.replace(/^\./, '')
            .replaceAll('.', '\\.'))
            .join('|')})`;
          break;
        }
      }
    }
  }
  reg += '$';
  return reg;
}

export function prependLayersToLayerBasedPathRegexString(regStr, layerCount): string {
  if (layerCount == 0) {
    return regStr;
  } else {
    if (layerCount > 0) {
      const r = buildLayerBasedPathRegexString(layerCount);
      const subR = r.substring(1, r.length - 1);
      return `^${subR}${pathSeparatorInRegexStr}${regStr?.substring(1, regStr.length - 1) ?? ''}$`;
    } else {
      let core = regStr?.substring(1, regStr.length - 1) ?? '';
      for (let i = layerCount; i < 0; i++) {
        if (core.startsWith(layerRegex)) {
          core = core.substring(layerRegex.length + pathSeparatorInRegexStr.length);
        } else {
          // invalid
          return regStr;
        }
      }
      return `^${core}$`;
    }
  }
}

//
// export function appendLayersToLayerBasedPathRegexString(regStr, layerCount): string {
//   if (layerCount == 0) {
//     return regStr;
//   } else {
//     if (layerCount > 0) {
//       const r = buildLayerBasedPathRegexString(layerCount);
//       const subR = r.substring(1, r.length - 1);
//       return `^${regStr?.substring(1, regStr.length - 1) ?? ''}${subR}$`;
//     } else {
//       let core = regStr?.substring(1, regStr.length - 1) ?? '';
//       for (let i = layerCount; i < 0; i++) {
//         if (core.endsWith(layerRegex)) {
//           core = core.substring(0, core.length - layerRegex.length);
//         } else {
//           // invalid
//           return regStr;
//         }
//       }
//       return `^${core}$`;
//     }
//   }
// }

export function parseLayerCountFromLayerBasedPathRegexString(regStr, strict: boolean): number {
  if (regStr == undefined) {
    return 0;
  }
  let coreRegStr = regStr.replace(/^\^/, '')
    .replace(/\$$/, '');
  let count = 0;
  while (coreRegStr.length > 0) {
    if (coreRegStr.startsWith(layerRegex)) {
      coreRegStr = coreRegStr.substring(layerRegex.length + pathSeparatorInRegexStr.length);
      count += 1;
    } else {
      if (strict) {
        return 0;
      } else {
        return count;
      }
    }
  }
  return count;
}

export function parseExtensionsFromPathRegex(regStr: string): string[] {
  if (regStr == undefined) {
    return [];
  }
  let coreRegStr = regStr.replace(/^\^/, '')
    .replace(/\$$/, '');
  console.log(coreRegStr);
  if (coreRegStr[coreRegStr.length - 1] == ')') {
    const startIdx = coreRegStr.lastIndexOf('(');
    // \.(ext1|ext2|...)
    if (startIdx > 1) {
      if (coreRegStr.substring(startIdx - 2, startIdx) == '\\.') {
        const value = coreRegStr.substring(startIdx, coreRegStr.length);
        return value.split('|')
          .map(a => `.${a}`);
      }
    }
  }
  return [];
}

export function getFileNameWithoutExtension(path?: string): string | undefined {
  if (path && path.length > 0) {
    const segments = path!.split('.');
    const filename = segments[Math.max(0, segments.length - 2)];
    const segments2 = filename.split('!');
    return segments2[segments2.length - 1];
  } else {
    return undefined;
  }
}

export function uuidv4(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    let r = Math.random() * 16 | 0,
      v = c == 'x' ? r : (r & 0x3 | 0x8);
    return v.toString(16);
  });
}

export function findCapturingGroupsInRegex(regex: string): string[] {
  return regex?.match(/\(\?<(\w+)>/g)
    ?.map(match => match.slice(3, -1)) || [];
}

export function extractEnhancerTargetDescription(target: string) {
  const segs = target.split(':');
  const type = segs[0];
  const key = segs[1];
  let typeName;
  let keyName;
  if (type == 'p') {
    typeName = 'Property';
    const reserved = reservedResourceProperties.find((x) => x.label.toLowerCase() == key.toLowerCase());
    keyName = reserved ? i18n.t(reserved.label) : key;
  } else if (type == 'f') {
    typeName = 'File';
    const reserved = reservedResourceFileTypes.find((x) => x.label.toLowerCase() == key.toLowerCase());
    keyName = reserved ? i18n.t(reserved.label) : key;
  } else {
    typeName = 'Unknown';
  }
  return {
    type: i18n.t(typeName),
    key: keyName,
  };
}

export function createPortalOfComponent(Component: React.ComponentType<any>, props: any) {
  const key = uuidv4();
  const node = document.createElement('div');
  document.body.appendChild(node);

  const root = ReactDOM.createRoot(node);

  // console.log(19282, node, props, Component);

  const unmount = () => {
    console.log('Unmounting', key);
    // console.trace(19282);
    setTimeout(() => {
      root.unmount();
      node.remove();
    }, 1);
  };

  console.log('Mounting', key);

  root.render(
    <store.Provider>
      <Component
        {...props}
        afterClose={() => {
          if (props.afterClose) {
            props.afterClose();
          }
          unmount();
        }}
      />
    </store.Provider>,
  );

  return {
    key,
    close: unmount,
  };
}

export function wrapWithStaticShowMethod<T extends {}>(Component: React.ComponentType<T>, props: T) {
  class Enhance extends React.Component {
    static show(props: T) {
      return createPortalOfComponent(Component, props);
    }
  }

  hoistNonReactStatic(Enhance, Component);
  return Enhance;
}

export function standardizePath(path?: string) {
  if (path == undefined) {
    return;
  }
  const np = path.replace(/\\/g, BusinessConstants.pathSeparator);
  if (isUncPath(path) && !np.startsWith(BusinessConstants.uncPathPrefix)) {
    return `${BusinessConstants.uncPathPrefix}${np}`;
  }
  return np;
}

export function buildLogger(key: string) {
  return ((...args) => {
    return Function.prototype.bind.call(console.log, console, chalk.blue(`[${key}]`));
  })();
}

export function createSelection(field: any, start: number, end: number) {
  if (field.createTextRange) {
    const selRange = field.createTextRange();
    selRange.collapse(true);
    selRange.moveStart('character', start);
    selRange.moveEnd('character', end);
    selRange.select();
    field.focus();
  } else if (field.setSelectionRange) {
    field.focus();
    field.setSelectionRange(start, end);
  } else if (typeof field.selectionStart !== 'undefined') {
    field.selectionStart = start;
    field.selectionEnd = end;
    field.focus();
  }
}

export function isString(value: any): boolean {
  return typeof value === 'string' || value instanceof String;
}

export function forceFocus(nodeOrQuery: Element | string | null | undefined) {
  let dom;
  if (isString(nodeOrQuery)) {
    const query = nodeOrQuery as string;
    console.log(query);
    dom = document.querySelector(query);
  } else {
    dom = nodeOrQuery;
  }
  // console.log(dom);
  if (dom) {
    dom.setAttribute('tabindex', '0');
    dom.focus();
  }
}


export function captureVideoFrame(video, format, quality) {
  // console.trace();

  if (typeof video === 'string') {
    video = document.getElementById(video);
  }

  // console.log(video);

  format = format || 'jpeg';
  quality = quality || 0.92;

  if (!video || (format !== 'png' && format !== 'jpeg')) {
    return false;
  }

  const canvas = document.createElement('canvas');

  canvas.width = video.videoWidth;
  canvas.height = video.videoHeight;

  // console.log(typeof canvas.getContext('2d'), canvas.getContext('2d'), video);
  canvas.getContext('2d')!.drawImage(video, 0, 0);

  const dataUri = canvas.toDataURL(`image/${format}`, quality);
  const data = dataUri.split(',')[1];
  const mimeType = dataUri.split(';')[0].slice(5);

  const bytes = window.atob(data);
  const buf = new ArrayBuffer(bytes.length);
  const arr = new Uint8Array(buf);

  for (let i = 0; i < bytes.length; i++) {
    arr[i] = bytes.charCodeAt(i);
  }

  const blob = new Blob([arr], { type: mimeType });
  return {
    blob,
    dataUri,
    format,
  };
}

export function getValue(object: any, key: string, separator: string = '.') {
  const segments = key.split(separator);
  while (segments.length > 0) {
    if (object == undefined) {
      return object;
    }
    // console.log(obj, segments);
    object = object[segments.splice(0, 1)[0]];
  }
  return object;
}

export function setValue(object: any, key: string, value: any, separator: string = '.') {
  const segments = key.split(separator);
  while (true) {
    const first = segments.splice(0, 1)[0];
    if (segments.length > 0) {
      if (!(first in object)) {
        object[first] = {};
      }
      object = object[first];
    } else {
      object[first] = value;
      break;
    }
  }
}

export function equalsOrIsChildOf(child: HTMLElement | null, parent: HTMLElement | null) {
  if (!child || !parent) {
    return false;
  }
  let p: HTMLElement | null = child;
  while (p) {
    if (p == parent) {
      return true;
    }
    p = p.parentElement;
  }
  return false;
}

export function execAll(regex: RegExp | string, str: string, maxCount: number): IterableIterator<RegExpMatchArray> | null {
  const newReg = new RegExp(regex, 'g');
  return str.matchAll(newReg);
}

export function splitPathIntoSegments(path: string): string[] {
  const sp = standardizePath(path)!;
  const segments = sp.split(BusinessConstants.pathSeparator).filter(a => a?.length > 0);
  if (sp.startsWith(BusinessConstants.uncPathPrefix)) {
    segments[0] = `${BusinessConstants.uncPathPrefix}${segments[0]}`;
  }
  return segments;
}

export function splitStringWithEscapeChar(str: string, separator: string, escapeChar: string): string[] | null {
  if (str.length === 0) {
    return null;
  }

  const result: string[] = [];
  let idx = 0;
  while (idx <= str.length) {
    let nextIdx = idx;
    while (true) {
      nextIdx = str.indexOf(separator, nextIdx);
      if (nextIdx > 0) {
        if (str[nextIdx - 1] === escapeChar) {
          nextIdx++;
          continue;
        }
      }
      break;
    }

    if (nextIdx === -1) {
      result.push(str.substring(idx));
      break;
    }

    result.push(str.substring(idx, nextIdx));
    idx = nextIdx + 1;
  }

  return result.map(r => r.replaceAll(`${escapeChar}${separator}`, separator));
}

export function splitStringWithEscapeCharNested(str: string, highLevelSeparator: string, lowLevelSeparator: string, escapeChar: string): string[][] | null {
  const lowLevelStrings = splitStringWithEscapeChar(str, highLevelSeparator, escapeChar);
  if (lowLevelStrings === null) {
    return null;
  }

  return lowLevelStrings.map(x => splitStringWithEscapeChar(x, lowLevelSeparator, escapeChar) ?? []);
}

export function joinWithEscapeChar(data: (string | null | undefined)[], separator: string, escapeChar: string): string {
  return data
    .map(d => d?.replace(new RegExp(separator, 'g'), `${escapeChar}${separator}`))
    // .filter(x => (ignoreNullOrEmpty ? true : (x != undefined && x.length > 0)))
    .join(separator);
}

export const getUiTheme = (appOptions?: BakabaseInfrastructuresComponentsConfigurationsAppAppOptions) => {
  let uiTheme: UiTheme = appOptions?.uiTheme as number;
  if (uiTheme == UiTheme.FollowSystem) {
    if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
      uiTheme = UiTheme.Dark;
    } else {
      uiTheme = UiTheme.Light;
    }
  }
  return uiTheme;
};

export const buildUntitledLabel = (translation: string, existed?: (string | undefined)[]): string => {
  let i = 1;
  while (true) {
    const label = `${translation} ${i}`;
    if (!existed || existed.length == 0 || !existed.includes(label)) {
      return label;
    }
    i++;
  }
};

export function adjustAlpha(color: string, alphaAdjustment: number): string {
  if (color.startsWith('#')) {
    return hexToRGBAWithAlpha(color, alphaAdjustment);
  } else if (color.startsWith('rgb')) {
    return adjustRGBAWithAlpha(color, alphaAdjustment);
  } else if (color.startsWith('hsl')) {
    return adjustHSLAWithAlpha(color, alphaAdjustment);
  } else {
    throw new Error('Unsupported color format');
  }
}

function hexToRGBAWithAlpha(hex: string, alphaAdjustment: number): string {
  hex = hex.replace(/^#/, '');
  if (hex.length === 3) {
    hex = hex.split('').map(char => char + char).join('');
  }
  const r = parseInt(hex.substring(0, 2), 16);
  const g = parseInt(hex.substring(2, 4), 16);
  const b = parseInt(hex.substring(4, 6), 16);
  const adjustedAlpha = Math.min(1, Math.max(0, alphaAdjustment));
  return `rgba(${r}, ${g}, ${b}, ${adjustedAlpha})`;
}

function adjustRGBAWithAlpha(rgba: string, alphaAdjustment: number): string {
  const match = rgba.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)(,\s*(\d+\.?\d*))?\)/);
  if (!match) throw new Error('Invalid RGB(A) format');
  const [_, r, g, b, a = 1] = match.map(Number);
  const adjustedAlpha = Math.min(1, Math.max(0, a * alphaAdjustment));
  return `rgba(${r}, ${g}, ${b}, ${adjustedAlpha})`;
}

function adjustHSLAWithAlpha(hsla: string, alphaAdjustment: number): string {
  const match = hsla.match(/hsla?\((\d+),\s*(\d+)%,\s*(\d+)%(,\s*(\d+\.?\d*))?\)/);
  if (!match) throw new Error('Invalid HSL(A) format');
  const [_, h, s, l, a = 1] = match.map(Number);
  const adjustedAlpha = Math.min(1, Math.max(0, a * alphaAdjustment));
  return `hsla(${h}, ${s}%, ${l}%, ${adjustedAlpha})`;
}

export function autoBackgroundColor(color: string): string {
  return adjustAlpha(color, 0.1);
}

export function generateNextWithPrefix(prefix: string, currentList: string[]): string {
  const regex = new RegExp(`^${prefix} (\\d+)$`);

  // Extract numbers from the list
  const numbers = currentList
    .map(item => {
      const match = item.match(regex);
      return match ? parseInt(match[1]!, 10) : null;
    })
    .filter(num => num !== null) as number[];

  // Determine the next number
  const nextNumber = numbers.length ? Math.max(...numbers) + 1 : 1;

  return `${prefix} ${nextNumber}`;
}

export function isNotEmpty(str: string | undefined): boolean {
  return str !== undefined && str !== null && str.length > 0;
}

export function hasCircleReference<TObject, TKey>(
  data: TObject,
  all: TObject[],
  getKey: (item: TObject) => TKey,
  getChildKey: (item: TObject) => TKey | undefined,
): boolean {
  const visited = new Set<TKey>();
  let current = data;
  while (current) {
    const key = getKey(current);
    if (visited.has(key)) {
      return true;
    }
    visited.add(key);
    const childKey = getChildKey(current);
    if (childKey === undefined || childKey === null) {
      return false;
    }
    current = all.find(item => getKey(item) === childKey)!;
  }

  return false;
}


export function willCauseCircleReference<TObject, TKey>(
  parent: TObject,
  childKey: TKey,
  all: TObject[],
  getKey: (item: TObject) => TKey,
  getChildKey: (item: TObject) => TKey | undefined,
  setChildKey: (item: TObject, key: TKey | undefined) => void,
): boolean {
  const prevChildKey = getChildKey(parent);
  setChildKey(parent, childKey);
  const hasCircle = hasCircleReference(parent, all, getKey, getChildKey);
  setChildKey(parent, prevChildKey);
  return hasCircle;
}
