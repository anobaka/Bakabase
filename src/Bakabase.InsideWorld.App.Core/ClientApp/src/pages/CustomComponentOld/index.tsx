import React, { useEffect, useState } from 'react';

import './index.scss';
import i18n from 'i18next';
import { Balloon, Button, Dialog, Input, Message, NumberPicker, Select, Table } from '@alifd/next';
import IceLabel from '@icedesign/label';
import { ComponentType, componentTypes } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import {
  AddCustomPlayableFileSelectorOptions,
  AddCustomPlayerOptions,
  GetAllCustomPlayableFileSelectorOptionsList,
  GetAllCustomPlayerOptionsList,
  OpenFileSelector,
  RemoveCustomPlayableFileSelectorOptionsByKey,
  RemoveCustomPlayerOptionsByKey,
  UpdateCustomPlayableFileSelectorOptions,
  UpdateCustomPlayerOptions,
} from '@/sdk/apis';
import Title from '@/components/Title';
import ConfirmationButton from '@/components/ConfirmationButton';

const customComponentTypes = [
  ComponentType.Player,
  ComponentType.PlayableFileSelector,
];

const customComponentDataSource = componentTypes.filter((a) => customComponentTypes.indexOf(a.value) > -1);

const customComponentOptionsProperties = {
  [ComponentType.Player]: [{
    label: i18n.t('Executable'),
    key: 'executable',
    render: (e) => e || i18n.t('Use OS defaults'),
  },
  {
    label: i18n.t('Command template'),
    key: 'commandTemplate',
  },
  {
    label: i18n.t('Sub players'),
    key: 'subCustomPlayerOptionsList',
    render: (list) => {
      const safeList = list || [];
      const extensions = safeList.reduce((s, t) => {
        return s.concat(t.extensions || []);
      }, []);
      return (
        <div style={{ display: 'flex', gap: '2px' }}>
          {
            extensions.map((e) => (
              <IceLabel style={{ margin: 0 }} inverse={false} status={'info'}>{e}</IceLabel>
            ))
          }
        </div>
      );
    },
  }],
  [ComponentType.PlayableFileSelector]: [
    {
      label: i18n.t('Extensions'),
      key: 'extensions',
      render: (extensions) => extensions?.length > 0 && (
        <div className={'extensions'}>
          {extensions.map((e) => <IceLabel key={e} inverse={false} status={'info'}>{e}</IceLabel>)}
        </div>
      ),
    },
    {
      label: i18n.t('MaxFileCount'),
      key: 'maxFileCount',
      width: '200px',
    },
  ],
};

