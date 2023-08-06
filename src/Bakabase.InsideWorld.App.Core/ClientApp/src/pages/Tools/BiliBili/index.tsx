import React, { useEffect, useState } from 'react';
import { Badge, Balloon, Button, Card, Checkbox, Input, Message, Progress, Tag } from '@alifd/next';
import i18n from 'i18next';
import { GetAppOptions, GetBiliBiliFavorites, OpenFileOrDirectory, PatchAppOptions, ValidateCookie } from '@/sdk/apis';
import { CookieValidatorTarget, ProgressorStatus } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import {useProgressorHubConnection} from '@/components/ProgressorHubConnection';
import './index.scss';

const finishedStatusList = [
  ProgressorStatus.Complete,
  ProgressorStatus.Suspended,
];

const MaxCapacity = 1000;

export default () => {
  const [favorites, setFavorites] = useState([]);
  const [state, setState] = useState({});
  const [progress, setProgress] = useState({});
  const [options, setOptions] = useState({});
  const [favError, setFavError] = useState();
  const [validatingCookie, setValidatingCookie] = useState(false);

  const downloaderRef = useProgressorHubConnection('BiliBiliProgressor', (progress) => setProgress(progress), (state) => setState(state));

  const selectedFavorites = favorites.map((f) => options.biliBiliFavoritesToDownloadIds?.indexOf(f.id) > -1);
  const showProgress = state.status != ProgressorStatus.Idle && Object.keys(progress)
    .some((a) => progress[a]);
  const downloading = state.status == ProgressorStatus.Running;

  useEffect(() => {
    GetAppOptions().invoke((a) => {
      setOptions(a.data);
      if (a.data.biliBiliCookie) {
        getFavorites();
      }
    });
  }, []);

  const getFavorites = () => {
    GetBiliBiliFavorites()
      .invoke((b) => {
        if (b.code) {
          setFavError(i18n.t(b.message));
        } else {
          setFavorites(b.data || []);
          setFavError(undefined);
        }
      });
  };

  // console.log(state);

  const renderResult = () => {
    if (finishedStatusList.indexOf(state.status) > -1) {
      let className;
      let tip;
      switch (state.status) {
        case ProgressorStatus.Complete:
          className = 'successful';
          tip = 'All posts have been downloaded';
          break;
        case ProgressorStatus.Suspended:
          className = 'fail';
          tip = `Error occurred: ${state.message}`;
          break;
      }
      return (
        <pre className={`result ${className}`}>{i18n.t(tip)}</pre>
      );
    }
  };

  const saveOptions = (cb = undefined) => {
    PatchAppOptions({
      model: {
        biliBiliCookie: options.biliBiliCookie,
        biliBiliDownloadPath: options.biliBiliDownloadPath,
        biliBiliSchedulerEnabled: options.biliBiliSchedulerEnabled,
        biliBiliFavoritesToDownloadIds: options.biliBiliFavoritesToDownloadIds,
        biliBiliFavoritesToArchiveId: options.biliBiliFavoritesToArchiveId,
      },
    }).invoke((a) => {
      if (!a.code) {
        cb && cb();
      }
    });
  };

  return (
    <div className={'bilibili-page'}>
      <Card free>
        <Card.Header title={i18n.t('Download videos in favorites')} />
        <Card.Divider />
        <Card.Content>
          <div className="favorites">
            {favError ? (
              <span className={'error'}>{favError}</span>
            ) : (
              favorites.map((f) => {
                const checked = options.biliBiliFavoritesToDownloadIds?.some((a) => a == f.id);
                return (
                  <Tag.Selectable
                    key={f.id}
                    checked={checked}
                    onChange={(c) => {
                      options.biliBiliFavoritesToDownloadIds = options.biliBiliFavoritesToDownloadIds || [];
                      if (checked != c) {
                        if (checked) {
                          options.biliBiliFavoritesToDownloadIds = options.biliBiliFavoritesToDownloadIds.filter((e) => e != f.id);
                        } else {
                          options.biliBiliFavoritesToDownloadIds.push(f.id);
                        }
                      }
                      setOptions({
                        ...options,
                      });
                    }}
                  >
                    {f.title}
                    &nbsp;
                    <Badge overflowCount={999999} count={f.media_count} showZero />
                  </Tag.Selectable>
                );
              })
            )}
          </div>
          {showProgress && (
            <div className="progressor">
              <Progress percent={progress.percentage} shape={'circle'} progressive className={'progress'} />
              <div className="info">
                <div>
                  <div>{i18n.t('Time costs')}</div>
                  <div>{Math.ceil(progress.elapsedMilliseconds / 1000)}s</div>
                </div>
                <div>
                  <div>{i18n.t('Total posts count')}</div>
                  <div>{progress.totalCount}</div>
                </div>
                <div>
                  <div>{i18n.t('Downloaded count')}</div>
                  <div>{progress.doneCount}</div>
                </div>
                <div>
                  <div>{i18n.t('Invalid posts count')}</div>
                  <div>{progress.invalidCount}</div>
                </div>
                <div>
                  <div className={'label'}>
                    <CustomIcon type={'star'} />
                  </div>
                  <div>{progress.favoriteName}</div>
                </div>
                <div>
                  <div className={'label'}>
                    <CustomIcon type={'video'} />
                  </div>
                  <div>{progress.postName}</div>
                </div>
                <div>
                  <Progress className={'post-progressor'} percent={progress.postPercentage} progressive />
                </div>
              </div>
            </div>
          )}
          {renderResult()}
        </Card.Content>
        <Card.Divider />
        <Card.Actions>
          <div className="form">
            {favorites?.length > 0 && (
              <div className="item">
                <div className="label">
                  {i18n.t('Favorites to archive')}
                  <Balloon.Tooltip
                    trigger={<CustomIcon type={'question-circle'} />}
                  >
                    {i18n.t(`Downloaded posts will be moved to there, please make sure the capacity of favorites is enough, the default max capacity of favorites is ${MaxCapacity} for now`)}
                  </Balloon.Tooltip>
                </div>
                <div className="value">
                  <Tag.Group >
                    {favorites.map((f) => {
                      const checked = options.biliBiliFavoritesToArchiveId == f.id;

                      const targetFavorites = favorites.filter((f) => options.biliBiliFavoritesToDownloadIds?.some((d) => d == f.id));
                      const totalCount = targetFavorites?.reduce((s, t) => s + t.media_count, 0) ?? 0;
                      const disabled = f.media_count + totalCount > MaxCapacity;
                      const tag = (
                        <Tag.Selectable
                          key={f.id}
                          checked={checked}
                          disabled={disabled}
                          onChange={(c) => {
                            setOptions({
                              ...options,
                              biliBiliFavoritesToArchiveId: f.id,
                            });
                          }}
                        >
                          {f.title}
                          &nbsp;
                          <Badge overflowCount={999999} style={{ backgroundColor: 'rgb(135, 208, 104)' }} count={MaxCapacity - f.media_count} />
                        </Tag.Selectable>
                      );
                      if (disabled) {
                        return (
                          <Balloon.Tooltip trigger={tag} triggerType={'hover'}>
                            {i18n.t('Capacity is not enough')}
                          </Balloon.Tooltip>
                        );
                      } else {
                        return tag;
                      }
                    })}
                  </Tag.Group>
                </div>
              </div>
            )}
            <div className="item">
              <div className="label">Cookie</div>
              <div className="value">
                <Input.TextArea autoHeight value={options.biliBiliCookie} onChange={(v) => setOptions({ ...options, biliBiliCookie: v })} />
              </div>
            </div>
            <div className="item">
              <div className="label">{i18n.t('Download path')}</div>
              <div className="value">
                <Input value={options.biliBiliDownloadPath} onChange={(v) => setOptions({ ...options, biliBiliDownloadPath: v })} />
              </div>
            </div>
            <div className="item">
              <div className="label">{i18n.t('Enable scheduler')}</div>
              <div className="value">
                <Checkbox
                  checked={options.biliBiliSchedulerEnabled}
                  onChange={(v) => {
                    if (v) {
                      const targetFavorites = favorites.filter((f) => options.biliBiliFavoritesToDownloadIds?.some((d) => d == f.id));
                      if (!(targetFavorites?.length > 0)) {
                        return Message.error(i18n.t('Favorites to download is not selected'));
                      }
                      const totalCount = targetFavorites.reduce((s, t) => s + t.media_count, 0);
                      const favoritesToArchive = favorites.find((f) => f.id == options.biliBiliFavoritesToArchiveId);
                      if (!favoritesToArchive) {
                        return Message.error(i18n.t('Favorites to archive is not selected'));
                      }
                      const restCapacity = MaxCapacity - favoritesToArchive.media_count;
                      if (restCapacity < totalCount) {
                        return Message.error(`Rest capacity ${restCapacity} is less than required ${totalCount}`);
                      }
                      setOptions({
                        ...options,
                        biliBiliSchedulerEnabled: v,
                      });
                    } else {
                      setOptions({
                        ...options,
                        biliBiliSchedulerEnabled: v,
                      });
                    }
                  }}
                />
              </div>
            </div>
          </div>
          <Button
            type="normal"
            disabled={!(options?.biliBiliCookie?.length > 0) || validatingCookie}
            loading={validatingCookie}
            onClick={() => {
              setValidatingCookie(true);
              ValidateCookie({
                target: CookieValidatorTarget.BiliBili,
                cookie: options.biliBiliCookie,
              }).invoke((t) => {
                if (t.code) {
                  Message.error(`${i18n.t('Invalid cookie')}:${t.message}`);
                } else {
                  Message.success(i18n.t('Cookie is good'));
                }
              }).finally(() => {
                setValidatingCookie(false);
              });
            }}
          >
            {i18n.t('Validate cookie')}
          </Button>
          <Button
            type="normal"
            onClick={() => {
              saveOptions(() => {
                Message.success(i18n.t('Success'));
              });
            }}
          >
            {i18n.t('Save')}
          </Button>
          <Button
            type="normal"
            onClick={() => {
              saveOptions(() => {
                getFavorites();
              });
            }}
          >
            {i18n.t('Get favorites')}
          </Button>
          <Button
            type={'primary'}
            warning={downloading}
            disabled={!downloading && !(selectedFavorites?.length > 0)}
            onClick={() => {
              if (downloading) {
                if (confirm(i18n.t('Sure?'))) {
                  downloaderRef.current.stop();
                }
              } else {
                saveOptions(() => {
                  downloaderRef.current.start({
                    favoriteIds: options.biliBiliFavoritesToDownloadIds,
                  });
                });
              }
            }}
          >
            {i18n.t(downloading ? 'Stop' : 'Download videos in favorites')}
          </Button>
          <Button
            type={'normal'}
            onClick={() => {
              if (options.biliBiliDownloadPath) {
                OpenFileOrDirectory({ path: options.biliBiliDownloadPath }).invoke();
              } else {
                Message.error(i18n.t('BiliBili download path is invalid'));
              }
            }}
          >
            {i18n.t('Open directory for downloading')}
          </Button>
        </Card.Actions>
      </Card>
    </div>
  );
};
