import React, { useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Badge, Balloon, Button, Dialog, Message, Tag } from '@alifd/next';
import { useUpdate, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import IceLabel from '@icedesign/label';
import type { BalloonProps } from '@alifd/next/types/balloon';
import SegmentMatcherConfiguration from './SegmentMatcherConfiguration';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import './index.scss';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import { buildLayerBasedPathRegexString, buildLogger, useTraceUpdate } from '@/components/utils';
import CustomIcon from '@/components/CustomIcon';
import { BuildMatchResultLabel, PscCoreData } from '@/components/PathSegmentsConfiguration/models/PscCoreData';
import BApi from '@/sdk/BApi';
import type { IMatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import { MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import type { IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import { PscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import PathSegmentMatcher from '@/components/PathSegmentsConfiguration/models/PathSegmentMatcher';
import type { MatchResult } from '@/components/PathSegmentsConfiguration/models/MatchResult';
import { MatchResultType } from '@/components/PathSegmentsConfiguration/models/MatchResult';
import { allMatchers, matchersAfter, matchersBefore } from '@/components/PathSegmentsConfiguration/models/instances';
import BusinessConstants from '@/components/BusinessConstants';
import ValidationResult from '@/components/PathSegmentsConfiguration/ValidationResult';
import SimpleGlobalError = PscCoreData.SimpleGlobalError;
import SimpleGlobalMatch = PscCoreData.SimpleGlobalMatch;
import SimpleLabel from '@/components/SimpleLabel';

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

  const selectMatcher = (c: PathSegmentMatcher, newValue: IMatcherValue) => {
    const currentValues = value[c.property] || [];
    if (!c.multiple) {
      currentValues.splice(0, currentValues.length);
    }

    const sameValue = currentValues.find(v => v.equals(newValue));
    if (sameValue) {
      return;
    }
    currentValues.push(new MatcherValue(newValue));

    const newAllValue = {
      ...value,
      [c.property]: currentValues,
    };
    log('Changing value', newAllValue);
    setValue(newAllValue);
  };

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

  const renderGlobalErrors = (errors: PscCoreData.SimpleGlobalError[]): any => {
    if (errors.length > 0) {
      return (
        <div className={'global-errors psc-block'}>
          <div className="title">{t('Errors')}</div>
          {errors.map(e => {
            const v = e.valueIndex == undefined ? value[e.property]?.[0] : value[e.property]?.[e.valueIndex];
            return (
              <div className={'error'}>
                <SimpleLabel status={'danger'}>
                  {e.label}
                </SimpleLabel>
                {v && (
                  <span>{MatcherValue.ToString(v)}</span>
                )}
                {e.message}
                {e.deletable && (
                  <CustomIcon
                    type={'delete'}
                    size={'small'}
                    onClick={() => {
                      value[e.property]!.splice(e.valueIndex ?? 0, 1);
                      setValue({
                        ...value,
                      });
                    }}
                  />
                )}
              </div>
            );
          })}
        </div>
      );
    }
    return;
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
                    confirmDeletingValue(gm.property, gm.valueIndex);
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

  const confirmDeletingValue = (property: ResourceProperty, valueIndex: number = 0) => {
    Dialog.confirm({
      title: t('Sure to delete?'),
      onOk: () => {
        value[property]!.splice(valueIndex ?? 0, 1);
        setValue({
          ...value,
        });
      },
      closeMode: ['esc', 'mask', 'close'],
    });
  };

  const renderMatchModeTip = (name: string, icon: string, available: boolean, errors: string[]) => {
    return (
      <Balloon.Tooltip
        trigger={(
          <CustomIcon type={icon} className={available ? 'available' : 'invalid'} size={'small'} />
        )}
      >
        {available ? t(`${name} mode is available`) : (
          <div>
            {t(`${name} mode is not available`)}
            {errors.length > 0 && (
              <ul>
                {errors.map(e => (
                  <li>{e}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      </Balloon.Tooltip>
    );
  };

  const renderSegments = (segmentsData: PscCoreData.Segment[]): any => {
    if (segmentsData.length > 0) {
      const elements: any[] = [];

      for (let i = 0; i < segmentsData.length; i++) {
        if (i > 0) {
          elements.push(
            <span className={'path-separator'}>/</span>,
          );
        }

        const {
          text,
          selectiveMatchers = [],
          matchResults = [],
          disabled,
        } = segmentsData[i];

        const sc = (
          <div className={`segment-container ${disabled ? 'disabled' : ''}`} key={i}>
            {matchResults.length > 0 && (
              <div className={'matched-matchers'}>
                {matchResults.map(mr => {
                  const {
                    label,
                    errors = [],
                  } = mr;
                  const hasError = errors.length > 0;
                  const v = mr.valueIndex == undefined ? value[mr.property]?.[0] : value[mr.property]?.[mr.valueIndex];
                  const comp = (
                    <div className={`matched-matcher ${hasError ? 'error' : ''} type-${ResourceProperty[mr.property]}`}>
                      {hasError && (
                        <CustomIcon type={'warning-circle'} size={'small'} />
                      )}
                      {label}
                      {v && (
                        <span>{MatcherValue.ToString(v)}</span>
                      )}
                      {!mr.readonly && (
                        <CustomIcon
                          type={'delete'}
                          size={'small'}
                          onClick={() => {
                            confirmDeletingValue(mr.property, mr.valueIndex);
                          }}
                        />
                      )}
                    </div>
                  );
                  if (hasError) {
                    return (
                      <Balloon
                        closable={false}
                        v2
                        trigger={comp}
                        triggerType={'hover'}
                      >
                        {errors.map(e => {
                          return (
                            <div
                              className={'error'}
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 2,
                                color: 'red',
                              }}
                            >
                              <CustomIcon type={'close'} size={'small'} />
                              {e}
                            </div>
                          );
                        })}
                      </Balloon>
                    );
                  } else {
                    return comp;
                  }
                })}
              </div>
            )}
            <Balloon
              trigger={(
                <div className="segment">
                  {text}
                </div>
              )}
              v2
              closable={false}
              triggerType={'click'}
              autoFocus={false}
            >
              <div className="matcher-selectors-balloon">
                <Message type={'notice'} title={t('Setting a Resource property to segment')}>
                  {t('Some properties may not able to set by layer, but you still can set it by regex.')}
                </Message>
                <div className="matcher-selectors">
                  {selectiveMatchers.map(m => {
                    const {
                      layer: l,
                      regex: r,
                      oneClick: o,
                    } = m.matchModes;
                    const showReplace = m.isConfigurable && m.replaceCurrent;
                    const btn = (
                      <Button
                        type={'normal'}
                        className={'matcher-selector'}
                        disabled={!m.isConfigurable}
                        onClick={(e) => {
                          if (o.available) {
                            selectMatcher(visibleMatchers.find(t => t.property == m.property)!, {
                              type: ResourceMatcherValueType.FixedText,
                              fixedText: segments.slice(0, i + 1).join(BusinessConstants.pathSeparator),
                            });
                          } else {
                            const hw = window.innerWidth / 2;
                            const hh = window.innerHeight / 2;
                            const cx = e.clientX;
                            const cy = e.clientY;
                            const align = cx > hw ? cy > hh ? 'lt' : 'l' : cy > hh ? 'rt' : 'r';
                            smcAlignRef.current = align;
                            setVisibleSmcId(`${i}-${m.property}`);
                          }
                        }}
                      >
                        <div>
                          <div className="property">
                            {t(ResourceProperty[m.property])}
                          </div>
                          <div className="icons">
                            {renderMatchModeTip('Layer', 'position', l.available, l.errors)}
                            {renderMatchModeTip('Regex', 'regex', r.available, r.errors)}
                            {m.errors.length > 0 && (
                              <Balloon.Tooltip
                                trigger={(
                                  <CustomIcon type={'warning-circle'} className={'invalid'} size={'small'} />
                                )}
                              >
                                <ul>
                                  {m.errors.map(e => (
                                    <li>{e}</li>
                                  ))}
                                </ul>
                              </Balloon.Tooltip>
                            )}
                            {showReplace && (
                              <Balloon.Tooltip
                                trigger={(
                                  <CustomIcon className={'replace'} type={'sync'} size={'small'} />
                                )}
                                triggerType={'hover'}
                              >
                                {t('Selected value will be replaced')}
                              </Balloon.Tooltip>
                            )}
                          </div>
                        </div>

                      </Button>
                    );

                    if (!m.useSmc) {
                      return btn;
                    } else {
                      return (
                        <Balloon
                          v2
                          closable={false}
                          trigger={btn}
                          triggerType={'click'}
                          // followTrigger
                          autoAdjust
                          autoFocus={false}
                          shouldUpdatePosition
                          onClose={() => {
                            setVisibleSmcId(undefined);
                          }}
                          align={smcAlignRef.current}
                          visible={visibleSmcId === `${i}-${m.property}`}
                        >
                          <SegmentMatcherConfiguration
                            property={m.property}
                            modesData={m.buildModesData()}
                            isCustomProperty={m.property == ResourceProperty.CustomProperty}
                            onSubmit={value => {
                              selectMatcher(visibleMatchers.find(t => t.property == m.property)!, value);
                              setVisibleSmcId(undefined);
                            }}
                          />
                        </Balloon>
                      );
                    }
                  })}
                </div>
              </div>
            </Balloon>
          </div>
        );

        elements.push(sc);
      }

      return (
        <div className={'path-segments'}>
          {elements}
        </div>
      );
    }
  };

  const fileSegments = fileResource?.split('.') ?? [];
  let currentFileExt;
  if (fileSegments?.length > 1) {
    currentFileExt = `.${fileSegments[fileSegments.length - 1]}`;
  }

  console.log(fileResourceExtensionCandidates);

  const renderFileExtensionLoader = () => {
    if (fileResource) {
      const rootSegmentIndex = (PathSegmentMatcher?.match(segments, value[ResourceProperty.RootPath]?.[0],
        -1, undefined))?.index ?? -1;
      const rootPathIsSelected = rootSegmentIndex > -1;
      const loaderBtn = (
        <Button
          type={'primary'}
          text
          style={{ marginTop: '10px' }}
          loading={loadingFileResourceExtensions}
          disabled={!rootPathIsSelected}
          onClick={() => {
            setLoadingFileResourceExtensions(true);
            BApi.file.getFileExtensionCounts({
              sampleFile: fileResource,
              rootPath: segments.slice(0, rootSegmentIndex + 1).join(BusinessConstants.pathSeparator),
            })
              .then(a => {
                if (!a.code) {
                  const counts = a.data ?? {};
                  const list = Object.keys(counts)
                    .sort((x, y) => counts[y]! - counts[x]!)
                    .map(b => ({
                      ext: b,
                      count: counts[b]!,
                    }));
                  setFileResourceExtensions(list);
                  if (currentFileExt) {
                    setFileResourceExtensionCandidates([currentFileExt]);

                    // we should change the value of resource matcher to file-extension-based regex immediately when user click this button
                    const newRegex = buildLayerBasedPathRegexString(segments.length - rootSegmentIndex - 1, [currentFileExt]);
                    value[ResourceProperty.Resource] = [
                      MatcherValue.Regex(newRegex),
                    ];
                    setValue({ ...value });
                  }
                }
              })
              .finally(() => {
                setLoadingFileResourceExtensions(false);
              });
          }}
        >
          {t('I need choose files with specific types as resources')}
        </Button>
      );

      return (
        <div className="file-resource-helper psc-block">
          <div className="title">{t('Match by file extensions')}</div>
          {fileResourceExtensions == undefined ? (
            <>
              <Message type={'warning'}>
                <>
                  {t('Please pay attention, you marked a file as a resource. We\'ll treat all files or folders with same path layer as resources by default. It may not be your actual design(if you chose a movie and its subtitle file has the same path layer, then the subtitle file will be another resource).')}
                  {t('If you want to mark resources with specific file types, you can click button below to load all file types and select some of them.')}
                </>
              </Message>
              {rootPathIsSelected ? loaderBtn : (
                <Balloon.Tooltip
                  trigger={loaderBtn}
                  triggerType={'hover'}
                  align={'t'}
                >
                  {t('Please select root path first.')}
                </Balloon.Tooltip>
              )}
            </>
          ) : fileResourceExtensions.length > 0 ? (
            <>
              <Message type={'notice'}>
                <>
                  {t('You can mark resources by file types.')}
                  {fileResourceExtensionCandidates.length > 0
                    ? `${t('You selected {{count}} file types:', { count: fileResourceExtensionCandidates.length })}${fileResourceExtensionCandidates.join(', ')}`
                    : t('None of file types has been selected, we\'ll treat all files or folders with same path layer as your file as resources.')}
                </>
              </Message>
              <Tag.Group style={{ marginTop: 5 }}>
                {fileResourceExtensions.map(e => {
                  return (
                    <Tag.Selectable
                      size={'small'}
                      checked={fileResourceExtensionCandidates.indexOf(e.ext) > -1}
                      onChange={c => {
                        const candidates = fileResourceExtensionCandidates || [];
                        const idx = candidates.indexOf(e.ext);

                        const apply = candidates => {
                          const newRegex = buildLayerBasedPathRegexString(segments.length - rootSegmentIndex - 1, candidates);
                          value[ResourceProperty.Resource] = [
                            MatcherValue.Regex(newRegex),
                          ];
                          setValue({ ...value });
                          setFileResourceExtensionCandidates([...candidates]);
                        };

                        if (idx == -1) {
                          candidates.push(e.ext);
                        } else {
                          if (e.ext == currentFileExt) {
                            Dialog.confirm({
                              title: t('You are deselecting extension same as file you selected'),
                              content: t('After your deselection, the current file will not be treat as a resource, and you need reset the Resource part from path segments, are you sure to remove file type: {{ext}}?', { ext: currentFileExt }),
                              closeable: true,
                              v2: true,
                              closeMode: ['close', 'esc', 'mask'],
                              onOk: () => new Promise((resolve, reject) => {
                                candidates.splice(idx, 1);
                                apply(candidates);
                                resolve(undefined);
                              }),
                            });
                            return;
                          } else {
                            candidates.splice(idx, 1);
                          }
                        }
                        apply(candidates);
                      }}
                    >
                      {e.ext}
                      &nbsp;
                      <Badge count={e.count} overflowCount={99999999} style={{ backgroundColor: '#87d068' }} />
                    </Tag.Selectable>
                  );
                })}
              </Tag.Group>
            </>
          ) : <>
            {t('No known file type is found.')}
          </>}
        </div>
      );
    }
    return;
  };

  const coreData = buildCoreData();

  const renderBottomOpts = () => {
    const validationButton = (
      <Button
        disabled={coreData.hasError}
        type={'primary'}
        size={'small'}
        onClick={() => {
          const dialog = Dialog.show({
            title: t('Validating'),
            footer: false,
            closeable: false,
          });

          // @ts-ignore
          BApi.mediaLibrary.validatePathConfiguration(PscValue.fromComponentValue(value))
            .then((t) => {
              ValidationResult.show({
                // @ts-ignore
                testResult: t.data,
              });
            })
            .finally(() => {
              dialog.hide();
            });
        }}
      >
        {t('Validate')}
      </Button>
    );

    return (
      <div className="bottom-opts">
        {validationButton}
      </div>
    );
  };

  return (
    <div className={'path-segments-configuration'}>
      <Message
        type={'notice'}
      >
        <>
          {t('You can set conventions on each path segments to fill the properties of resources.')}
          <br />
          {t('If your path layer isn\'t stable to root path but it is stable to resource, \'The xxx layer to the resource\' is your better choice.')}
        </>
      </Message>
      {renderGlobalErrors(coreData.globalErrors)}
      <div className="path-segments-container psc-block">
        <div className="title">{t('Segment matches')}</div>
        {segments.length > 0 && (
          <>
            <div className="icon">
              <FileSystemEntryIcon path={segments.join('/')} isDirectory={isDirectory} />
            </div>
            {renderSegments(coreData.segments)}
          </>
        )}
      </div>
      {renderFileExtensionLoader()}
      {renderGlobalMatches(coreData.globalMatches)}
      {renderBottomOpts()}
    </div>
  );
});
export default React.memo(PathSegmentsConfiguration);
