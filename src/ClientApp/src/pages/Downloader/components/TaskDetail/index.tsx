import React, { useEffect, useState } from 'react';
import {
  BilibiliDownloadTaskType,
  bilibiliDownloadTaskTypes,
  DependentComponentStatus,
  ExHentaiDownloadTaskType,
  exHentaiDownloadTaskTypes,
  PixivDownloadTaskType,
  pixivDownloadTaskTypes,
  ResponseCode,
  ThirdPartyId,
  thirdPartyIds,
} from '@/sdk/constants';
import NameIcon from '@/pages/Downloader/components/NameIcon';

import './index.scss';
import { Balloon, Button, Dialog, Divider, Icon, Input, NumberPicker, Select, Tag } from '@alifd/next';
import i18n from 'i18next';
import { useUpdateEffect } from 'react-use';
import {
  CreateDownloadTask,
  GetBiliBiliFavorites,
  GetBilibiliOptions,
  GetDownloadTask,
  GetExHentaiOptions,
  GetPixivOptions,
  PutDownloadTask,
} from '@/sdk/apis';
import Configurations from '@/pages/Downloader/components/Configurations';
import CustomIcon from '@/components/CustomIcon';
import FileSelector from '@/components/FileSelector';
import store from '@/store';
import dependentComponentIds from '@/core/models/Constants/DependentComponentIds';
import { useTranslation } from 'react-i18next';

const timeUnits = [
  {
    label: 'Day',
    value: 'd',
    seconds: 60 * 60 * 24,
  },
  {
    label: 'Hour',
    value: 'h',
    seconds: 60 * 60,
  },
  {
    label: 'Minute',
    value: 'm',
    seconds: 60,
  },
  {
    label: 'Second',
    value: 's',
    seconds: 1,
  },
].map((a) => ({
  ...a,
  label: `${i18n.t(a.label)}(s)`,
}));

const createIntervalFormItem = (value, onValueChange, unit, onUnitChange) => {
  return {
    label: 'Interval',
    tip: 'Interval for checking for new contents. ',
    component: (
      <Input.Group addonAfter={(
        <Select
          style={{ width: 150 }}
          dataSource={timeUnits}
          value={unit}
          onChange={onUnitChange}
        />
      )}
      >
        <NumberPicker
          style={{ width: 200 }}
          step={1}
          value={value}
          onChange={onValueChange}
        />
      </Input.Group>
    ),
    className: 'interval',
  };
};

const convertTaskToForm = (task) => {
  let intervalUnit = 'd';
  let intervalValue;
  if (task.interval > 0) {
    for (const tu of timeUnits) {
      const r = task.interval / tu.seconds;
      if (r == Math.floor(r)) {
        intervalUnit = tu.value;
        intervalValue = r;
        break;
      }
    }
  }
  return {
    ...task,
    intervalUnit,
    intervalValue,
    keyAndNames: {
      [task.key]: task.name,
    },
  };
};

