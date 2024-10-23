import React, { useEffect, useState } from 'react';
import { Balloon, Dialog, Dropdown, Icon, Input, Menu, Message, Table, TimePicker2 } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { DeleteOutlined, FolderOpenOutlined, PlusCircleOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import FileSelector from '@/components/FileSelector';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import AnimatedArrow from '@/components/AnimatedArrow';
import BApi from '@/sdk/BApi';
import store from '@/store';
import ClickableIcon from '@/components/ClickableIcon';
import { Button, Switch, Tooltip } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import MediaLibraryPathSelectorV2 from '@/components/MediaLibraryPathSelectorV2';

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
  const { createPortal } = useBakabaseContext();

  const [preferredTarget, setPreferredTarget] = useState();
  const [preferredSource, setPreferredSource] = useState();

  const [value, setValue] = useState<IValue>(new Value());

  const [quickEditModeData, setQuickEditModeData] = useState<{
    path?: string;
    sources?: string[];
  }[]>();

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
    // sources.push(null);
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
        <Button
          size={'sm'}
          variant={'light'}
          isIconOnly
        >
          <FolderOpenOutlined
            onClick={() => {
              BApi.tool.openFileOrDirectory({
                path,
              });
            }}
            className={'text-base'}
          />
        </Button>
      </>
    );
  };

  const renderQuickEditMode = () => {
    return (
      <Table
        size={'small'}
        dataSource={quickEditModeData}
        className={'quick-edit-mode-table'}
      >
        <Table.Column
          title={t('Target')}
          dataIndex={'path'}
          cell={(path, i, r) => {
            if (i == quickEditModeData!.length - 1) {
              return (
                <Button
                  color={'primary'}
                  size={'sm'}
                  onClick={() => {
                    quickEditModeData?.splice(i, 0, {});
                    setQuickEditModeData([...quickEditModeData!]);
                  }}
                >{t('Add')}</Button>
              );
            }
            return (
              <div className={'target'}>
                <Input
                  placeholder={t('Target path')}
                  value={path}
                  onChange={v => {
                    quickEditModeData![i].path = v;
                    setQuickEditModeData([...quickEditModeData!]);
                  }}
                />
                <Button
                  size={'sm'}
                  variant={'light'}
                  isIconOnly
                  color={'danger'}
                >
                  <DeleteOutlined
                    onClick={() => {
                      quickEditModeData!.splice(i, 1);
                      setQuickEditModeData([...quickEditModeData!]);
                    }}
                    className={'text-base'}
                  />
                </Button>
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
          dataIndex={'sources'}
          cell={(s, i, r) => {
            if (i == quickEditModeData!.length - 1) {
              return;
            }
            return (
              <Input.TextArea
                size={'small'}
                placeholder={t('One path per line')}
                width={'100%'}
                autoHeight={{
                  minRows: 2,
                  maxRows: 100,
                }}
                value={s?.join('\n')}
                onChange={v => {
                  quickEditModeData![i].sources = v.split('\n');
                  setQuickEditModeData([...quickEditModeData!]);
                }}
              />
            );
          }}
        />
      </Table>
    );
  };

  const renderNormalEditMode = () => {
    return (
      <Table
        size={'small'}
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
                          defaultLabel={t('Add target path')}
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
                        createPortal(MediaLibraryPathSelectorV2, {
                          onSelect: (id, path) => {
                            if (!target) {
                              addTarget(path);
                            } else {
                              updateTarget(target, path);
                            }
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
                  <div className={'flex items-center gap-1'}>
                    <Tooltip content={t('Add source path')} >
                      <Button
                        size={'sm'}
                        variant={'light'}
                        isIconOnly
                      >
                        <PlusCircleOutlined
                          onClick={() => {
                            BApi.gui.openFolderSelector()
                              .then(a => {
                                if (a.data) {
                                  addSource(target, a.data);
                                }
                              });
                          }}
                          className={'text-base'}
                        />
                      </Button>
                    </Tooltip>
                    {renderCommonOperations(target)}
                    <Button
                      size={'sm'}
                      variant={'light'}
                      isIconOnly
                      color={'danger'}
                    >
                      <DeleteOutlined
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
                        className={'text-base'}
                      />
                    </Button>
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
                    <div className={'flex items-center gap-1'}>
                      {renderCommonOperations(s)}
                      <Button
                        size={'sm'}
                        variant={'light'}
                        isIconOnly
                        color={'danger'}
                      >
                        <DeleteOutlined
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
                          className={'text-base'}
                        />
                      </Button>
                    </div>
                  )}
                </div>
              );
            }
            return;
          }}
        />
      </Table>
    );
  };

  return (
    <div className={'file-mover'}>
      <div className="opt">
        <div className="left">
          <div className="enable">
            <div className="label">
              {t(enabled ? 'Enabled' : 'Disabled')}
            </div>
            <Switch
              size={'sm'}
              isSelected={enabled}
              onValueChange={(c) => {
                save({
                  enabled: c,
                });
              }}
            />
          </div>
          <div className="delay">
            <Tooltip
              placement={'bottom'}
              content={t('Files or directories will be moved after the delayed time from the time they are created here. The delay is working for the first layer entries only.')}
            >
              <div className={'flex items-center gap-1'}>
                {t('Delay')}
                <QuestionCircleOutlined className={'text-base'} />
              </div>
            </Tooltip>
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
        <div className="right">
          {quickEditModeData && (
            <Button
              color={'primary'}
              size={'sm'}
              onClick={() => {
                const newTargets = quickEditModeData?.filter(d => !!d.path);
                for (const nt of newTargets) {
                  if (nt.sources) {
                    nt.sources = nt.sources.filter(s => !!s);
                  }
                }
                save({
                  targets: newTargets,
                }, () => {
                  setQuickEditModeData(undefined);
                  loadOptions();
                });
              }}
            >
              {t('Save')}
            </Button>
          )}
          <Button
            size={'sm'}
            onClick={() => {
              if (quickEditModeData) {
                setQuickEditModeData(undefined);
              } else {
                const newData = JSON.parse(JSON.stringify(targets));
                newData.push({});
                setQuickEditModeData(newData);
              }
            }}
          >
            {quickEditModeData ? t('Back to normal edit mode') : t('Quick edit mode')}
          </Button>
        </div>
      </div>
      {quickEditModeData ? renderQuickEditMode() : renderNormalEditMode()}
    </div>
  );
};
