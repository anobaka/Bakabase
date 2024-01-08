import { Balloon, Button, Dialog, Icon, Message } from '@alifd/next';
import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import {
  buildLogger,
  parseLayerCountFromLayerBasedPathRegexString,
  splitPathIntoSegments,
  standardizePath,
} from '@/components/utils';
import type { IPscMatcherValue, IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import TagSelector from '@/components/TagSelector';
import './index.scss';
import PathSegmentsConfiguration, {
  PathSegmentConfigurationPropsMatcherOptions,
} from '@/components/PathSegmentsConfiguration';
import ClickableIcon from '@/components/ClickableIcon';
import { MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import BusinessConstants from '@/components/BusinessConstants';

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
  const [checkingPathRelations, setCheckingPathRelations] = useState(false);
  const [relativeLibraries, setRelativeLibraries] = useState<any[]>([]);
  const [pscData, setPscData] = useState<{ value: IPscValue; segments: string[]; isDirectory: boolean }>();


  useEffect(() => {
    const newValue = structuredClone(propsValue);
    if (newValue) {
      newValue.prevPath = newValue.path;
    }
    log('Props value changed', newValue);
    setValue(newValue);
  }, [propsValue]);

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

  let error;
  if (!value?.path) {
    error = 'Root path is not set';
  } else if (value?.rpmValues?.length > 0) {

  }
  if (error) {
    error = t(error);
  }

  const renderSubmitButton = () => {
    return (
      <Button
        type={'primary'}
        disabled={!!error}
        onClick={() => {
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
        }}
      >{t('Save')}
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

    // todo: if close mode includes mask, the click in inner balloon will trigger onClose of this dialog.
    // tried: set popupContainer of inner Balloon, but the balloon be the wrong position (V2 enabled) or shrinked by container (V2 disabled)
    return (
      <Dialog
        v2
        width={1200}
        visible={!!pscData}
        closeMode={['close', 'esc']}
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
          isDirectory={pscData?.isDirectory || false}
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

  const renderRpmValues = () => {
    const values = value?.rpmValues || [];
    if (values.length == 0) {
      return (
        <div className={'not-set'}>{t('You have to set this to discover resources')}</div>
      );
    }

    console.log(values);

    return (
      <div className="items">
        {(values).map((s, i) => {
          return (
            <div className={'segment'}>
              <div className="label">
                <SimpleLabel
                  status={s.property == ResourceProperty.Resource ? 'primary' : 'default'}
                >{t(ResourceProperty[s.property])}{s.property == ResourceProperty.CustomProperty ? `:${s.key}` : ''}</SimpleLabel>
              </div>
              <div className="value">
                {MatcherValue.ToString({
                  ...s,
                  type: s.valueType,
                })}
              </div>
            </div>
          );
        })}
      </div>
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
                      let startPath: string | undefined;
                      if (value.path) {
                        const segments = splitPathIntoSegments(value.path);
                        startPath = segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator);
                      }
                      FileSystemSelectorDialog.show({
                        startPath: startPath,
                        targetType: 'folder',
                        onSelected: e => {
                          const newPc = {
                            ...value,
                            path: e.path,
                          };
                          setValue(newPc);
                          checkPathRelations(newPc);
                        },
                        defaultSelectedPath: value.path,
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
                            // targetType: 'file',
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
                                  isDirectory: e.isDirectory,
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
              {renderRpmValues()}
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
                        <SimpleLabel
                          status={'default'}
                        >{(t.groupNamePreferredAlias ?? t.groupName)}:{(t.namePreferredAlias ?? t.name)}
                        </SimpleLabel>
                      );
                    })}
                  </div>
                )}
              </div>
            </section>
            <div className="opt">
              {error ? (
                <Balloon.Tooltip
                  trigger={renderSubmitButton()}
                  align={'t'}
                >
                  {error}
                </Balloon.Tooltip>
              ) : (
                <>
                  {renderSubmitButton()}
                </>
              )}
            </div>
          </div>
        </div>
      </Dialog>
    </>
  );
};
