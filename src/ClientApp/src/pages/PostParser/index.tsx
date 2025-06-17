import { useTranslation } from 'react-i18next';
import {
  AiOutlineDelete,
  AiOutlinePlusCircle,
  AiOutlineQuestionCircle,
  AiOutlineSetting,
  AiOutlineWarning,
} from 'react-icons/ai';
import _ from 'lodash';
import React, { useEffect } from 'react';
import {
  Alert,
  Button,
  Checkbox,
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
import { PostParserSource, postParserSources, ThirdPartyId } from '@/sdk/constants';
import store from '@/store';
import ConfigurationModal from '@/pages/PostParser/components/ConfigurationModal';
import ThirdPartyIcon from '@/components/ThirdPartyIcon';

const ThirdPartyMap: Record<PostParserSource, ThirdPartyId> = {
  [PostParserSource.SoulPlus]: ThirdPartyId.SoulPlus,
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');

  const tasks = store.useModelState('postParserTasks');

  useEffect(() => {
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
                    {postParserSources.map(s => {
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
                  await BApi.postParser.addPostParserTasks(linksMap);
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
            onValueChange={async v => {
              const r = await BApi.options.patchThirdPartyOptions({ automaticallyParsingPosts: v });
              if (v && !r.code) {
                await BApi.postParser.startAllPostParserTasks();
              }
            }}
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
                title: t('Instructions for use'),
                children: (
                  <div>
                    <Alert
                      description={(
                        <div>
                          <div>{t('This feature internally uses curl. If your system-level curl version is lower than 8.14, please configure the correct curl path in the system settings.')}</div>
                          <div>{t('The request interval configuration is temporarily unavailable. The built-in default interval is 3 seconds.')}</div>
                        </div>
                      )}
                      title={t('curl')}
                    />
                    <Alert
                      description={(
                        <div>
                          <div>{t('This feature internally uses Ollama. You need to install and run Ollama first, and install at least one model. Then configure the Ollama API address in the system settings.')}</div>
                          <div>{t('This feature will prioritize the largest model in the Ollama model list.')}</div>
                          <div>{t('Currently, deepseek-r1:14b and 32b have been tested and can be used safely.')}</div>
                        </div>
                      )}
                      title={t('ollama')}
                    />
                    <Alert
                      color={'success'}
                      variant={'flat'}
                      description={(
                        <div>
                          <div>{t('Some sites can integrate quick actions through the browser (such as one-click task creation). You can check the "Third-Party Integrations" section.')}</div>
                        </div>
                      )}
                      title={t('browser integrations')}
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
            {t('Instructions for use')}
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
            {/* <TableColumn>{t('Content')}</TableColumn> */}
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
                  {/* <TableCell> */}
                  {/*   {task.content && ( */}
                  {/*     <Button onPress={() => { */}
                  {/*       createPortal(Modal, { */}
                  {/*         size: 'xl', */}
                  {/*         defaultVisible: true, */}
                  {/*         children: ( */}
                  {/*           <pre>{task.content}</pre> */}
                  {/*         ), */}
                  {/*       }); */}
                  {/*     }} */}
                  {/*     > */}
                  {/*       {t('View')} */}
                  {/*     </Button> */}
                  {/*   )} */}
                  {/* </TableCell> */}
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
                          await BApi.postParser.deletePostParserTask(task.id);
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
