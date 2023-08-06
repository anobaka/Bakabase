import React, { useCallback, useEffect, useReducer, useState } from 'react';
import { Button, Dialog, Input, Loading, Message, Pagination, Table, Upload } from '@alifd/next';
import { CreateAlias, ExportAliases, ImportAliasesURL, MergeAliasGroup, RemoveAlias, SearchAliases, UpdateAlias } from '@/sdk/apis';
import { AliasAdditionalItem } from '@/sdk/constants';
import './index.scss';
import i18n from 'i18next';
import CustomIcon from '@/components/CustomIcon';
import ButtonsBalloon from '@/components/ButtonsBalloon';
import ConfirmationButton from '@/components/ConfirmationButton';
import AliasSelector from '@/pages/Alias/components/AliasSelector';
import serverConfig from '@/serverConfig';

export default () => {
  const [form, setForm] = useState({
    pageSize: 20,
    pageIndex: 0,
    additionalItems: AliasAdditionalItem.Candidates,
  });
  const [aliases, setAliases] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    search();
  }, []);

  const search = () => {
    SearchAliases(form)
      .invoke((a) => {
        setForm({
          ...form,
          pageIndex: a.pageIndex,
          pageSize: a.pageSize,
        });
        setAliases(a.data);
        setTotalCount(a.totalCount);
      });
  };

  const changeForm = (update, searchAfterChanging = false) => {
    update(form);
    setForm({ ...form });
    if (searchAfterChanging) {
      search();
    }
  };

  const startEditingAlias = useCallback((alias) => {
    let { name } = alias;
    const title = alias.id > 0 ? `${i18n.t('Editing alias')}: ${name}` : i18n.t('Adding alias');
    Dialog.show({
      title,
      content: (
        <Input defaultValue={name} onChange={(v) => { name = v; }} />
      ),
      closeable: true,
      onOk: () => new Promise(((resolve, reject) => {
        if (name?.length > 0) {
          if (alias.id > 0) {
            UpdateAlias({
              id: alias.id,
              model: { name },
            }).invoke((b) => {
              if (!b.code) {
                alias.name = name;
                forceUpdate();
                resolve();
              } else {
                reject();
              }
            });
          } else {
            CreateAlias({
              model: {
                ...alias,
                name,
              },
            }).invoke((a) => {
              if (!a.code) {
                search();
                resolve();
              } else {
                reject();
              }
            });
          }
        }
      })),
    });
  }, []);

  return (
    <div className="alias-page">
      <Loading visible={loading} fullScreen />
      <div className="form">
        <div className="opt">
          <div className="left">
            <Input.Group
              addonAfter={(
                <Button
                  type={'normal'}
                  onClick={() => {
                    changeForm((f) => { f.pageIndex = 0; }, true);
                  }}
                >
                  {i18n.t('Search')}
                </Button>
              )}
            >
              <Input
                onKeyDown={(e) => {
                  if (e.key == 'Enter') {
                    changeForm((f) => { f.pageIndex = 0; }, true);
                  }
                }}
                addonTextBefore={i18n.t('Keyword')}
                size={'medium'}
                value={form.name}
                onChange={(v) => changeForm((f) => {
                  f.name = v;
                })}
              />
            </Input.Group>
          </div>
          <div className="right">
            <Upload
              action={`${serverConfig.apiEndpoint}${ImportAliasesURL()}`}
              onSuccess={(a) => {
                setLoading(false);
                Message.success(i18n.t('Success'));
                search();
                console.log(a);
              }}
              beforeUpload={() => {
                setLoading(true);
                Message.loading(i18n.t('Importing'));
              }}
              onError={({ response }) => {
                setLoading(false);
                Message.error(i18n.t(response.message));
              }}
              multiple
              formatter={(res, file) => {
                // 函数里面根据当前服务器返回的响应数据
                // 重新拼装符合组件要求的数据格式
                return {
                  success: res.code == 0,
                  message: res.message,
                };
              }}
            >
              <Button
                type="normal"
                size={'small'}
                // onClick={() => {
                //   OpenFilesSelector().invoke((a) => {
                //     if (a.data) {
                //       ImportAliases({
                //
                //       });
                //     }
                //   });
                // }}
              >
                <CustomIcon type={'upload'} size={'small'} />
                {i18n.t('Import')}
              </Button>
            </Upload>
            <Button
              type="normal"
              size={'small'}
              onClick={() => {
                ExportAliases()
                  .invoke();
              }}
            >
              <CustomIcon type={'download'} size={'small'} />
              {i18n.t('Export')}
            </Button>
          </div>
        </div>
      </div>
      <div className="aliases">
        {totalCount > 1 &&
          (
            <Pagination
              size={'small'}
              pageShowCount={8}
              current={form.pageIndex}
              pageSize={form.pageSize}
              total={totalCount}
              onChange={(p) => changeForm((f) => f.pageIndex = p, true)}
            />
          )}
        <Table
          dataSource={aliases}
          isZebra
          size={'small'}
        >
          <Table.Column
            title={i18n.t('Preferred')}
            dataIndex="name"
            cell={(n, i, a) => {
              const name = n;
              return (
                <ButtonsBalloon
                  operations={[
                    {
                      type: 'secondary',
                      label: 'Edit',
                      icon: 'edit-square',
                      onClick: () => startEditingAlias(a),
                    },
                  ]}
                  trigger={n}
                />
              );
            }}
          />
          <Table.Column
            title={i18n.t('Candidates')}
            dataIndex="candidates"
            cell={(candidates, i, alias) => candidates && (
              <div className={'candidates'} key={i}>
                {
                  candidates.map((c, j) => (
                    <ButtonsBalloon
                      key={j}
                      operations={[
                        {
                          type: 'secondary',
                          label: 'Edit',
                          icon: 'edit-square',
                          onClick: () => startEditingAlias(c),
                        },
                        {
                          type: 'secondary',
                          label: 'Set preferred',
                          icon: 'totop',
                          onClick: () => {
                            UpdateAlias({
                              id: c.id,
                              model: {
                                isPreferred: true,
                              },
                            }).invoke((a) => {
                              if (!a.code) {
                                search();
                              }
                            });
                          },
                        },
                        {
                          warning: true,
                          onClick: (e) => {
                            RemoveAlias({
                              id: c.id,
                            }).invoke((t) => {
                              if (!t.code) {
                                alias.candidates = candidates.filter((tc) => tc != c);
                                setAliases([...aliases]);
                              }
                            });
                          },
                          label: 'Delete',
                          icon: 'delete',
                          confirmation: true,
                        },
                      ]}
                      trigger={(
                        <div className={'candidate'}>
                          {c.name}
                        </div>
                      )}
                    />
                  ))
                }
              </div>
            )}
          />
          <Table.Column
            title={i18n.t('Operations')}
            dataIndex="id"
            width={'20%'}
            cell={(id, _, a) => (
              <div className={'opts'}>
                <Button
                  size={'small'}
                  type={'secondary'}
                  onClick={() => {
                    startEditingAlias({
                      groupId: a.groupId,
                    });
                  }}
                >
                  <CustomIcon type={'plus-circle'} size={'small'} />
                  {i18n.t('Add')}
                </Button>
                <Button
                  size={'small'}
                  onClick={() => {
                    let targetGroupId;
                    Dialog.show({
                      title: i18n.t('Merging current alias group to'),
                      content: (
                        <AliasSelector onChange={(v) => { targetGroupId = v; }} excludeGroupIds={[a.groupId]} />
                      ),
                      closeable: true,
                      onOk: () => new Promise(((resolve, reject) => {
                        if (targetGroupId > 0) {
                          MergeAliasGroup({
                            model: {
                              targetGroupId,
                            },
                            id: a.groupId,
                          }).invoke((a) => {
                            if (!a.code) {
                              resolve();
                              search();
                            } else {
                              reject();
                            }
                          });
                        }
                      })),
                    });
                  }}
                >
                  <CustomIcon type={'git-merge-line'} size={'small'} />
                  {i18n.t('Merge to')}
                </Button>
                <ConfirmationButton
                  size={'small'}
                  warning
                  type={'normal'}
                  onClick={() => {
                    RemoveAlias({
                      id,
                    }).invoke((a) => {
                      if (!a.code) {
                        search();
                      }
                    });
                  }}
                  confirmation
                  icon={'delete'}
                  label={'Delete'}
                />
              </div>
            )}
          />
        </Table>
        {totalCount > 1 &&
          (
            <Pagination
              size={'small'}
              pageShowCount={8}
              current={form.pageIndex}
              pageSize={form.pageSize}
              total={totalCount}
              onChange={(p) => changeForm((f) => f.pageIndex = p, true)}
            />
          )}
      </div>
    </div>
  );
};
