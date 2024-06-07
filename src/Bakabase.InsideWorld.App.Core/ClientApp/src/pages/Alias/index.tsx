import React, { useEffect, useReducer, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AliasAdditionalItem } from '@/sdk/constants';
import './index.scss';
import BApi from '@/sdk/BApi';
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Divider,
  Input,
  Modal,
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow, Tooltip,
} from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { DeleteOutlined, EnterOutlined, MergeOutlined, SearchOutlined, ToTopOutlined } from '@ant-design/icons';

type Form = {
  pageSize: 20;
  pageIndex: number;
  additionalItems: AliasAdditionalItem.Candidates;
  text?: string;
  fuzzyText?: string;
};

type Alias = {
  originalText: string;
  text: string;
  preferred?: string;
  candidates?: string[];
};

type BulkOperationContext = {
  preferredTexts: string[];
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [form, setForm] = useState<Form>({
    pageSize: 20,
    pageIndex: 0,
    additionalItems: AliasAdditionalItem.Candidates,
  });
  const [aliases, setAliases] = useState<Alias[]>([]);
  const [bulkOperationContext, setBulkOperationContext] = useState<BulkOperationContext>();
  const [totalCount, setTotalCount] = useState(0);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    search();
  }, []);

  const search = (pForm?: Partial<Form>) => {
    const nf = {
      ...form,
      ...pForm,
    };
    setForm(nf);
    BApi.alias.searchAliasGroups(nf)
      .then((a) => {
        setAliases(a.data?.map(x => ({
          originalText: x.text!,
          text: x.text!,
          preferred: x.preferred ?? undefined,
          candidates: x.candidates ?? undefined,
        })) ?? []);
        setTotalCount(a.totalCount!);
      });
  };

  const renderPagination = () => {
    const pageCount = Math.ceil(totalCount / form.pageSize);
    if (pageCount > 1) {
      return (
        <div className={'flex justify-center'}>
          <Pagination
            size={'sm'}
            page={form.pageIndex}
            total={pageCount}
            onChange={(p) => search({ pageIndex: p })}
          />
        </div>
      );
    }
    return;
  };

  return (
    <div className="">
      <div className={'flex items-center justify-between'}>
        <div>
          <Input
            startContent={<SearchOutlined className={'text-sm'} />}
            placeholder={t('Press enter to search')}
            value={form.fuzzyText}
            onValueChange={v => setForm({
              ...form,
              fuzzyText: v,
            })}
            onKeyDown={e => {
              if (e.key == 'Enter') {
                search();
              }
            }}
          />
        </div>
        <div>
          <Button
            size={'sm'}
          >
            {t('Add an alias')}
          </Button>
        </div>
      </div>
      <Divider className={'my-1'} />
      {bulkOperationContext && (
        <>
          <Card>
            <CardHeader>
              <div className={'text-md flex items-center gap-2'}>
                {t('Bulk operations')}
                <Tooltip
                  content={t('{{text}} will be the preferred text in merged groups, you can change the preferred text by clicking the text.', { text: bulkOperationContext.preferredTexts[0] })}
                >
                  <Button
                    color={'secondary'}
                    size={'sm'}
                    startContent={<MergeOutlined className={'text-sm'} />}
                    onClick={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t('Merging alias groups: {{texts}}', { texts: bulkOperationContext.preferredTexts.join(',') }),
                        children: t('All selected alias groups will be merged into one, and the final preferred is {{preferred}}, are you sure?', { preferred: bulkOperationContext.preferredTexts[0] }),
                        onOk: async () => {
                          await BApi.alias.mergeAliasGroups({ preferredTexts: bulkOperationContext.preferredTexts });
                          setBulkOperationContext(undefined);
                          search();
                        },
                      });
                    }}
                  >
                    {t('Merge')}
                  </Button>
                </Tooltip>
                <Button
                  color={'danger'}
                  size={'sm'}
                  onClick={() => {
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t('Deleting alias groups: {{texts}}', { texts: bulkOperationContext.preferredTexts.join(',') }),
                      children: t('All selected alias groups and its candidates will be delete and there is no way back, are you sure?'),
                      onOk: async () => {
                        await BApi.alias.deleteAliasGroups({ preferredTexts: bulkOperationContext.preferredTexts });
                        setBulkOperationContext(undefined);
                        search();
                      },
                    });
                  }}
                  startContent={<DeleteOutlined className={'text-sm'} />}
                >
                  {t('Delete')}
                </Button>
                <Button
                  color={'default'}
                  size={'sm'}
                  startContent={<EnterOutlined className={'text-sm'} />}
                  onClick={() => {
                    setBulkOperationContext(undefined);
                  }}
                >
                  {t('Exit')}
                </Button>
              </div>
            </CardHeader>
            <Divider />
            <CardBody>
              <div className={'flex flex-wrap gap-1'}>
                {bulkOperationContext.preferredTexts.map((t, i) => {
                  return (
                    <Chip
                      size={'sm'}
                      radius={'sm'}
                      color={i == 0 ? 'primary' : 'default'}
                      onClick={() => {
                        bulkOperationContext.preferredTexts.splice(i, 1);
                        setBulkOperationContext({
                          ...bulkOperationContext,
                          preferredTexts: [t, ...bulkOperationContext.preferredTexts],
                        });
                      }}
                      onClose={() => {
                        const texts = bulkOperationContext.preferredTexts.filter(x => x != t);
                        if (texts.length == 0) {
                          setBulkOperationContext(undefined);
                        } else {
                          setBulkOperationContext({
                            ...bulkOperationContext,
                            preferredTexts: texts,
                          });
                        }
                      }}
                    >
                      {t}
                    </Chip>
                  );
                })}
              </div>
            </CardBody>
          </Card>
        </>
      )}
      {aliases.length > 0 && (
        <div className={'mt-1'}>
          <Table
            topContent={renderPagination()}
            bottomContent={renderPagination()}
            isStriped
            isCompact
            selectionMode={'multiple'}
            selectedKeys={bulkOperationContext?.preferredTexts}
            onSelectionChange={keys => {
              let selection: string[];
              if (keys === 'all') {
                selection = aliases.map(a => a.text);
              } else {
                selection = Array.from(keys).map(String);
              }

              const notSelected = aliases.map(x => x.text).filter(x => !selection.includes(x));
              const ns = Array.from(new Set(
                (bulkOperationContext?.preferredTexts ?? []).filter(x => !notSelected.includes(x)).concat(selection),
              ));

              setBulkOperationContext({
                ...bulkOperationContext,
                preferredTexts: ns,
              });
            }}
            color={'primary'}
          >
            <TableHeader>
              <TableColumn>{t('Preferred')}</TableColumn>
              <TableColumn>{t('Candidates')}</TableColumn></TableHeader>
            <TableBody>
              {aliases.map(a => {
                return (
                  <TableRow key={a.text}>
                    <TableCell>{a.originalText}</TableCell>
                    <TableCell>
                      <div
                        className={'flex flex-wrap gap-1'}
                        onClick={e => {
                        e.cancelable = true;
                        e.stopPropagation();
                        e.preventDefault();
                      }}
                      >
                        {a.candidates?.map(c => {
                          return (
                            <Chip
                              radius={'sm'}
                              onClick={e => {
                                e.cancelable = true;
                                e.preventDefault();
                                e.stopPropagation();
                                BApi.alias.patchAlias({
                                  isPreferred: true,
                                }, { text: a.originalText }).then(() => {
                                  search();
                                });
                              }}
                              onClose={() => {
                                createPortal(Modal, {
                                  defaultVisible: true,
                                  title: t('Deleting an alias: {{text}}', { text: c }),
                                  content: t('There is no way back, are you sure?'),
                                  onOk: async () => {
                                    await BApi.alias.deleteAlias({ text: c });
                                    a.candidates = a.candidates?.filter(x => x != c);
                                    forceUpdate();
                                  },
                                });
                              }}
                            >
                              {c}
                            </Chip>
                          );
                        })}
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
};
