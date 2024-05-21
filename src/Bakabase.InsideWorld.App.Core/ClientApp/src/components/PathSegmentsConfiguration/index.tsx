import React, { useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Badge, Balloon, Dialog, Message, Tag } from '@alifd/next';
import { useUpdate, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import type { BalloonProps } from '@alifd/next/types/balloon';
import Tips from './Tips';
import Errors from './Errors';
import Segments from './Segments';
import './index.scss';
import { ResourceProperty } from '@/sdk/constants';
import { buildLayerBasedPathRegexString, buildLogger, useTraceUpdate } from '@/components/utils';
import CustomIcon from '@/components/CustomIcon';
import { BuildMatchResultLabel, PscCoreData } from '@/components/PathSegmentsConfiguration/models/PscCoreData';
import BApi from '@/sdk/BApi';
import { MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import type { IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import { PscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import PathSegmentMatcher from '@/components/PathSegmentsConfiguration/models/PathSegmentMatcher';
import type { MatchResult } from '@/components/PathSegmentsConfiguration/models/MatchResult';
import { MatchResultType } from '@/components/PathSegmentsConfiguration/models/MatchResult';
import { allMatchers, matchersAfter, matchersBefore } from '@/components/PathSegmentsConfiguration/models/instances';
import BusinessConstants from '@/components/BusinessConstants';
import ValidationResult from '@/components/PathSegmentsConfiguration/ValidationResult';
import SimpleLabel from '@/components/SimpleLabel';
import { Button, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import SimpleGlobalError = PscCoreData.SimpleGlobalError;
import SimpleGlobalMatch = PscCoreData.SimpleGlobalMatch;

export class PathSegmentConfigurationPropsMatcherOptions {
  property: ResourceProperty;
  readonly = false;

  constructor(init?: Partial<PathSegmentConfigurationPropsMatcherOptions>) {
    Object.assign(this, init);
  }
}

interface IPathSegmentsConfigurationProps {
  segments: string[];
  isDirectory: boolean;
  // type - readonly
  matchers?: PathSegmentConfigurationPropsMatcherOptions[];
  defaultValue?: IPscValue;
  onChange?: (value: IPscValue) => void;
  // onError?: (hasError: boolean) => void;
}

export interface IPathSegmentConfigurationRef {
  readonly coreData: PscCoreData;
}

const PathSegmentsConfiguration = React.forwardRef((props: IPathSegmentsConfigurationProps, ref) => {
  useTraceUpdate(props, 'PathSegmentsConfiguration');

  const {
    segments = [],
    isDirectory,
    matchers = [],
    defaultValue,
    onChange = (v) => {
    },
  } = props;

  const log = buildLogger('PathSegmentsConfiguration');

  log('Rendering with props', props);

  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();

  const [value, setValue] = useState<{ [type in ResourceProperty]?: MatcherValue[]; }>(new PscValue(defaultValue || {}).toComponentValue());

  const [visibleSmcId, setVisibleSmcId] = useState<string>();
  const smcAlignRef = useRef<BalloonProps['align']>();

  const valueRef = useRef(value);
  const visibleMatchers = matchers.map((a) => allMatchers.find((b) => b.property == a.property)!)
    .sort((a, b) => a.checkOrder - b.checkOrder);
  const configurableMatchers = matchers.filter((a) => !a.readonly)
    .map((a) => visibleMatchers.find((b) => b.property == a.property)!);

  const [fileResourceExtensions, setFileResourceExtensions] = useState<{
    ext: string;
    count: number;
  }[]>();
  const [fileResourceExtensionCandidates, setFileResourceExtensionCandidates] = useState<string[]>([]);
  const [fileResource, setFileResource] = useState<string>();
  const [loadingFileResourceExtensions, setLoadingFileResourceExtensions] = useState(false);

  const resourceMatcherMatchesLastLayerRef = useRef(false);

  useEffect(() => {
    valueRef.current = value;
    log('Changed:', JSON.parse(JSON.stringify(value)));
    onChange(PscValue.fromComponentValue(value));

    if (!isDirectory) {
      if (matchers.some(a => a.property == ResourceProperty.Resource && !a.readonly)) {
        const resourceMatcherValue = value[ResourceProperty.Resource]?.[0];
        if (resourceMatcherValue) {
          const rootMatch = PathSegmentMatcher.matchFirst(segments, value[ResourceProperty.RootPath]);
          const rootSegmentIndex = rootMatch?.index ?? -1;

          const resourceSegmentIndex = PathSegmentMatcher
            .match(segments, resourceMatcherValue, rootSegmentIndex, segments.length)?.index ?? -1;
          if (resourceSegmentIndex > -1) {
            const resourceMatcherMatchesLastLayer = resourceSegmentIndex == segments.length - 1;
            if (resourceMatcherMatchesLastLayer != resourceMatcherMatchesLastLayerRef.current) {
              resourceMatcherMatchesLastLayerRef.current = resourceMatcherMatchesLastLayer;
              if (resourceMatcherMatchesLastLayer) {
                setFileResource(segments.join('/'));
              } else {
                setFileResource(undefined);
              }
            }
          }
        }
      }
    }
  }, [value]);

  useUpdateEffect(() => {
    if (fileResource == undefined) {
      setFileResourceExtensions(undefined);
      setFileResourceExtensionCandidates([]);
    }
  }, [fileResource]);

  useImperativeHandle(ref, (): IPathSegmentConfigurationRef => ({
    get coreData(): PscCoreData {
      return buildCoreData();
    },
  }), []);

  const buildCoreData = (): PscCoreData => {
    const data = new PscCoreData();
    data.segments = segments.map(s => new PscCoreData.Segment({ text: s }));

    let rootSegmentIndex: number | undefined;
    const rootMatcherValue = value[ResourceProperty.RootPath]?.[0];
    let rootMatchResult: MatchResult | undefined;
    if (rootMatcherValue) {
      rootMatchResult = PathSegmentMatcher.match(segments, rootMatcherValue, -1, undefined);
      rootSegmentIndex = rootMatchResult?.index;
    }

    const resourceMatcherValue = value[ResourceProperty.Resource]?.[0];
    let resourceSegmentIndex: number | undefined;
    let resourceMatchResult: MatchResult | undefined;
    if (resourceMatcherValue && rootSegmentIndex != undefined) {
      resourceMatchResult = PathSegmentMatcher
        .match(segments, resourceMatcherValue, rootSegmentIndex, segments.length);
      if (resourceMatchResult) {
        switch (resourceMatchResult.type) {
          // Regex value may produce layer match result
          case MatchResultType.Layer:
            resourceSegmentIndex = resourceMatchResult.index;
            break;
          case MatchResultType.Regex:
            data.globalErrors.push(new SimpleGlobalError(ResourceProperty.Resource, undefined,
              t('Resource matcher can not have a regex result(make sure you are not setting groups in regex)'), true));
            // resourceSegmentIndex = rootSegmentIndex + (resourceMatchResult.matches?.[0].split('/').length ?? 0);
            break;
        }
      }
    }
    log(`Root index: ${rootSegmentIndex}, resource index: ${resourceSegmentIndex}`);

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


    const allMatchResults: { [property in ResourceProperty]?: (MatchResult | undefined)[] } = {};
    Object.keys(value)
      .forEach((key) => {
        const matcherType = parseInt(key, 10) as ResourceProperty;
        const results = (allMatchResults[matcherType] ??= []);
        switch (matcherType) {
          case ResourceProperty.RootPath:
            if (rootMatcherValue) {
              results.push(rootMatchResult);
            }
            break;
          case ResourceProperty.Resource:
            if (resourceMatcherValue && resourceMatchResult?.type == MatchResultType.Layer) {
              results.push(resourceMatchResult);
            }
            break;
          default: {
            const values = value[matcherType] || [];
            for (let i = 0; i < values.length; i++) {
              const result = PathSegmentMatcher.match(segments, values[i], rootSegmentIndex, resourceSegmentIndex);
              results.push(result);
            }
            break;
          }
        }
      });
    log('All match results:', allMatchResults);

    // We should render:
    // 1. Segments.
    // 2. Matchers with layer match results upon each segment.
    // 3. Matchers with regex match results under segments block.
    // 4. Mismatched matchers under segments block.
    // So we need iterate all matchers, not segments
    for (const vm of visibleMatchers) {
      const values = value[vm.property] || [];
      const results = allMatchResults[vm.property] || [];
      const readonly = !configurableMatchers.includes(vm);

      // check required
      if (vm.isRequired) {
        if (values.length == 0) {
          data.globalErrors.push(new SimpleGlobalError(vm.property, undefined, t('Missing'), false));
        }
      }

      // check prerequisites
      const missingPrerequisites = vm.prerequisites
        .filter(p => !(p in allMatchResults && allMatchResults[p]!.some(r => r)));
      let missingPrerequisitesTip: string | undefined;
      if (missingPrerequisites.length > 0) {
        missingPrerequisitesTip = t('[{{matchers}}] (is)are required to set this property', {
          matchers: missingPrerequisites.map(p => t(ResourceProperty[p]))
            .join(','),
        });
        if (results.length > 0) {
          data.globalErrors.push(new SimpleGlobalError(vm.property, undefined, missingPrerequisitesTip!, false));
        }
      }

      const orderCheckList = [
        matchersAfter[vm.property]!.map(m => m.property),
        matchersBefore[vm.property]!.map(m => m.property),
      ];

      log(`Checking order for ${ResourceProperty[vm.property]} with list`, orderCheckList);

      const getInvalidOrderMatcherTips = (segmentIndex: number): string[] => {
        const errors: string[] = [];
        // check orders
        for (let j = 0; j < orderCheckList.length; j++) {
          const list = orderCheckList[j];
          const invalidOrderResultLabels: string[] = [];
          for (const t of list) {
            const trs = (allMatchResults[t] || []).map(x => x?.index);
            for (let k = 0; k < trs.length; k++) {
              const si = trs[k];
              if (si != undefined && ((j == 0 && si <= segmentIndex) || (j == 1 && si >= segmentIndex))) {
                invalidOrderResultLabels.push(BuildMatchResultLabel(t, trs.length > 1 ? k : undefined));
              }
            }
          }
          if (invalidOrderResultLabels.length > 0) {
            errors.push(t(`{{target}} should come ${j == 1 ? 'after' : 'before'} {{invalidMatchers}}`, {
              target: t(ResourceProperty[vm.property]),
              invalidMatchers: invalidOrderResultLabels.join(','),
            }));
          }
        }
        return errors;
      };

      for (let i = 0; i < results.length; i++) {
        const r = results[i];
        log(`Checking result ${i} of ${ResourceProperty[vm.property]}`, r);
        // check mismatched values
        if (r == undefined) {
          data.globalErrors.push(new SimpleGlobalError(vm.property, results.length > 1 ? i : undefined, t('Match failed'), true));
        } else {
          if (r.type == MatchResultType.Layer) {
            if (r.index != undefined) {
              const segmentIndex = r.index!;
              const ds = data.segments[segmentIndex];
              let mr = ds.matchResults.find(a => a.property == vm.property && a.valueIndex == i);
              if (!mr) {
                mr = new PscCoreData.SimpleMatchResult(vm.property, results.length > 1 ? i : undefined);
                mr.readonly = readonly;
                ds.matchResults.push(mr);
              }
              const errors = getInvalidOrderMatcherTips(segmentIndex);
              for (const t of errors) {
                mr.errors.push(t);
              }
            }
          } else {
            if (r.type == MatchResultType.Regex) {
              data.globalMatches.push(new SimpleGlobalMatch(vm.property, i, r.matches!));
            }
          }
        }
      }

      // selective
      for (let i = 0; i < segments.length; i++) {
        const ds = data.segments[i];

        const sm = new PscCoreData.SelectiveMatcher({
          property: vm.property,
          readonly: readonly,
          replaceCurrent: !vm.multiple && results.length > 0,
        });

        if (!readonly) {
          if (missingPrerequisites.length == 0) {
            if (vm.property != ResourceProperty.RootPath) {
              const r = sm.matchModes.regex;
              if (commonRegexTargetText?.length > 0 ||
                (vm.property == ResourceProperty.Resource && resourceRegexTargetText?.length > 0)) {
                r.text = vm.property == ResourceProperty.Resource ? resourceRegexTargetText : commonRegexTargetText;
                r.available = true;
              } else {
                r.errors.push(t('Match target is not found'));
              }
            }
            const errors = getInvalidOrderMatcherTips(i);
            if (vm.property == ResourceProperty.RootPath) {
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
                    vm.property != ResourceProperty.Resource) {
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

    log('CoreData', data);
    return data;
  };

  const renderGlobalMatches = (matches: PscCoreData.SimpleGlobalMatch[]): any => {
    if (matches.length > 0) {
      return (
        <div className={'global-matches psc-block'}>
          <div className="title">{t('Global matches')}</div>
          {matches.map(gm => {
            const v = gm.valueIndex == undefined ? value[gm.property]?.[0] : value[gm.property]?.[gm.valueIndex];
            return (
              <div className={'global-match'}>
                <SimpleLabel status={'default'}>{gm.label}</SimpleLabel>
                {v && (
                  <span>{MatcherValue.ToString(v)}</span>
                )}
                {t('Matched {{count}} results', { count: gm.matches.length })}
                {gm.matches.map(m => (
                  <SimpleLabel status={'default'}>{m}</SimpleLabel>
                ))}
                <CustomIcon
                  type={'delete'}
                  size={'small'}
                  onClick={() => {
                    onDeleteMatcherValue(gm.property, gm.valueIndex);
                  }}
                />
              </div>
            );
          })}
        </div>
      );
    }
    return;
  };

  const onDeleteMatcherValue = (property: ResourceProperty, valueIndex: number = 0) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t('Sure to delete?'),
      onOk: () => {
        value[property]!.splice(valueIndex ?? 0, 1);
        setValue({
          ...value,
        });
      },
    });
  };

  const fileSegments = fileResource?.split('.') ?? [];
  let currentFileExt;
  if (fileSegments?.length > 1) {
    currentFileExt = `.${fileSegments[fileSegments.length - 1]}`;
  }

  // console.log(fileResourceExtensionCandidates);

  const renderFileExtensionLoader = () => {

  };

  const coreData = buildCoreData();

  const renderBottomOpts = () => {

  };

  return (
    <div className={'path-segments-configuration flex flex-col gap-1'}>
      <Tips />
      <Errors
        errors={coreData.globalErrors}
        value={value}
        onDeleteMatcherValue={onDeleteMatcherValue}
      />
      <Segments
        segments={coreData.segments}
        isDirectory={isDirectory}
        value={value}
        onChange={setValue}
        onDeleteMatcherValue={onDeleteMatcherValue}
      />
      {renderFileExtensionLoader()}
      {renderGlobalMatches(coreData.globalMatches)}
      {renderBottomOpts()}
    </div>
  );
});
export default React.memo(PathSegmentsConfiguration);
