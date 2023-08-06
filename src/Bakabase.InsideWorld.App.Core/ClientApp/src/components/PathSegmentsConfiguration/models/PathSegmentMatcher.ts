import { ResourceProperty } from '@/sdk/constants';
import type { MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { ResourceMatcherValueType } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { MatchResult } from '@/components/PathSegmentsConfiguration/models/MatchResult';
import { execAll } from '@/components/utils';
import { getResultFromExecAll } from '@/components/PathSegmentsConfiguration/utils';

class PathSegmentMatcher {
  property: ResourceProperty;
  multiple: boolean;
  valueType: ResourceMatcherValueType;
  isRequired: boolean;
  order: number;
  prerequisites: ResourceProperty[] = [ResourceProperty.Resource, ResourceProperty.RootPath];
  checkOrder: number;

  constructor(init?: Partial<PathSegmentMatcher>) {
    Object.assign(this, init);
  }

  /**
   * If regex value is applied, layer result will be returned if there is no matching group,
   * otherwise the matched groups will be returned
   * @param segments
   * @param value
   * @param startIndex starts from -1
   * @param endIndex ends to {@link segments.length}
   */
  static match(segments: string[], value: MatcherValue | undefined, startIndex: number | undefined, endIndex: number | undefined): MatchResult | undefined {
    let result: MatchResult | undefined;
    if (value !== undefined) {
      switch (value.type) {
        case ResourceMatcherValueType.FixedText: {
          if (startIndex != undefined && startIndex >= -1) {
            const subSegments = segments.slice(startIndex + 1);
            let str = '';
            for (let i = 0; i < subSegments.length; i++) {
              str += subSegments[i];
              if (str == value.fixedText) {
                const layer = i + 1;
                result = MatchResult.OfLayer(layer, startIndex + layer);
                break;
              }
              str += '/';
            }
          }
          break;
        }
        case ResourceMatcherValueType.Layer: {
          if (value.layer != undefined && value.layer != 0) {
            if (value.layer > 0) {
              if (startIndex != undefined && startIndex >= -1) {
                const idx = startIndex + value.layer;
                if (idx < segments.length) {
                  result = MatchResult.OfLayer(value.layer, value.layer + startIndex);
                }
              }
            } else {
              if (endIndex != undefined && endIndex <= segments.length && endIndex + value.layer >= 0) {
                result = MatchResult.OfLayer(value.layer, value.layer + endIndex);
              }
            }
          }
          break;
        }
        case ResourceMatcherValueType.Regex: {
          if (endIndex != undefined && endIndex <= segments.length && startIndex != undefined && startIndex >= -1 && endIndex > startIndex + 1) {
            const subPath = segments.slice(startIndex + 1, endIndex)
              .join('/');
            const rr = getResultFromExecAll(value.regex!, subPath);
            if (rr) {
              if (rr.groups) {
                result = MatchResult.OfRegex(rr.groups);
              } else {
                const length = subPath.indexOf(rr.text!) + rr.text!.length;
                const matchedText = subPath.substring(0, length);
                const layer = matchedText.split('/').length;
                result = MatchResult.OfLayer(layer, layer + startIndex);
              }
            }
          }
          break;
        }
      }

      console.log('[Matcher]Matching', segments, `from ${startIndex} to ${endIndex}`, 'with', value.toString(), 'result:', result);
    }

    return result;
  }

  static matchAll(segments: string[], values?: MatcherValue[], rootIndex: number = -1, resourceIndex: number = -1, stopAtFirstMatch: boolean = false): ((MatchResult | undefined)[]) {
    if (!values) {
      return [];
    }
    const results: (MatchResult | undefined)[] = [];
    for (let i = 0; i < values.length; i++) {
      const value = values[i];
      const result = this.match(segments, value, rootIndex, resourceIndex);
      if (result) {
        results.push(result);
        if (stopAtFirstMatch) {
          break;
        }
      } else {
        results.push(undefined);
      }
    }
    return results;
  }

  static matchFirst(segments: string[], values?: MatcherValue[], rootIndex: number = -1, resourceIndex: number = -1): MatchResult | undefined {
    return this.matchAll(segments, values, rootIndex, resourceIndex, true)?.[0];
  }
}

export default PathSegmentMatcher;
