import { useTranslation } from 'react-i18next';
import {
  AiOutlineDelete,
  AiOutlinePlusCircle,
  AiOutlineQuestionCircle,
  AiOutlineSetting,
  AiOutlineWarning,
} from 'react-icons/ai';
import _ from 'lodash';
import React, { useEffect, useState } from 'react';
import { MdOutlineAutoMode } from 'react-icons/md';
import {
  Alert,
  Button,
  Checkbox,
  Chip,
  Divider,
  Modal,
  Snippet,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
} from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import FeatureStatusTip from '@/components/FeatureStatusTip';
import BApi from '@/sdk/BApi';
import { DownloadTaskParserSource, downloadTaskParserSources, ThirdPartyId } from '@/sdk/constants';
import type { components } from '@/sdk/BApi2';
import store from '@/store';
import ConfigurationModal from '@/pages/PostParser/components/ConfigurationModal';
import ThirdPartyIcon from '@/components/ThirdPartyIcon';

type Task = components['schemas']['Bakabase.InsideWorld.Business.Components.DownloadTaskParser.Models.Domain.DownloadTaskParseTask']
  ;

const ThirdPartyMap: Record<DownloadTaskParserSource, ThirdPartyId> = {
  [DownloadTaskParserSource.SoulPlus]: ThirdPartyId.SoulPlus,
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');

  const [tasks, setTasks] = useState<Task[]>([]);
  const loadAllTasks = async () => {
    const r = (await BApi.downloadTaskParseTask.getAllDownloadTaskParseTasks()).data ?? [];
    setTasks(r);
  };

  useEffect(() => {
    loadAllTasks();
  }, []);

  return (
    <div>
      <div className={'flex justify-between items-center'}>
        <div className={'flex items-center gap-2'}>
          <Button
            size={'sm'}
            // variant={'flat'}
            color={'primary'}
            onPress={() => {
              let linksTextMap: Record<number, string> = {};
              createPortal(Modal, {
                defaultVisible: true,
                size: 'xl',
                title: t('Add tasks'),
                children: (
                  <div>
                    {downloadTaskParserSources.map(s => {
                      return (
                        <Textarea
                          onValueChange={v => {
                            linksTextMap[s.value] = v;
                          }}
                          label={t('Post links in {{source}}', { source: s.label })}
                          minRows={10}
                          placeholder={`https://xxxxxxx
https://xxxxxxx
...`}
                        />
                      );
                    })}
                    <FeatureStatusTip status={'developing'} name={t('Support for other sites')} />
                  </div>
                ),
                onOk: async () => {
                  const linksMap = _.mapValues(linksTextMap, value => value.split('\n').map(x => x.trim()).filter(x => x));
                  await BApi.downloadTaskParseTask.addDownloadTaskParseTasks(linksMap);
                },
              });
            }}
          >
            <AiOutlinePlusCircle className={'text-medium'} />
            {t('Add tasks')}
          </Button>
          <Button
            size={'sm'}
            onPress={() => {
              createPortal(ConfigurationModal, {});
            }}
          >
            <AiOutlineSetting className={'text-medium'} />
            {t('Configuration')}
          </Button>
        </div>
        <div className={'flex items-center gap-2'}>
          <Checkbox
            size={'sm'}
            // variant={'flat'}
            color={'secondary'}
            isSelected={thirdPartyOptions.automaticallyParsingPosts}
            onValueChange={v => BApi.options.patchThirdPartyOptions({ automaticallyParsingPosts: v })}
          >
            {t('Automatically parsing')}
          </Checkbox>
          <Button
            variant={'light'}
            size={'sm'}
            color={'success'}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                size: 'xl',
                title: t('Instructions for Use'),
                children: (
                  <div>
                    <Alert
                      description={(
                        <div>
                          <div>本功能内部使用curl，如果您的系统级curl低于8.14版本，请先在系统配置中配置正确的curl路径。</div>
                          <div>配置请求间隔暂时无法配置，默认内置间隔为3秒。</div>
                        </div>
                      )}
                      title={'curl'}
                    />
                    <Alert
                      description={(
                        <div>
                          <div>本功能内部使用ollama，您需要先安装并运行ollama，并安装至少1个模型，然后在系统配置中配置ollama的api地址。</div>
                          <div>本功能会优先使用ollama模型列表中最大的模型。</div>
                          <div>目前deepseek-r1:14b,32b已经过测试，可放心使用。</div>
                        </div>
                      )}
                      title={'ollama'}
                    />
                  </div>
                ),
                footer: {
                  actions: ['cancel'],
                },
              });
            }}
          >
            <AiOutlineQuestionCircle className={'text-medium'} />
            {t('Instructions for Use')}
          </Button>
        </div>
      </div>
      <div className={'mt-2'}>
        <Table
          removeWrapper
          isStriped
          className={'break-all'}
        >
          <TableHeader>
            <TableColumn>{t('#')}</TableColumn>
            <TableColumn>{t('Target')}</TableColumn>
            <TableColumn>{t('Content')}</TableColumn>
            <TableColumn>{t('Items')}</TableColumn>
            <TableColumn>{t('ParsedAt')}</TableColumn>
            <TableColumn>{t('Operations')}</TableColumn>
          </TableHeader>
          <TableBody>
            {tasks.map((task) => {
              return (
                <TableRow>
                  <TableCell>{task.id}</TableCell>
                  <TableCell>
                    {task.title ? (
                      <div className={'flex flex-col gap-1'}>
                        <div className={'flex items-center gap-1'}>
                          {/* <Chip */}
                          {/*   size={'sm'} */}
                          {/*   variant={'flat'} */}
                          {/* >{t(`DownloadTaskParserSource.${DownloadTaskParserSource[task.source]}`)}</Chip> */}
                          <ThirdPartyIcon thirdPartyId={ThirdPartyMap[task.source]} />
                          <div>{task.title}</div>
                        </div>
                        <div>
                          <Button
                            color={'primary'}
                            size={'sm'}
                            variant={'light'}
                            onPress={e => {
                              BApi.gui.openUrlInDefaultBrowser({ url: task.link });
                            }}
                          >{task.link}</Button>
                        </div>
                      </div>
                    ) : (
                      <div className={'flex items-center gap-1'}>
                        {/* <Chip */}
                        {/*   size={'sm'} */}
                        {/*   variant={'flat'} */}
                        {/* >{t(`DownloadTaskParserSource.${DownloadTaskParserSource[task.source]}`)}</Chip> */}
                        <ThirdPartyIcon thirdPartyId={ThirdPartyMap[task.source]} />
                        <div>
                          <Button
                            color={'primary'}
                            size={'sm'}
                            variant={'light'}
                            onPress={e => {
                              BApi.gui.openUrlInDefaultBrowser({ url: task.link });
                            }}
                          >{task.link}</Button>
                        </div>
                      </div>
                    )}
                  </TableCell>
                  <TableCell>
                    {task.content && (
                      <Button onPress={() => {
                        createPortal(Modal, {
                          size: 'xl',
                          defaultVisible: true,
                          children: (
                            <pre>{task.content}</pre>
                          ),
                        });
                      }}
                      >
                        {t('View')}
                      </Button>
                    )}
                  </TableCell>
                  <TableCell>{task.items?.map((item, idx) => {
                    return (
                      <React.Fragment key={idx}>
                        <div className={'flex flex-col gap-1'}>
                          {item.link && (
                            <Button
                              color={'primary'}
                              size={'sm'}
                              variant={'light'}
                              onPress={e => {
                                BApi.gui.openUrlInDefaultBrowser({ url: item.link });
                              }}
                            >{item.link}</Button>
                          )}
                          <div>
                            {item.accessCode && (
                              <Snippet symbol={t('Access code')} size={'sm'}>{item.accessCode}</Snippet>
                            )}
                            {item.decompressionPassword && (
                              <Snippet symbol={t('Decompression password')} size={'sm'}>{}</Snippet>
                            )}
                          </div>
                        </div>
                        {idx < task.items!.length - 1 && <Divider />}
                      </React.Fragment>
                    );
                  })}</TableCell>
                  <TableCell>
                    {task.parsedAt ? task.parsedAt : task.error ? (
                      <Button
                        onPress={() => {
                          createPortal(Modal, {
                            defaultVisible: true,
                            size: 'xl',
                            children: (
                              <pre>
                                {task.error}
                              </pre>
                            ),
                            footer: {
                              actions: ['cancel'],
                            },
                          });
                          }}
                        size={'sm'}

                        variant={'light'}
                        color={'danger'}
                        isIconOnly
                      >
                        <AiOutlineWarning className={'text-medium'} />
                      </Button>
                    ) : null}
                  </TableCell>
                  <TableCell>
                    <div className={'flex items-center gap-1'}>
                      <Button
                        isIconOnly
                        color={'danger'}
                        size={'sm'}
                        variant={'light'}
                        onPress={async () => {
                          await BApi.downloadTaskParseTask.deleteDownloadTaskParseTask(task.id);
                          setTasks(tasks.filter(x => x.id != task.id));
                        }}
                      >
                        <AiOutlineDelete className={'text-medium'} />
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  );
};
