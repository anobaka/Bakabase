import { Balloon, Button, Dialog, Icon, Message } from '@alifd/next';
import IceLabel from '@icedesign/label';
import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useDeepCompareEffect, usePrevious } from 'react-use';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';
import { buildLogger, parseLayerCountFromLayerBasedPathRegexString, splitPathIntoSegments, standardizePath } from '@/components/utils';
import type { IPscMatcherValue, IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import TagSelector from '@/components/TagSelector';
import './index.scss';
import PathSegmentsConfiguration, { PathSegmentConfigurationPropsMatcherOptions } from '@/components/PathSegmentsConfiguration';
import ClickableIcon from '@/components/ClickableIcon';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';

const log = buildLogger('PathConfigurationDialog');

interface Props {
  library: { id: number; pathConfigurations: any[]; name: string };
  value?: any;
  afterClose: () => void;
  onSaved: () => Promise<any>;
}

export default (props: Props) => {
  const {
    library,
    value: propsValue,
    afterClose = () => {
    },
    onSaved,
  } = props;
  const { t } = useTranslation();
  const [value, setValue] = useState(structuredClone(propsValue));
  const [testResult, setTestResult] = useState<any>();
  const [checkingPathRelations, setCheckingPathRelations] = useState(false);
  const [relativeLibraries, setRelativeLibraries] = useState<any[]>([]);
  const [pathConfigurationChanged, setPathConfigurationChanged] = useState(false);
  const [pscData, setPscData] = useState<{ value: IPscValue; segments: string[] }>();


  useEffect(() => {
    const newValue = structuredClone(propsValue);
    if (newValue) {
      newValue.prevPath = newValue.path;
    }
    log('Props value changed', newValue);
    setValue(newValue);
  }, [propsValue]);

  const prevValue = usePrevious(value);

  useEffect(() => {
    log('Value changed', value, 'prev', prevValue, value == prevValue);
    setPathConfigurationChanged(prevValue != value);
  }, [value]);

  const checkPathRelations = useCallback((pc) => {
    setCheckingPathRelations(true);
    BApi.mediaLibrary.getPathRelatedLibraries({
      libraryId: library.id,
      currentPath: pc.prevPath,
      newPath: pc.path,
    })
      .then((a) => {
        setRelativeLibraries(a.data || []);
      })
      .finally(() => {
        setCheckingPathRelations(false);
      });
  }, []);

  useEffect(() => {
    log('Initialize with', props);
  }, []);

  const renderTestResultError = useCallback((testResult) => {
    let error;
    if (!(testResult?.entries?.length > 0)) {
      error = t('Unable to find any resource');
    }
    if (!error) {
      if (testResult?.conflictSegmentConfigurationCount > 0) {
        error = t('{{count}} possible conflicts segment configurations are found.', { count: testResult.conflictSegmentConfigurationCount });
      }
    }
    return error && (
      <span className={'error'}>{error}      </span>
    );
  }, []);

  console.log(pathConfigurationChanged, testResult);
  const testThenSave = pathConfigurationChanged || !testResult;

  let error;
  if (!value?.path) {
    error = 'Root path is not set';
  } else if (value?.rpmValues?.length > 0) {

  }
  if (error) {
    error = t(error);
  }

  const renderSubmitButton = (validate: boolean) => {
    return (
      <Button
        type={validate ? 'primary' : 'normal'}
        disabled={!!error}
        onClick={() => {
          if (!testThenSave || !validate) {
            const error = renderTestResultError(testResult);
            const save = (onSuccess?, onFail?) => {
              log(library.pathConfigurations, value);
              const pcIdx = library.pathConfigurations.findIndex((p) => p.path == value.prevPath);
              if (pcIdx > -1) {
                const newPcs = library.pathConfigurations.slice();
                newPcs.splice(pcIdx, 1, value);
                log(`Saving ${pcIdx} of ${library.pathConfigurations.length} in ${library.name}`, value, newPcs);
                BApi.mediaLibrary.patchMediaLibrary(library.id, {
                  pathConfigurations: newPcs,
                })
                  .then((t) => {
                    if (!t.code) {
                      onSaved();
                      setValue(undefined);
                      onSuccess && onSuccess();
                    } else {
                      onFail && onFail();
                    }
                  })
                  .catch(() => {
                    onFail && onFail();
                  });
              } else {
                return Message.error(t('Unable to locate prev value in media library path configurations'));
              }
            };
            if (error) {
              Dialog.confirm({
                title: t('Potential risks have been detected'),
                content: (
                  <>
                    <div>{error}</div>
                    <div>{t('Sure to continue?')}</div>
                  </>
                ),
                onOk: () => new Promise((resolve, reject) => {
                  save(resolve, reject);
                }),
                closeable: true,
              });
            } else {
              save();
            }
          } else {
            const dialog = Dialog.show({
              title: t('Validating'),
              footer: false,
              closeable: false,
            });
            BApi.mediaLibrary.validatePathConfiguration(value)
              .then((t) => {
                setPathConfigurationChanged(false);
                setTestResult(t.data || {});
              })
              .finally(() => {
                dialog.hide();
              });
          }
        }}
      >{t(validate ? testThenSave ? 'Test then save' : 'Save' : 'Save without validation')}
      </Button>
    );
  };

  const close = useCallback(() => {
    setValue(undefined);
  }, []);

  const renderPsc = () => {
    const simpleMatchers = {
      [ResourceProperty.RootPath]: true,
      [ResourceProperty.Resource]: false,
      [ResourceProperty.ParentResource]: false,
      [ResourceProperty.ReleaseDt]: false,
      [ResourceProperty.Publisher]: false,
      [ResourceProperty.Name]: false,
      // [ResourceProperty.Language]: false,
      [ResourceProperty.Volume]: false,
      [ResourceProperty.Original]: false,
      [ResourceProperty.Series]: false,
      [ResourceProperty.Tag]: false,
      // [MatcherType.Introduction]: false,
      [ResourceProperty.Rate]: false,
      [ResourceProperty.CustomProperty]: false,
    };
    const matchers = Object.keys(simpleMatchers)
      .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
        ts.push(new PathSegmentConfigurationPropsMatcherOptions({
          property: parseInt(t, 10),
          readonly: simpleMatchers[t],
        }));
        return ts;
      }, []);

    const onClose = () => {
      setPscData(undefined);
    };

    return (
      <Dialog
        v2
        width={1200}
        visible={!!pscData}
        closeable
        onClose={onClose}
        onCancel={onClose}
        top={20}
        onOk={() => {
          const newValue = {
            ...value,
            ...pscData?.value,
            // todo: legacy
            regex: undefined,
          };
          setValue(newValue);
          setPscData(undefined);
        }}
      >
        <PathSegmentsConfiguration
          isDirectory={false}
          segments={pscData?.segments!}
          onChange={value => {
            pscData!.value = value;
          }}
          matchers={matchers}
          defaultValue={pscData?.value}
        />
      </Dialog>
    );
  };

  return (
    <>
      {renderPsc()}
      <Dialog
        visible={value != undefined}
        onClose={close}
        onCancel={close}
        afterClose={afterClose}
        footer={false}
        closeable
        className={'pc-dialog'}
        title={t('Path configuration')}
      >
        <div className="path-configuration-validator">
          <div className="main">
            <section className="root">
              <div className="title">{t('Root path')}</div>
              <div className="items">
                <div className="path-container">
                  <Button
                    text
                    type={'primary'}
                    onClick={() => {
                      BApi.gui.openFolderSelector()
                        .then((t) => {
                          if (t.data) {
                            const newPc = {
                              ...value,
                              path: t.data,
                            };
                            setValue(newPc);
                            checkPathRelations(newPc);
                          }
                        });
                    }}
                  >{value?.path}
                  </Button>
                  <ClickableIcon
                    colorType={'normal'}
                    type="folder-open"
                    onClick={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      BApi.tool.openFileOrDirectory({ path: value.path });
                    }}
                  />
                  {checkingPathRelations && (
                    <Icon type={'loading'} />
                  )}
                </div>
                {relativeLibraries?.length > 0 && (
                  <div className={'conflict'}>
                    {t('Warning: path may be conflict with other media libraries:')} {relativeLibraries.map((a) => `${a.categoryName}:${a.name}`)
                    .join(', ')}
                  </div>
                )}
              </div>
            </section>
            <section className="resource-locator">
              <div className="title">
                {t('Setup how to find resources and properties')}
                &emsp;
                <Balloon.Tooltip
                  trigger={
                    (
                      <Button
                        type={'primary'}
                        size={'small'}
                        text
                        onClick={() => {
                          FileSystemSelectorDialog.show({
                            // targetType: 'folder',
                            startPath: value.path,
                            onSelected: (e) => {
                              const std = standardizePath(e.path)!;
                              const stdPrev = standardizePath(value.path);
                              if (stdPrev && std.startsWith(stdPrev)) {
                                const segments = splitPathIntoSegments(e.path);
                                const pscValue: IPscValue = {};

                                // todo: 修复数据前临时调整数据，后端修复后移除
                                if (value) {
                                  pscValue.rpmValues = JSON.parse(JSON.stringify(value.rpmValues ?? []));
                                  pscValue.path = value.path;
                                  const resourceSegment = value.rpmValues?.find((s) => s.property == ResourceProperty.Resource);
                                  if (!resourceSegment && value.regex) {
                                    const layer = parseLayerCountFromLayerBasedPathRegexString(value.regex!, true);
                                    const rv: IPscMatcherValue = {
                                      property: ResourceProperty.Resource,
                                      valueType: layer > 0 ? ResourceMatcherValueType.Layer : ResourceMatcherValueType.Regex,
                                    };
                                    if (layer > 0) {
                                      rv.layer = layer;
                                      rv.valueType = ResourceMatcherValueType.Layer;
                                    } else {
                                      rv.regex = value.regex;
                                      rv.valueType = ResourceMatcherValueType.Regex;
                                    }

                                    (pscValue.rpmValues)!.push(rv);
                                  }
                                }
                                setPscData({
                                  segments,
                                  value: pscValue,
                                });
                              } else {
                                Dialog.error({
                                  title: t('Error'),
                                  content: t('You can select a file out of root path. If you want to change the root path of your library, you should click on your root path.'),
                                  closeable: true,
                                });
                              }
                            },
                          });

                          // BApi.gui.openFileSelector({ initialDirectory: value.path })
                          //   .then((a) => {
                          //     if (a.data) {
                          //
                          //     }
                          //   });
                        }}
                      >
                        {t('Setup')}
                      </Button>
                    )
                  }
                  align={'t'}
                  triggerType={'hover'}
                >
                  {t('To setup this item, you should pick up a file first in your root path.')}
                  <br />
                  {t('If you want to populate properties as many as possible, you should pick up a file with more layers in path.')}
                </Balloon.Tooltip>

              </div>
              <div className="items">
                {(value?.rpmValues || []).map((s, i) => {
                  return (
                    <div className={'segment'}>
                      <div className="label">
                        <IceLabel
                          inverse={false}
                          status={s.property == ResourceProperty.Resource ? 'primary' : 'default'}
                        >{t(ResourceProperty[s.property])}{s.property == ResourceProperty.CustomProperty ? `:${s.key}` : ''}</IceLabel>
                      </div>
                      <div className="value">
                        {s.regex ? value?.regex : s.isReverse ? t('The {{layer}} layer to the resource', { layer: s.layer }) : t('The {{layer}} layer after root path', { layer: s.layer })}
                      </div>
                    </div>
                  );
                })}
              </div>
            </section>
            <section className="tag-indicator">
              <div className="title">
                {t('Add fixed tags for resources')}
                &emsp;
                <Button
                  type={'primary'}
                  size={'small'}
                  text
                  onClick={() => {
                    let tmpTags: any;
                    Dialog.show({
                      content: (
                        <TagSelector
                          onChange={(value, tags) => {
                            tmpTags = value.tagIds.map(id => tags[id]);
                          }}
                          defaultValue={{ tagIds: value?.fixedTagIds }}
                        />
                      ),
                      onOk: () => new Promise((resolve, reject) => {
                        value.fixedTagIds = tmpTags?.map((a) => a.id);
                        value.fixedTags = tmpTags;
                        setValue({
                          ...value,
                        });
                        resolve(undefined);
                      }),
                      closeable: true,
                    });
                  }}
                >
                  {t('Setup')}
                </Button>
              </div>
              <div className="items">
                {value?.fixedTags?.length > 0 && (
                  <div className="tags">
                    {value.fixedTags.map((t) => {
                      return (
                        <IceLabel
                          inverse={false}
                          status={'default'}
                        >{(t.groupNamePreferredAlias ?? t.groupName)}:{(t.namePreferredAlias ?? t.name)}
                        </IceLabel>
                      );
                    })}
                  </div>
                )}
              </div>
            </section>
            <div className="opt">
              {error ? (
                <Balloon.Tooltip
                  trigger={renderSubmitButton(true)}
                  align={'t'}
                >
                  {error}
                </Balloon.Tooltip>
              ) : (
                <>
                  {renderSubmitButton(true)}
                  {renderSubmitButton(false)}
                </>
              )}
            </div>
            {testResult && (
              <section className="test-result">
                {t('Found top {{count}} resources. (shows up to 100 results)', { count: (testResult.entries || []).length })}
                {renderTestResultError(testResult)}
                {testResult.entries?.length > 0 && (
                  <div className="entries">
                    {
                      testResult.entries.map((e, i) => {
                        const {
                          globalMatchedValues = {},
                          fixedTags = [],
                          isDirectory,
                        } = e;

                        console.log(globalMatchedValues);

                        const segments: any[] = [];
                        for (let j = 0; j < e.segmentAndMatchedValues?.length; j++) {
                          const ps = e.segmentAndMatchedValues[j];
                          const types: ResourceProperty[] = [];
                          if (j == e.segmentAndMatchedValues.length - 1) {
                            types.push(ResourceProperty.Resource);
                          }
                          if (ps.properties?.length > 0) {
                            for (const p of ps.properties) {
                              // if (mc.property == ResourceProperty.CustomProperty) {
                              //   types.push(`${t('Custom property')}:${mc.customPropertyKey}`);
                              // } else {
                              //   types.push(t(ResourceProperty[mc.property]));
                              // }
                              types.push(p);
                            }
                          }
                          // console.log(types, ps);
                          segments.push(
                            <div className={'segment'}>
                              {types.length > 0 && (
                                <div className={`types ${types.length > 1 ? 'conflict' : ''}`}>
                                  {types.map(x => t(ResourceProperty[x]))
                                    .join(', ')}
                                </div>
                              )}
                              <div className="value">{ps.value}</div>
                            </div>,
                          );
                          if (j != e.segmentAndMatchedValues.length - 1) {
                            segments.push(
                              <span className={'path-separator'}>/</span>,
                            );
                          }
                        }

                        const globalMatchesElements: any[] = [];
                        if (globalMatchedValues) {
                          Object.keys(globalMatchedValues)
                            .forEach(propertyStr => {
                              const property: ResourceProperty = parseInt(propertyStr, 10);
                              const values = globalMatchedValues[property] || [];
                              if (values?.length > 0) {
                                globalMatchesElements.push(
                                  <div className={'gm'}>
                                    <div className="property">{t(ResourceProperty[property])}</div>
                                    <div className="values">
                                      {values.map(v => (
                                        <IceLabel inverse={false} status={'default'}>{v}</IceLabel>
                                      ))}
                                    </div>
                                  </div>,
                                );
                              }
                            });
                        }

                        if (fixedTags) {
                          if (fixedTags.length > 0) {
                            globalMatchesElements.push(
                              <div className={'gm'}>
                                <div className="property">{t('Fixed tags')}</div>
                                <div className="values">
                                  {fixedTags.map(t => (
                                    <IceLabel inverse={false} status={'default'}>{t.displayName}</IceLabel>
                                  ))}
                                </div>
                              </div>,
                            );
                          }
                        }

                        return (
                          <div className={'entry'}>
                            <div className="no">
                              <IceLabel status={'default'} inverse={false} className="no">{i + 1}</IceLabel>
                            </div>
                            <div className="result">
                              <div className="segments">
                                <IceLabel
                                  className="fs-type"
                                  inverse={false}
                                  status={isDirectory ? 'success' : 'info'}
                                >{t(isDirectory ? 'Directory' : 'File')}</IceLabel>
                                {segments}
                              </div>
                              {globalMatchesElements.length > 0 && (
                                <div className="global-matches">
                                  {globalMatchesElements}
                                </div>
                              )}
                            </div>
                          </div>
                        );
                      })
                    }
                  </div>
                )}
              </section>
            )}
          </div>
        </div>
      </Dialog>
    </>
  );
};
