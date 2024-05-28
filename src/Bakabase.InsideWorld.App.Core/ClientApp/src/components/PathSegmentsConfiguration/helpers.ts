import type { TFunction } from 'react-i18next';
import PscProperty from './models/PscProperty';
import type { IPscPropertyMatcherValue } from './models/PscPropertyMatcherValue';
import { PscMatcherValue } from './models/PscMatcherValue';
import { execAll } from '@/components/utils';
import { MatchResultType, ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import type { BakabaseInsideWorldBusinessModelsDomainPathConfiguration } from '@/sdk/Api';
import { PscContext } from '@/components/PathSegmentsConfiguration/models/PscContext';
import type { PscMatchResult } from '@/components/PathSegmentsConfiguration/models/PscMatchResult';
import PscMatcher from '@/components/PathSegmentsConfiguration/models/PscMatcher';
import { matchersAfter, matchersBefore } from '@/components/PathSegmentsConfiguration/matchers';
import { PscPropertyType } from '@/components/PathSegmentsConfiguration/models/PscPropertyType';
import SimpleGlobalError = PscContext.SimpleGlobalError;
import SimpleGlobalMatch = PscContext.SimpleGlobalMatchResult;

export function convertToPscValueFromPathConfigurationDto(pc: BakabaseInsideWorldBusinessModelsDomainPathConfiguration): IPscPropertyMatcherValue[] {
  const value: IPscPropertyMatcherValue[] = [];
  if (pc.path) {
    value.push({
      value: new PscMatcherValue({
        valueType: ResourceMatcherValueType.FixedText,
        fixedText: pc.path,
      }),
      property: PscProperty.RootPath,
    });
  }

  if (pc.rpmValues) {
    for (const segment of pc.rpmValues) {
      value.push({
        value: new PscMatcherValue({
          valueType: segment.valueType!,
          fixedText: segment.fixedText ?? undefined,
          regex: segment.regex ?? undefined,
          layer: segment.layer ?? undefined,
        }),
        property: new PscProperty({
          isReserved: segment.isReservedProperty!,
          id: segment.propertyId!,
        }),
      });
    }
  }

  console.log('Convert dto value to component value', pc, value);
  return value;
}

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

export function convertToPathConfigurationDtoFromPscValue(pmvs: IPscPropertyMatcherValue[]): BakabaseInsideWorldBusinessModelsDomainPathConfiguration {
  const rootPath = (pmvs.filter(v => v.property.isRootPath))[0]?.value?.fixedText;
  const dto: BakabaseInsideWorldBusinessModelsDomainPathConfiguration = {
    path: rootPath,
    // regex: resourceRegex,
    rpmValues: pmvs.filter(v => !v.property.isRootPath).map(v => {
      return {
        propertyId: v.property.id,
        isReservedProperty: v.property.isReserved,
        fixedText: v.value.fixedText,
        layer: v.value.layer,
        regex: v.value.regex,
        valueType: v.value.valueType,
      };
    }),
  };

  console.log('Convert component value to dto value', pmvs, dto);
  // @ts-ignore
  return dto;
}

type IPscPropertyMatcherResult = {
  pmv: IPscPropertyMatcherValue;
  result?: PscMatchResult;
  indexByProperty?: number;
};


export const BuildPscContext = (segments: string[], pmvs: IPscPropertyMatcherValue[],
                                visibleMatchers: PscMatcher[], configurableMatchers: PscMatcher[],
                                t: TFunction<'translation', undefined>, log = console.log): PscContext => {
  const data = new PscContext();
  data.segments = segments.map(s => new PscContext.Segment({ text: s }));

  const allPropertyMatchResults: IPscPropertyMatcherResult[] = [];

  // calculate root match result
  let rootSegmentIndex: number | undefined;
  let rootMatchResult: PscMatchResult | undefined;
  const rootPmv = pmvs.find(x => x.property.isRootPath);
  if (rootPmv) {
    const rootMatcherValue = rootPmv.value;
    if (rootMatcherValue) {
      rootMatchResult = PscMatcher.match(segments, rootMatcherValue, -1, undefined);
      if (rootMatchResult) {
        rootSegmentIndex = rootMatchResult?.index;
      }
    }
    allPropertyMatchResults.push({
      pmv: rootPmv,
      result: rootMatchResult,
    });
  }

  // calculate resource match result
  let resourceSegmentIndex: number | undefined;
  let resourceMatchResult: PscMatchResult | undefined;
  const resourcePmv = pmvs.find(x => x.property.isResource);
  if (resourcePmv) {
    const resourceMatcherValue = resourcePmv.value;
    if (resourceMatcherValue && rootSegmentIndex != undefined) {
      resourceMatchResult = PscMatcher
        .match(segments, resourceMatcherValue, rootSegmentIndex, segments.length);
      if (resourceMatchResult) {
        switch (resourceMatchResult.type) {
          // Regex value may produce layer match result
          case MatchResultType.Layer:
            resourceSegmentIndex = resourceMatchResult.index;
            break;
          case MatchResultType.Regex:
            data.globalErrors.push(new SimpleGlobalError(PscProperty.Resource, undefined,
              t('Resource matcher can not have a regex result(make sure you are not setting groups in regex)'), true));
            break;
        }
      }
      allPropertyMatchResults.push({
        pmv: resourcePmv,
        result: resourceMatchResult,
      });
    }
  }
  log(`Root index: ${rootSegmentIndex}, resource index: ${resourceSegmentIndex}`);

  {
    const pmvIndexByPropertyMap = new Map<IPscPropertyMatcherValue, number>();
    const pmvIndexByPropertyKeyMapCounter: Record<string, number> = {};
    for (const pmv of pmvs) {
      const { key } = pmv.property;
      let index = pmvIndexByPropertyKeyMapCounter[key];
      if (index == undefined) {
        index = 0;
      }
      pmvIndexByPropertyKeyMapCounter[key] = index + 1;
      pmvIndexByPropertyMap.set(pmv, index);
    }

    for (const pmv of pmvs) {
      const { key } = pmv.property;
      const count = pmvIndexByPropertyKeyMapCounter[key];
      if (count == 1) {
        pmvIndexByPropertyMap.delete(pmv);
      }
    }

    pmvs!.filter(v => !v.property.isResource && !v.property.isRootPath).forEach((pmv) => {
      const result = PscMatcher.match(segments, pmv.value, rootSegmentIndex, resourceSegmentIndex);
      allPropertyMatchResults.push({
        pmv,
        result,
        indexByProperty: pmvIndexByPropertyMap.get(pmv),
      });
    });
  }
  log('All match results:', allPropertyMatchResults);

  // We should render:
  // 1. Segments.
  // 2. Matchers with layer match results upon each segment.
  // 3. Matchers with regex match results under segments block.
  // 4. Mismatched matchers under segments block.


  // Check required, prerequisites and orders
  for (const vm of visibleMatchers) {
    const propertyMatchResultsOfMatcher = allPropertyMatchResults.filter(x => x.pmv.property.type == vm.propertyType) || [];
    // check required
    if (vm.isRequired && propertyMatchResultsOfMatcher.length == 0) {
      data.globalErrors.push(new SimpleGlobalError(PscProperty.fromPscType(vm.propertyType), undefined, t('Missing'), false));
    }

    // check prerequisites
    const missingPrerequisites = vm.prerequisites.filter(p => allPropertyMatchResults.every(m => m.pmv.property.type != p));
    let missingPrerequisitesTip: string | undefined;
    if (missingPrerequisites.length > 0) {
      missingPrerequisitesTip = t('[{{matchers}}] (is)are required to set this property', {
        matchers: missingPrerequisites.map(p => t(PscPropertyType[p])).join(','),
      });
      if (propertyMatchResultsOfMatcher.length > 0) {
        propertyMatchResultsOfMatcher.forEach(r => {
          data.globalErrors.push(
            new SimpleGlobalError(r.pmv.property, r.indexByProperty, missingPrerequisitesTip!, false),
          );
        });
      }
    }

    // check orders
    const orderCheckList = [
      matchersAfter[vm.propertyType]!.map(m => m.propertyType),
      matchersBefore[vm.propertyType]!.map(m => m.propertyType),
    ];
    log(`Checking order for ${PscPropertyType[vm.propertyType]} with list`, orderCheckList);
    const getInvalidOrderMatcherTips = (segmentIndex: number): string[] => {
      const errors: string[] = [];
      // check orders
      for (let direction = 0; direction < orderCheckList.length; direction++) {
        const propertyTypes = orderCheckList[direction];
        const invalidOrderResultLabels: string[] = [];
        for (const pt of propertyTypes) {
          const pmrs = (allPropertyMatchResults.filter(r => r.pmv.property.type == pt) || []);
          for (const pmr of pmrs) {
            if (pmr.result) {
              const mr = pmr.result;
              const si = mr.index;
              if (si != undefined && ((direction == 0 && si <= segmentIndex) || (direction == 1 && si >= segmentIndex))) {
                invalidOrderResultLabels.push(pmr.pmv.property.toString(t, pmr.indexByProperty));
              }
            }
          }
        }
        if (invalidOrderResultLabels.length > 0) {
          errors.push(t(`{{target}} should come ${direction == 1 ? 'after' : 'before'} {{invalidMatchers}}`, {
            target: t(PscPropertyType[vm.propertyType]),
            invalidMatchers: invalidOrderResultLabels.join(','),
          }));
        }
      }
      return errors;
    };

    const readonly = !configurableMatchers.includes(vm);

    // validate all match results
    for (const pmr of propertyMatchResultsOfMatcher) {
      const r = pmr.result;
      log(`Checking result ${pmr.indexByProperty} of ${pmr.pmv.property.toString(t, pmr.indexByProperty)}`);
      // check mismatched values
      if (r == undefined) {
        data.globalErrors.push(new SimpleGlobalError(pmr.pmv.property, pmr.indexByProperty, t('Match failed'), true));
      } else {
        if (r.type == MatchResultType.Layer) {
          if (r.index != undefined) {
            const segmentIndex = r.index!;
            const segment = data.segments[segmentIndex];
            let mr = segment.matchResults.find(a => a.property.type == vm.propertyType && a.valueIndex == pmr.indexByProperty);
            if (!mr) {
              mr = new PscContext.SimpleMatchResult(pmr.pmv.property, pmr.indexByProperty);
              mr.readonly = readonly;
              segment.matchResults.push(mr);
            }
            const errors = getInvalidOrderMatcherTips(segmentIndex);
            for (const t of errors) {
              mr.errors.push(t);
            }
          }
        } else {
          if (r.type == MatchResultType.Regex) {
            data.globalMatches.push(new SimpleGlobalMatch(pmr.pmv.property, pmr.indexByProperty, r.matches!));
          }
        }
      }
    }

    // 用于资源或其他属性正则匹配，从根路径右侧到资源路径左侧
    let resourceRegexTargetText = '';
    let commonRegexTargetText = '';
    if (rootSegmentIndex != undefined) {
      resourceRegexTargetText = segments.slice(rootSegmentIndex + 1)
        .join('/');

      if (resourceSegmentIndex != undefined) {
        commonRegexTargetText = segments.slice(rootSegmentIndex + 1, resourceSegmentIndex)
          .join('/');
      }
    }
    log(`Resource regex target: [${resourceRegexTargetText}], Common regex target: [${commonRegexTargetText}]`);

    // selective for matcher
    for (let i = 0; i < segments.length; i++) {
      const ds = data.segments[i];

      const sm = new PscContext.SelectiveMatcher({
        propertyType: vm.propertyType,
        readonly: readonly,
        replaceCurrent: !vm.multiple && propertyMatchResultsOfMatcher.length > 0,
      });

      if (!readonly) {
        if (missingPrerequisites.length == 0) {
          if (vm.propertyType != PscPropertyType.RootPath) {
            const r = sm.matchModes.regex;
            if (commonRegexTargetText?.length > 0 ||
              (vm.propertyType == PscPropertyType.Resource && resourceRegexTargetText?.length > 0)) {
              r.text = vm.propertyType == PscPropertyType.Resource ? resourceRegexTargetText : commonRegexTargetText;
              r.available = true;
            } else {
              r.errors.push(t('Match target is not found'));
            }
          }
          const errors = getInvalidOrderMatcherTips(i);
          if (vm.propertyType == PscPropertyType.RootPath) {
            const oc = sm.matchModes.oneClick;
            if (errors.length == 0) {
              oc.available = true;
            } else {
              for (const t of errors) {
                sm.errors.push(t);
              }
            }
          } else {
            const l = sm.matchModes.layer;
            if (errors.length == 0) {
              const layers: number[] = [];
              if (rootSegmentIndex != undefined && rootSegmentIndex > -1) {
                layers.push(i - rootSegmentIndex);
                if (resourceSegmentIndex != undefined && resourceSegmentIndex > -1 &&
                  vm.propertyType != PscPropertyType.Resource) {
                  layers.push(i - resourceSegmentIndex);
                }
              }
              l.layers = layers;
              l.available = true;
            } else {
              for (const t of errors) {
                l.errors.push(t);
              }
            }
          }
        } else {
          sm.errors.push(missingPrerequisitesTip!);
        }
      } else {
        sm.errors.push(t('Readonly'));
      }

      ds.selectiveMatchers.push(sm);
    }
  }

  data.segments.forEach(s => {
    s.disabled = s.selectiveMatchers.length == 0 || s.selectiveMatchers.every(e => !e.isConfigurable);
  });

  log('Context', data);
  return data;
};
