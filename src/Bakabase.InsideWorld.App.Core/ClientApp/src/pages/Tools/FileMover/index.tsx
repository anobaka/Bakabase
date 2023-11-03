import React, { useEffect, useState } from 'react';
import { Balloon, Dialog, Dropdown, Icon, Menu, Message, Switch, Table, TimePicker2 } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { OpenFileOrDirectory } from '@/sdk/apis';
import FileSelector from '@/components/FileSelector';
import './index.scss';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import CustomIcon from '@/components/CustomIcon';
import AnimatedArrow from '@/components/AnimatedArrow';
import BApi from '@/sdk/BApi';
import store from '@/store';
import ClickableIcon from '@/components/ClickableIcon';

interface IValue {
  targets: {
    path: string;
    sources: string[];
  }[];
  enabled: boolean;
  delay?: string;
}

class Value implements IValue {
  delay?: string;
  enabled: boolean = false;
  targets: { path: string; sources: string[] }[] = [];
}

export default () => {
  const { t } = useTranslation();
  const [preferredTarget, setPreferredTarget] = useState();
  const [preferredSource, setPreferredSource] = useState();

  const [value, setValue] = useState<IValue>(new Value());

  const progresses = store.useModelState('fileMovingProgresses');

  console.log(progresses);

  const loadOptions = () => {
    BApi.options.getFileSystemOptions()
      .then(a => {
        const fm = a.data?.fileMover || {};
        const value = {
          delay: fm.delay as string,
          enabled: fm.enabled ?? false,
          targets: fm.targets?.map((t) => {
            return {
              path: t.path!,
              sources: t.sources || [],
            };
          }) || [],
        };
        // console.log('0-1 options loaded');
        setValue(value);
      });
  };

  useEffect(() => {
    loadOptions();
  }, []);

  const save = (patches = {}, cb?: () => any) => {
    const options = {
      ...(value || {}),
      ...(patches || {}),
    };

    BApi.options.patchFileSystemOptions({
      // @ts-ignore
      fileMover: options,
    })
      .then((a) => {
        if (!a.code) {
          Message.success(t('Saved'));
          loadOptions();
          cb && cb();
        }
      });
  };

  const addTarget = (targetPath, cb = () => {
  }) => {
    if (!targets.some((t) => t.path == targetPath)) {
      targets.push({
        path: targetPath,
        sources: [],
      });
      save({
        targets,
      }, () => {
        setPreferredTarget(targetPath);
        cb();
      });
    }
  };

  const updateTarget = (targetPath, newTargetPath, cb = () => {
  }) => {
    const targetIndex = targets.findIndex((a) => a.path == targetPath);
    const target = targets[targetIndex];
    target.path = newTargetPath;
    targets.splice(targetIndex, 1, target);
    save({
      targets,
    }, () => {
      setPreferredTarget(targetPath);
      cb();
    });
  };

  const addSource = (targetPath, sourcePath) => {
    const target = targets.find((a) => a.path == targetPath)!;
    if (!target.sources) {
      target.sources = [];
    }
    const { sources = [] } = target;
    if (sources.indexOf(sourcePath) == -1) {
      sources.push(sourcePath);
      save({
        targets,
      }, () => {
        setPreferredSource(sourcePath);
      });
    } else {
      setPreferredTarget(targetPath);
    }
  };

  const updateSource = (targetPath, sourcePath, newSourcePath) => {
    const sources = targets.find((a) => a.path == targetPath)?.sources || [];
    const idx = sources.indexOf(sourcePath);
    if (idx > -1) {
      if (sources.indexOf(newSourcePath) == -1) {
        sources[idx] = sourcePath;
        save({
          targets,
        });
      }
    } else {
      setPreferredSource(sourcePath);
    }
  };

  const {
    enabled = false,
    targets = [],
  } = value;

  const ds = targets.reduce((s: any[], t: any) => {
    const sources = (t.sources || []).slice();
    sources.push(null);
    const newArr = sources.map((x, i) => (
      {
        target: t.path,
        rowSpan: i == 0 ? sources.length : undefined,
        source: x,
      }
    ));
    newArr.sort((a, b) => {
      return (a == preferredSource ? -1 : 0) - (b == preferredSource ? -1 : 0);
    });
    if (t.path == preferredTarget) {
      return newArr.concat(s);
    } else {
      return s.concat(newArr);
    }
  }, []) || [];

  ds.splice(0, 0, {
    rowSpan: 1,
  });

  // console.log(value, ds);

  const renderCommonOperations = (path) => {
    return (
      <>
        <ClickableIcon
          type={'folder-open'}
          colorType={'normal'}
          onClick={() => {
            OpenFileOrDirectory({
              path,
            })
              .invoke();
          }}
        />
      </>
    );
  };

  return (
    <div className={'file-mover'}>
      <div className="opt">
        <div className="enable">
          <div className="label">
            {t(enabled ? 'Enabled' : 'Disabled')}
          </div>
          <Switch
            checked={enabled}
            onChange={(c) => {
              save({
                enabled: c,
              });
            }}
          />
        </div>
        <div className="delay">
          <Balloon.Tooltip
            trigger={(
              <div className={'label'}>
                {t('Delay')}
                <CustomIcon type={'question-circle'} />
              </div>
            )}
            triggerType={'hover'}
            align={'t'}
          >
            {t('Files or directories will be moved after the delayed time from the time they are created here. The delay is working for the first layer entries only.')}
          </Balloon.Tooltip>
          <TimePicker2
            value={value?.delay}
            onChange={(c) => {
              save({
                delay: c?.format('HH:mm:ss') ?? '00:05:00',
              });
            }}
          />
        </div>
      </div>
      <Table
        dataSource={ds}
        cellProps={(rowIndex, colIndex, dataIndex, record) => {
          if (record.rowSpan && (colIndex == 0 || colIndex == 1)) {
            return {
              rowSpan: record.rowSpan,
            };
          }
          return;
        }}
      >
        <Table.Column
          title={t('Target')}
          dataIndex={'target'}
          cell={(target, i) => {
            // console.log(`rendering table col ${i}-1`, target, i);
            return (
              <div className={'target'}>
                <div className="left">
                  <Dropdown
                    trigger={
                      <div>
                        <FileSelector
                          multiple={false}
                          type={'folder'}
                          value={target ?? null}
                          size={'small'}
                          onChange={(newPath) => {
                            if (!target) {
                              addTarget(newPath);
                            } else {
                              updateTarget(target, newPath);
                            }
                          }}
                        />
                      </div>
                    }
                    triggerType={['hover']}
                  >
                    <Menu>
                      <Menu.Item onClick={() => {
                        MediaLibraryPathSelector.show({
                          onSelect: (newPath) => {
                            if (!target) {
                              addTarget(newPath);
                            } else {
                              updateTarget(target, newPath);
                            }
                            return;
                          },
                        });
                      }}
                      >
                        {t('Select from media library')}
                      </Menu.Item>
                    </Menu>
                  </Dropdown>
                </div>
                {target && (
                  <div className={'right'}>
                    {renderCommonOperations(target)}
                    <ClickableIcon
                      colorType={'danger'}
                      type={'delete'}
                      onClick={() => {
                        Dialog.confirm({
                          title: t('Sure to remove?'),
                          v2: true,
                          onOk: () => new Promise(((resolve, reject) => {
                            save({
                              targets: targets.filter((a) => a.path != target),
                            }, () => {
                              resolve(undefined);
                            });
                          })),
                        });
                      }}
                    />
                  </div>
                )}
              </div>
            );
          }}
        />
        <Table.Column
          width={90}
          align={'center'}
          title={t('Moving direction')}
          cell={() => {
            return (
              <AnimatedArrow direction={'left'} />
            );
          }}
        />
        <Table.Column
          title={t('Source')}
          dataIndex={'source'}
          cell={(s, i, r) => {
            if (i > 0) {
              // console.log(`rendering table col ${i}-2`, s, i);
              const progress = progresses[s];
              return (
                <div className={'source'}>
                  <div className="left">
                    <FileSelector
                      multiple={false}
                      type={'folder'}
                      value={s}
                      size={'small'}
                      onChange={(newPath) => {
                        // console.log(s, newPath, r);
                        if (!s) {
                          // console.log(r.target, newPath);
                          addSource(r.target, newPath);
                        } else {
                          updateSource(r.target, s, newPath);
                        }
                      }}
                    />
                    {progress && (
                      <div className={'status'}>
                        {progress.error && (
                          <ClickableIcon
                            type={'warning-circle'}
                            colorType={'danger'}
                            onClick={() => {
                              Dialog.alert({
                                title: t('Error'),
                                v2: true,
                                width: 'auto',
                                closeMode: ['esc', 'close', 'mask'],
                                content: (
                                  <pre>
                                    {progress.error}
                                  </pre>
                                ),
                              });
                            }}
                          />
                        )}
                        {progress.moving && (
                          <Icon type={'loading'} />
                        )}
                        {progress.percentage > 0 && progress.percentage < 100 && (
                          `${progress.percentage}%`
                        )}
                        {progress.percentage == 100 && (
                          <CustomIcon type={'check-circle'} style={{ color: 'var(--theme-color-success)' }} />
                        )}
                      </div>
                    )}
                  </div>
                  {s && (
                    <div className={'right'}>
                      {renderCommonOperations(s)}
                      <ClickableIcon
                        type={'delete'}
                        colorType={'danger'}
                        onClick={() => {
                          Dialog.confirm({
                            title: t('Sure to remove?'),
                            v2: true,
                            onOk: () => new Promise(((resolve, reject) => {
                              const { target: targetPath } = r;
                              const target = targets.find((t) => t.path == targetPath)!;
                              target.sources = target.sources?.filter((a) => a != s);
                              save({
                                targets,
                              }, () => {
                                resolve(undefined);
                              });
                            })),
                          });
                        }}
                      />
                    </div>
                  )}
                </div>
              );
            }
            return;
          }}
        />
      </Table>
    </div>
  );
};