export default () => {
  const [players, setPlayers] = useState([]);
  const [loadingPlayers, setLoadingPlayers] = useState(false);
  const [playableFileSelectors, setPlayableFileSelectors] = useState([]);
  const [loadingPlayableFileSelectors, setLoadingPlayableFileSelectors] = useState(false);
  const [player, setPlayer] = useState();
  const [playableFileSelector, setPlayableFileSelector] = useState();

  useEffect(() => {
    loadPlayers();
    loadPlayableFileSelectors();
  }, []);

  const loadPlayers = () => {
    setLoadingPlayers(true);
    GetAllCustomPlayerOptionsList().invoke((a) => {
      setLoadingPlayers(false);
      setPlayers(a.data);
    });
  };

  const loadPlayableFileSelectors = () => {
    setLoadingPlayableFileSelectors(true);
    GetAllCustomPlayableFileSelectorOptionsList().invoke((a) => {
      setLoadingPlayableFileSelectors(false);
      setPlayableFileSelectors(a.data);
    });
  };

  const showPlayerDetailDialog = (player = {}) => {
    // console.log(player);
    setPlayer({ ...player });
  };

  const closePlayerDetailDialog = () => {
    setPlayer(undefined);
  };

  const openDetail = (type, component = {}) => {
    switch (type) {
      case ComponentType.Player:
        showPlayerDetailDialog({ ...component });
        break;
      case ComponentType.PlayableFileSelector:
        showPlayableFileSelectorDetailDialog({ ...component });
        break;
    }
  };

  const removeComponent = (type, id) => {
    let invoker;
    let loadComponents;
    switch (type) {
      case ComponentType.Player:
        invoker = RemoveCustomPlayerOptionsByKey({ id });
        loadComponents = loadPlayers;
        break;
      case ComponentType.PlayableFileSelector:
        invoker = RemoveCustomPlayableFileSelectorOptionsByKey({ id });
        loadComponents = loadPlayableFileSelectors;
        break;
    }
    invoker?.invoke((a) => {
      if (!a.code) {
        loadComponents();
      }
    });
  };

  const showPlayableFileSelectorDetailDialog = (playableFileSelector = {}) => {
    setPlayableFileSelector({ ...playableFileSelector });
  };

  const closePlayableFileSelectorDetailDialog = () => {
    setPlayableFileSelector(undefined);
  };
  const renderPlayableFileSelector = () => {
    const extensions = playableFileSelector?.extensions || [];
    const extensionsDataSource = extensions.map((x) => ({ label: x, value: x }));

    return (
      <Dialog
        visible={playableFileSelector != undefined}
        closeable
        title={i18n.t('Playable File Selector Detail')}
        width={600}
        className={'custom-component-page-component-detail-dialog'}
        onClose={closePlayableFileSelectorDetailDialog}
        onCancel={closePlayableFileSelectorDetailDialog}
        onOk={() => {
          const invoker = playableFileSelector?.id > 0 ? UpdateCustomPlayableFileSelectorOptions({
            id: playableFileSelector.id,
            model: playableFileSelector,
          }) : AddCustomPlayableFileSelectorOptions({
            model: playableFileSelector,
          });
          invoker.invoke((a) => {
            if (!a.code) {
              loadPlayableFileSelectors();
              closePlayableFileSelectorDetailDialog();
            }
          });
        }}
      >
        <div className={'detail'}>
          <div className="item">
            <div className="label">{i18n.t('Name')}</div>
            <div className="value">
              <Input
                value={playableFileSelector?.name}
                onChange={(name) => {
                  setPlayableFileSelector({
                    ...playableFileSelector,
                    name,
                  });
                }}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{i18n.t('Extensions')}</div>
            <div className="value">
              <Select
                aria-label="tag mode"
                mode="tag"
                value={extensions}
                onChange={(v) => {
                  playableFileSelector.extensions = v.filter((t, i) => v.indexOf(t) == i).map((t) => (t.startsWith('.') ? t : `.${t}`));
                  setPlayableFileSelector({
                    ...playableFileSelector,
                  });
                }}
                dataSource={extensionsDataSource}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{i18n.t('Max File Count')}</div>
            <div className="value">
              <NumberPicker
                value={playableFileSelector?.maxFileCount}
                onChange={(maxFileCount) => {
                  setPlayableFileSelector({
                    ...playableFileSelector,
                    maxFileCount,
                  });
                }}
              />
            </div>
          </div>
        </div>
      </Dialog>
    );
  };

  const renderPlayerDetailDialog = () => {
    return (
      <Dialog
        visible={player != undefined}
        onOk={() => {
          const subPlayers = player.subCustomPlayerOptionsList || [];
          if (subPlayers.length > 0 && subPlayers.some((s) => !(s.extensions?.length > 0))) {
            Message.error(i18n.t('Extensions can not be empty'));
            return;
          }

          const model = player;
          const invoker = player.id > 0 ? UpdateCustomPlayerOptions({
            id: player.id,
            model,
          }) : AddCustomPlayerOptions({
            model,
          });
          invoker.invoke((a) => {
            if (!a.code) {
              loadPlayers();
              closePlayerDetailDialog();
            }
          });
        }}
        onCancel={closePlayerDetailDialog}
        onClose={closePlayerDetailDialog}
        closeable
        title={i18n.t('Player') + i18n.t('Detail')}
        className={'custom-component-page-component-detail-dialog player'}
      >
        <div className={'detail'}>
          <div className="item">
            <div className="label">{i18n.t('Name')}</div>
            <div className="value">
              <Input
                value={player?.name}
                onChange={(name) => {
                  setPlayer({ ...player, name });
                }}
              />
            </div>
          </div>
          <div className="item executable">
            <div className="label">
              {i18n.t('Default executable')}&nbsp;
              <Balloon.Tooltip
                triggerType={'click'}
                style={{ maxWidth: 'unset' }}
                trigger={<CustomIcon type={'question-circle'} />}
              >
                {i18n.t('The file path will be the first argument of your executable.')}{i18n.t('For example')} <br />
                "C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe" "D:\anime\dragon ball\1.mp4"
              </Balloon.Tooltip>
            </div>
            <div className="value">
              <Button
                size={'small'}
                text={player?.executable}
                type={'primary'}
                onClick={() => {
                  OpenFileSelector().invoke((t) => {
                    setPlayer({
                      ...player,
                      executable: t.data,
                    });
                  });
                }}
              >{player?.executable ?? i18n.t('Select a executable')}
              </Button>
            </div>
          </div>
          <div className="item">
            <div className="label">{i18n.t('Default command template')}&nbsp;
              <Balloon.Tooltip
                triggerType={'click'}
                style={{ maxWidth: 'unset' }}
                trigger={<CustomIcon type={'question-circle'} />}
              >
                {i18n.t('You can change the command template for some specific scenarios. The `{0}` will be replaced by filename and the default command template is `{0}`.')}{i18n.t('For example')} <br />
                {i18n.t('If the command template is `-i {0} --windowed`, the full command at runtime will be')} <br />
                "C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe" -i "D:\anime\dragon ball\1.mp4" --windowed'
              </Balloon.Tooltip>
            </div>
            <div className="value">
              <Input
                value={player?.commandTemplate}
                placeholder={i18n.t('Default is `{0}`. {0} will be replaced by filename')}
                onChange={(commandTemplate) => {
                  setPlayer({ ...player, commandTemplate });
                }}
              />
            </div>
          </div>
          <div className="item">
            <div className="label">{i18n.t('Sub player')}&nbsp;
              <Balloon.Tooltip
                triggerType={'click'}
                style={{ maxWidth: 'unset' }}
                trigger={<CustomIcon type={'question-circle'} />}
              >
                {i18n.t('You can set different players to open files with different types')}
              </Balloon.Tooltip>
            </div>
            <div className="value sub-players">
              <div className="first-line">
                <Button
                  type={'normal'}
                  size={'small'}
                  onClick={() => {
                    const list = player.subCustomPlayerOptionsList || [];
                    list.push({});
                    setPlayer({
                      ...player,
                      subCustomPlayerOptionsList: list,
                    });
                  }}
                >{i18n.t('Add')}
                </Button>
              </div>
              <Table dataSource={player?.subCustomPlayerOptionsList || []} size={'small'}>
                <Table.Column
                  title={i18n.t('Extensions')}
                  dataIndex={'extensions'}
                  cell={(c, i, r) => {
                    const value = c || [];
                    const ds = value.map((x) => ({ label: x, value: x }));
                    return (
                      <Select
                        aria-label="tag mode"
                        mode="tag"
                        value={value}
                        onChange={(v) => {
                          r.extensions = v.filter((t, i) => v.indexOf(t) == i).map((t) => (t.startsWith('.') ? t : `.${t}`));
                          setPlayer({
                            ...player,
                          });
                        }}
                        dataSource={ds}
                      />
                    );
                  }}
                />
                <Table.Column
                  title={i18n.t('Executable')}
                  dataIndex={'executable'}
                  cell={(c, i, r) => {
                    return (
                      <Button
                        size={'small'}
                        text={c}
                        type={'primary'}
                        onClick={() => {
                          OpenFileSelector().invoke((t) => {
                            r.executable = t.data;
                            setPlayer({
                              ...player,
                            });
                          });
                        }}
                      >{c ?? i18n.t('Select a executable')}
                      </Button>
                    );
                  }}
                />
                <Table.Column
                  title={i18n.t('Command template')}
                  dataIndex={'commandTemplate'}
                  cell={(c, i, r) => {
                    return (
                      <Input
                        value={c}
                        placeholder={i18n.t('Default is `{0}`. {0} will be replaced by filename')}
                        onChange={(commandTemplate) => {
                          r.commandTemplate = commandTemplate;
                          setPlayer({ ...player });
                        }}
                      />);
                  }}
                />
                <Table.Column
                  title={i18n.t('Operations')}
                  cell={(c, i, r) => {
                    return (
                      <CustomIcon
                        type={'delete'}
                        onClick={() => {
                          const list = (player.subCustomPlayerOptionsList || []).filter((t, j) => j != i);
                          setPlayer({
                            ...player,
                            subCustomPlayerOptionsList: list,
                          });
                        }}
                      />
                    );
                  }}
                />
              </Table>
            </div>
          </div>
        </div>
        <div className="tip">
          <span className={'important'}>*</span>
          {i18n.t('If all sub players mismatched the target file, then the default executable would be used')}
          <br />
          <span className={'important'}>*</span>
          {i18n.t('If executable is not set, files will be opened by your application set in your OS settings.')}
        </div>
      </Dialog>
    );
  };

  return (
    <div className={'custom-component-page'}>
      {renderPlayableFileSelector()}
      {renderPlayerDetailDialog()}
      {customComponentDataSource.map((cc) => {
        let components = [];
        let loading;
        let iconType;
        switch (cc.value) {
          case ComponentType.Player:
            components = players;
            loading = loadingPlayers;
            iconType = 'play-circle';
            break;
          case ComponentType.PlayableFileSelector:
            components = playableFileSelectors;
            loading = loadingPlayableFileSelectors;
            iconType = 'filesearch';
            break;
        }
        // console.log(components);
        return (
          <div className={'typed-components'} key={cc.value}>
            <Title title={cc.label} buttons={[{ text: 'Add', onClick: () => openDetail(cc.value, {}), icon: require('@/assets/add.png') }]} />
            <Table dataSource={components} loading={loading} size={'small'} >
              <Table.Column
                dataIndex={'id'}
                title={'Id'}
                width={'5%'}
                cell={(id) => id}
              />
              <Table.Column dataIndex={'name'} title={i18n.t('Name')} width={'20%'} />
              {customComponentOptionsProperties[cc.value].map((p) => (<Table.Column
                key={p.key}
                dataIndex={p.key}
                title={p.label}
                width={p.width}
                cell={(v) => {
                  // console.log(p, v);
                  return (p.render ? p.render(v) : v);
                }}
              />))}
              <Table.Column
                dataIndex={'id'}
                title={i18n.t('Operations')}
                width={'15%'}
                cell={(id, i, options) => {
                  return (
                    <div className={'confirmation-button-group'}>
                      <ConfirmationButton
                        size={'small'}
                        type={'secondary'}
                        onClick={() => openDetail(cc.value, options)}
                        icon={'edit-square'}
                        label={'Edit'}
                      />
                      <ConfirmationButton
                        icon={'delete'}
                        warning
                        onClick={(e) => {
                          removeComponent(cc.value, id);
                        }}
                        size={'small'}
                        confirmation
                        label={'Delete'}
                      />
                    </div>
                  );
                }}
              />
            </Table>
          </div>
        );
      })}
    </div>
  );
};