export default ({
  onCreatedOrUpdated,
  onClose,
  taskId,
}) => {
  const { t } = useTranslation();

  const [thirdPartyId, setThirdPartyId] = useState<ThirdPartyId | undefined>();
  const [type, setType] = useState<number | undefined>();
  const [form, setForm] = useState<any>({
    intervalUnit: 'd',
  });

  const [configurationsVisible, setConfigurationsVisible] = useState(false);

  const [bilibiliFavorites, setBilibiliFavorites] = useState([]);
  const [gettingBilibiliFavorites, setGettingBilibiliFavorites] = useState(false);

  const isCreatingTask = taskId == 0;
  const disableEditing = !isCreatingTask;

  const [exHentaiOptions, setExHentaiOptions] = useState({});
  const [pixivOptions, setPixivOptions] = useState({});
  const [bilibiliOptions, setBilibiliOptions] = useState({});

  const dependentComponentContexts = store.useModelState('dependentComponentContexts');
  const luxState = dependentComponentContexts?.find(d => d.id == dependentComponentIds.Lux);
  const ffmpegState = dependentComponentContexts?.find(d => d.id == dependentComponentIds.FFMpeg);

  const createDefaultFormValue = () => {
    let options;
    switch (thirdPartyId) {
      case ThirdPartyId.Pixiv:
        options = pixivOptions;
        break;
      case ThirdPartyId.Bilibili:
        options = bilibiliOptions;
        break;
      case ThirdPartyId.ExHentai:
        options = exHentaiOptions;
        break;
    }

    return {
      intervalUnit: 'd',
      downloadPath: options?.downloader?.defaultPath,
    };
  };

  useEffect(() => {
    GetExHentaiOptions()
      .invoke((a) => {
        setExHentaiOptions(a.data);
      });

    GetPixivOptions()
      .invoke((a) => {
        setPixivOptions(a.data);
      });

    GetBilibiliOptions()
      .invoke((a) => {
        setBilibiliOptions(a.data);
      });

    if (taskId > 0) {
      GetDownloadTask({
        id: taskId,
      })
        .invoke((b) => {
          if (!b.code) {
            const task = b.data;
            setThirdPartyId(task.thirdPartyId);
            setType(task.type);
            const f = convertTaskToForm(task);
            setForm(f);
          }
        });
    }
  }, []);

  useUpdateEffect(() => {
    if (taskId == 0) {
      setType(undefined);
      setForm(createDefaultFormValue());
    }
  }, [thirdPartyId]);

  const getBilibiliFavorites = (cb: any = undefined) => {
    if (!gettingBilibiliFavorites) {
      setGettingBilibiliFavorites(true);
      // in case there is a delay after new options saving
      setTimeout(() => {
        GetBiliBiliFavorites({
          $config: {
            ignoreError: (rsp) => true,
          },
        })
          .invoke((a) => {
            if (!a.code) {
              setBilibiliFavorites(a.data);
              cb && cb();
            }
          })
          .finally(() => {
            setGettingBilibiliFavorites(false);
          });
      }, 500);
    }
  };

  useUpdateEffect(() => {
    if (thirdPartyId == ThirdPartyId.Bilibili) {
      if (type == BilibiliDownloadTaskType.Favorites) {
        getBilibiliFavorites();
      }
    }
    if (thirdPartyId == ThirdPartyId.ExHentai) {
      form.key = type == ExHentaiDownloadTaskType.Watched ? 'https://exhentai.org/watched' : undefined;
    }
    setForm({ ...form });
  }, [type]);

  const renderTypes = () => {
    let types: any[] = [];
    switch (thirdPartyId) {
      case ThirdPartyId.ExHentai:
        types = exHentaiDownloadTaskTypes;
        break;
      case ThirdPartyId.Pixiv:
        types = pixivDownloadTaskTypes;
        break;
      case ThirdPartyId.Bilibili:
        types = bilibiliDownloadTaskTypes;
        break;
    }

    return types.map((type) => {
      console.log(type, type);
      return (
        <Tag.Selectable
          className={'type'}
          onChange={(checked) => {
            if (checked) {
              setType(type.value);
            }
          }}
          checked={type == type.value}
          disabled={disableEditing}
        >
          {t(type.label)}
        </Tag.Selectable>
      );
    });
  };

  const createSimpleFormInput = (multiple, placeholder: string | undefined = undefined) => {
    const Component = multiple ? Input.TextArea : Input;
    const createKeyAndNamesOnChange = multiple ? (s) => {
      const keyAndNames = {};
      if (s) {
        for (const u of s.split('\n')) {
          keyAndNames[u] = null;
        }
      }
      return keyAndNames;
    } : (s) => {
      return {
        [s]: null,
      };
    };

    const value = Object.keys(form.keyAndNames || {})
      .join('\n');

    const onChange = (s) => {
      const kn = createKeyAndNamesOnChange(s);
      console.log(kn);
      setForm({
        ...form,
        keyAndNames: kn,
      });
    };

    return (
      <Component
        className={'full'}
        onChange={onChange}
        value={value}
        placeholder={placeholder == undefined ? undefined : t(placeholder)}
        disabled={disableEditing}
      />
    );
  };

  const renderFormItems = () => {
    if (!thirdPartyId || !type) {
      return;
    }

    const items: any[] = [];

    let hasPages = false;
    switch (thirdPartyId) {
      case ThirdPartyId.ExHentai:
        switch (type) {
          case ExHentaiDownloadTaskType.SingleWork:
            items.push({
              label: 'Urls',
              component: createSimpleFormInput(true, 'One url for one line'),
            });
            break;
          case ExHentaiDownloadTaskType.Watched:
            items.push({
              label: 'Url',
              component: createSimpleFormInput(false, '\'https://exhentai.org/watched\' by default'),
            });
            break;
          case ExHentaiDownloadTaskType.List:
            items.push({
              label: 'Url',
              component: createSimpleFormInput(false),
            });
            hasPages = true;
            break;
        }
        break;
      case ThirdPartyId.Pixiv: {
        switch (type) {
          case PixivDownloadTaskType.Search:
            items.push({
              label: 'Url',
              component: createSimpleFormInput(false),
              tip: (
                <>
                  {t('Novel is not supported now, please make sure your url likes: ')}
                  <br />
                  https://www.pixiv.net/tags/azurlane
                  <br />
                  https://www.pixiv.net/tags/azurlane/top
                  <br />
                  https://www.pixiv.net/tags/azurlane/illustrations
                  <br />
                  https://www.pixiv.net/tags/azurlane/manga
                  <br />
                  https://www.pixiv.net/tags/azurlane/artworks?order=popular_male_d&mode=safe
                </>
              ),
            });
            hasPages = true;
            break;
          case PixivDownloadTaskType.Ranking:
            items.push({
              label: 'Url',
              component: createSimpleFormInput(false),
              tip: (
                <>
                  {t('Novel is not supported now, please make sure your url likes: ')}
                  <br />
                  https://www.pixiv.net/ranking.php
                  <br />
                  https://www.pixiv.net/ranking.php?mode=daily_r18
                </>
              ),
            });
            break;
          case PixivDownloadTaskType.Following:
            items.push({
              label: 'Url',
              component: createSimpleFormInput(false),
              tip: (
                <>
                  {t('Novel is not supported now, please make sure your url likes: ')}
                  <br />
                  https://www.pixiv.net/bookmark_new_illust.php
                  <br />
                  https://www.pixiv.net/bookmark_new_illust_r18.php
                  <br />
                  https://www.pixiv.net/bookmark_new_illust_r18.php?p=3
                </>
              ),
            });
            hasPages = true;
            break;
        }
        break;
      }
      case ThirdPartyId.Bilibili: {
        switch (type) {
          case BilibiliDownloadTaskType.Favorites: {
            items.push({
              label: 'Favorites',
              component: (
                gettingBilibiliFavorites ? (
                  <Icon type={'loading'} />
                )
                  : bilibiliFavorites.length > 0 ? bilibiliFavorites.map((f: any) => {
                    return (
                      <Tag.Selectable
                        onChange={(c) => {
                          const kn = form.keyAndNames || {};
                          if (c) {
                            kn[f.id] = f.title;
                          } else {
                            delete kn[f.id];
                          }
                          setForm({
                            ...form,
                            keyAndNames: kn,
                          });
                        }}
                        checked={f.id in (form.keyAndNames || {})}
                        disabled={disableEditing}
                      >
                        {f.title}({f.media_count})
                      </Tag.Selectable>
                    );
                  }) : (
                    <div>
                      {t('Unable to get bilibili favorites, make sure your cookie is set properly')}
                      <Button
                        onClick={() => {
                          setConfigurationsVisible(true);
                        }}
                        style={{ marginLeft: 5 }}
                        type={'primary'}
                        text
                      >{t('Set now')}
                      </Button>
                    </div>
                  )
              ),
            });
            hasPages = true;
            break;
          }
        }
      }
    }

    items.push({
      label: 'Download path',
      component: (
        <FileSelector
          type={'folder'}
          value={form.downloadPath}
          multiple={false}
          onChange={(downloadPath) => {
            setForm({
              ...form,
              downloadPath,
            });
          }}
        />
      ),
    });

    const intervalComponent = createIntervalFormItem(form.intervalValue, (v) => {
      setForm({
        ...form,
        intervalValue: v,
      });
    }, form.intervalUnit, (u) => {
      setForm({
        ...form,
        intervalUnit: u,
      });
    });
    items.push(intervalComponent);

    if (hasPages) {
      items.push({
        label: 'Pages',
        tip: 'Set a page range if you don\'t want to download them all. The minimal page number is 1.',
        className: 'pages',
        component: (
          <>
            <NumberPicker
              min={1}
              step={1}
              value={form.startPage}
              onChange={(v) => {
                setForm({
                  ...form,
                  startPage: v,
                });
              }}
            />
            ~
            <NumberPicker
              min={form.startPage || 1}
              step={1}
              value={form.endPage}
              onChange={(v) => {
                setForm({
                  ...form,
                  endPage: v,
                });
              }}
            />
          </>
        ),
      });
    }

    items.push({
      label: 'Checkpoint',
      tip: 'You can set the previous downloading checkpoint manually to make the downloader start the downloading task from it. ' +
        'In most cases, you should let this field set by downloader automatically. ' +
        'Each downloader has its own checkpoint format, and invalid checkpoint data will be ignored. You can find samples on our online document.',
      component: (
        <Input.TextArea
          className={'full'}
          value={form.checkpoint}
          onChange={(v) => {
            setForm({
              ...form,
              checkpoint: v,
            });
          }}
        />
      ),
    });

    items.push({
      label: 'Global configurations',
      component: (
        <Button
          type={'primary'}
          text
          onClick={() => {
            setConfigurationsVisible(true);
          }}
        >
          {t('Check')}
        </Button>
      ),
    });

    return (
      <>
        <Divider />
        <div className={'grid form'}>
          {items.map((i) => {
            const tipIsString = typeof i.tip === 'string' || i.tip instanceof String;
            return (
              <>
                <div className={'label'}>
                  {t(i.label)}
                  {i.tip && (
                    <Balloon.Tooltip
                      align={'t'}
                      // needAdjust
                      // shouldUpdatePosition
                      // autoAdjust
                      // v2
                      trigger={(
                        <CustomIcon type={'question-circle'} />
                      )}
                      triggerType={'hover'}
                    >{tipIsString ? t(i.tip) : i.tip}
                    </Balloon.Tooltip>
                  )}
                </div>
                <div className={`value ${i.className}`}>
                  {i.component}
                </div>
              </>
            );
          })}
        </div>
      </>
    );
  };

  const buildTask = () => {
    let interval;
    if (form.intervalValue && form.intervalUnit) {
      const tu = timeUnits.find((a) => a.value == form.intervalUnit);
      if (tu) {
        interval = parseInt(form.intervalValue) * tu.seconds;
      }
    }
    return {
      ...form,
      interval,
      thirdPartyId,
      type,
    };
  };

  const createDownloadTask = (forceCreating = false, onSuccess: any = undefined, onFail: any = undefined) => {
    const model = buildTask();
    model.forceCreating = forceCreating;
    console.log(model, JSON.stringify(model));
    CreateDownloadTask({
      model,
    })
      .invoke((a) => {
        if (!a.code) {
          onSuccess && onSuccess();
          onCreatedOrUpdated && onCreatedOrUpdated();
        } else if (a.code == ResponseCode.Conflict) {
          Dialog.confirm({
            title: t('Duplicates are found'),
            content: (
              <pre>
                {a.message}
              </pre>
            ),
            onOk: () => new Promise((resolve, reject) => {
              createDownloadTask(true, resolve, reject);
            }),
          });
        }
        onFail && onFail();
      });
  };

  console.log(thirdPartyId, type, form);

  return (
    <>
      {configurationsVisible && (
        <Configurations
          onSaved={() => {
            if (thirdPartyId == ThirdPartyId.Bilibili && type == BilibiliDownloadTaskType.Favorites) {
              getBilibiliFavorites();
            }
            setConfigurationsVisible(false);
          }}
          onClose={() => {
            setConfigurationsVisible(false);
          }}
        />
      )}
      <Dialog
        title={taskId > 0 ? t('Download task') : t('Creating download task')}
        className={'download-task-detail'}
        visible
        closeable
        onOk={() => {
          if (isCreatingTask) {
            createDownloadTask();
          } else {
            PutDownloadTask({
              id: taskId,
              model: buildTask(),
            })
              .invoke((a) => {
                if (!a.code) {
                  onCreatedOrUpdated();
                }
              });
          }
        }}
        onCancel={onClose}
        onClose={onClose}
      >
        <div className={'grid'}>
          <div className="label">
            {t('Site')}
          </div>
          <div className="sources value">
            {thirdPartyIds.map((tpId) => {
              // todo: This is a temporary solution.
              // Tag.Selectable with disabled status will block the hover event, which makes the balloon not working.
              let blockChanging = false;
              let disabledTip: string | undefined;
              if (tpId.value == ThirdPartyId.Bilibili) {
                if (luxState?.status != DependentComponentStatus.Installed || ffmpegState?.status != DependentComponentStatus.Installed) {
                  blockChanging = true;
                  disabledTip = (
                    t('This function is not working because lux or ffmpeg is not found, check them in system configurations')
                  );
                }
              }

              const tag = (
                <Tag.Selectable
                  className={'source'}
                  size={'large'}
                  onChange={(checked) => {
                    if (blockChanging) {
                      return;
                    }
                    if (checked) {
                      setThirdPartyId(tpId.value);
                    }
                  }}
                  disabled={disableEditing}
                  checked={thirdPartyId == tpId.value}
                >
                  <img src={NameIcon[tpId.value]} alt="" />
                  <span>{tpId.label}</span>
                  {disabledTip && (
                    <Balloon.Tooltip
                      trigger={(
                        <CustomIcon type={'warning-circle'} />
                      )}
                      triggerType={'hover'}
                      align={'t'}
                    >
                      {disabledTip}
                    </Balloon.Tooltip>
                  )}
                </Tag.Selectable>
              );
              return tag;
            })}
          </div>
          {thirdPartyId && (
            <>
              <div className="label">
                {t('Type')}
              </div>
              <div className="types value">
                {renderTypes()}
              </div>
            </>
          )}
        </div>
        {renderFormItems()}
      </Dialog>
    </>
  );
};
