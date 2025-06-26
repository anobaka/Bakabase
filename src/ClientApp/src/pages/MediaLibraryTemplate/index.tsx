import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { ImportOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { AiOutlineEdit, AiOutlinePlusCircle, AiOutlineSearch } from 'react-icons/ai';
import { useUpdate } from 'react-use';
import _ from 'lodash';
import { IoDuplicateOutline } from 'react-icons/io5';
import { GoShareAndroid } from 'react-icons/go';
import { MdDeleteOutline } from 'react-icons/md';
import toast from 'react-hot-toast';
import { PiEmpty } from 'react-icons/pi';
import { BiCollapseVertical, BiExpandVertical } from 'react-icons/bi';
import type { MediaLibraryTemplate } from './models';
import Template from './components/Template';
import { Accordion, AccordionItem, Button, Chip, Input, Modal, Select, Textarea, Tooltip } from '@/components/bakaui';
import { PropertyPool } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { PropertyMap } from '@/components/types';
import BApi from '@/sdk/BApi';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import ImportModal from '@/pages/MediaLibraryTemplate/components/ImportModal';
import { willCauseCircleReference } from '@/components/utils';
import AddModal from '@/pages/MediaLibraryTemplate/components/AddModal';

type SearchForm = {
  keyword?: string;
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [templates, setTemplates] = useState<MediaLibraryTemplate[]>([]);
  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});
  const [searchForm, setSearchForm] = useState<SearchForm>({});
  const [expandedTemplateIds, setExpandedTemplateIds] = useState<string[]>([]);


  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(_.groupBy(psr, x => x.pool), (v) => _.keyBy(v, x => x.id));
    setPropertyMap(ps);
  };

  const loadTemplates = async () => {
    const r = await BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates();
    const templates = r.data || [];
    if (!r.code) {
      setTemplates(templates);
      return templates;
    }
    return [];
  };

  // load one template
  const loadTemplate = async (id: number) => {
    const r = await BApi.mediaLibraryTemplate.getMediaLibraryTemplate(id);
    if (!r.code) {
      setTemplates(templates.map(t => (t.id == r.data?.id ? r.data : t)));
    }
  };

  useEffect(() => {
    // createPortal(
    //   PathFilterModal, {
    //
    //   },
    // );
    BApi.enhancer.getAllEnhancerDescriptors().then(r => {
      setEnhancers(r.data || []);
    });
    loadProperties();
    loadTemplates().then(tpls => setExpandedTemplateIds(tpls.map(tpl => tpl.id.toString())));
  }, []);

  const putTemplate = async (tpl: MediaLibraryTemplate) => {
    const r = await BApi.mediaLibraryTemplate.putMediaLibraryTemplate(tpl.id, tpl);
    if (!r.code) {
      toast.success(t('Saved successfully'));
      await loadTemplate(tpl.id);
    }
  };

  const filteredTemplates = templates.filter(tpl => {
    if (!searchForm.keyword || searchForm.keyword.length == 0) {
      return true;
    }
    const keyword = searchForm.keyword.toLowerCase();
    return tpl.name.toLowerCase().includes(keyword) ||
      (tpl.author && tpl.author.toLowerCase().includes(keyword)) ||
      (tpl.description && tpl.description.toLowerCase().includes(keyword));
  });

  return (
    <div className={'h-full flex flex-col'}>
      <div className={'flex items-center gap-2 justify-between'}>
        <div className={'flex items-center gap-2'}>
          <Button
            size={'sm'}
            color={'primary'}
            onPress={() => createPortal(AddModal, { onDestroyed: loadTemplates })}
          >
            <AiOutlinePlusCircle className={'text-base'} />
            {t('Create a template')}
          </Button>
          <Input
            size={'sm'}
            fullWidth={false}
            startContent={<AiOutlineSearch className={'text-base'} />}
            placeholder={t('Search templates')}
            value={searchForm.keyword}
            onValueChange={keyword => setSearchForm({
              ...searchForm,
              keyword,
            })}
            endContent={filteredTemplates.length == templates.length ? filteredTemplates.length : `${filteredTemplates.length}/${templates.length}`}
          />
          {templates.length > 0 ? expandedTemplateIds.length < templates.length ? (
            <Button
              size={'sm'}
              color={'default'}
              onPress={() => setExpandedTemplateIds(templates.map(tpl => tpl.id.toString()))}
            >
              <BiExpandVertical className={'text-base'} />
              {t('Expand all')}
            </Button>
          ) : (
            <Button
              size={'sm'}
              color={'default'}
              onPress={() => setExpandedTemplateIds([])}
            >
              <BiCollapseVertical className={'text-base'} />
              {t('Collapse all')}
            </Button>
          ) : null}
        </div>
        <div>
          <Button
            size={'sm'}
            color={'default'}
            onPress={() => {
              createPortal(ImportModal, {
                onImported: async () => {
                  await loadProperties();
                  await loadTemplates();
                  // await loadExtensionGroups();
                },
              });
            }}
          >
            <ImportOutlined />
            {t('Import a template')}
          </Button>
        </div>
      </div>
      <div className={'mt-2 grow'}>
        {templates.length == 0 ? (
          <div className={'flex items-center gap-2 w-full h-full justify-center'}>
            <PiEmpty className={'text-2xl'} />
            {t('No templates found. You can create a template or import one from a share code.')}
          </div>
        ) : (
          <Accordion
            variant="splitted"
            selectedKeys={expandedTemplateIds}
            onSelectionChange={keys => {
              const ids = Array.from(keys).map(k => k as string);
              setExpandedTemplateIds(ids);
            }}
          >
            {filteredTemplates.map((tpl) => {
              return (
                <AccordionItem
                  id={tpl.id.toString()}
                  key={tpl.id}
                  title={(
                    <div className={'flex items-center justify-between'}>
                      <div className={'flex items-center gap-1'}>
                        <Chip className={''} variant={'flat'} size={'sm'}>
                          #{tpl.id}
                        </Chip>
                        <div className={'font-bold'}>
                          {tpl.name}
                        </div>
                        {tpl.author && (
                          <div className={'opacity-60'}>
                            {tpl.author}
                          </div>
                        )}
                        {tpl.description && (
                          <Tooltip placement={'bottom'} content={tpl.description}>
                            <QuestionCircleOutlined className={'text-medium'} />
                          </Tooltip>
                        )}
                        <Button
                          size={'sm'}
                          isIconOnly
                          onPress={() => {
                            let {
                              name,
                              author,
                              description,
                            } = tpl;
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t('Edit template'),
                              children: (
                                <div className={'flex flex-col gap-2'}>
                                  <Input
                                    label={t('Name')}
                                    placeholder={t('Enter template name')}
                                    isRequired
                                    defaultValue={name}
                                    onValueChange={v => {
                                      name = v;
                                    }}
                                  />
                                  <Input
                                    label={`${t('Author')}${t('(optional)')}`}
                                    placeholder={t('Enter author name')}
                                    defaultValue={author}
                                    onValueChange={v => {
                                      author = v;
                                    }}
                                  />
                                  <Textarea
                                    label={`${t('Description')}${t('(optional)')}`}
                                    placeholder={t('Enter description')}
                                    defaultValue={description}
                                    onValueChange={v => {
                                      description = v;
                                    }}
                                  />
                                </div>
                              ),
                              size: 'lg',
                              onOk: async () => {
                                tpl.name = name;
                                tpl.author = author;
                                tpl.description = description;
                                await putTemplate(tpl);
                                forceUpdate();
                              },
                            });
                          }}
                          variant={'light'}
                        >
                          <AiOutlineEdit className={'text-medium'} />
                        </Button>
                      </div>
                      <div className={'flex items-center gap-1'}>
                        <Chip variant={'light'} className={'opacity-60'}>
                          {tpl.createdAt}
                        </Chip>
                        <Button
                          size={'sm'}
                          isIconOnly
                          variant={'light'}
                          onPress={async () => {
                            const r = await BApi.mediaLibraryTemplate.getMediaLibraryTemplateShareCode(tpl.id);
                            if (!r.code && r.data) {
                              const shareText = r.data;
                              await navigator.clipboard.writeText(shareText);
                              createPortal(Modal, {
                                defaultVisible: true,
                                title: t('Share code has been copied'),
                                children: t('Share code has been copied to your clipboard, you can send it to your friends. (ctrl+v)'),
                                footer: {
                                  actions: ['ok'],
                                  okProps: {
                                    children: t('I got it'),
                                  },
                                },
                              });
                            }
                          }}
                        >
                          <GoShareAndroid className={'text-large'} />
                        </Button>
                        <Button
                          color={'default'}
                          variant={'light'}
                          size={'sm'}
                          isIconOnly
                          onPress={() => {
                            let newName = '';
                            createPortal(
                              Modal, {
                                defaultVisible: true,
                                title: t('Duplicate current template'),
                                // children: (
                                //
                                // ),
                                onOk: async () => {
                                  const r = await BApi.mediaLibraryTemplate.duplicateMediaLibraryTemplate(tpl.id);
                                  if (!r.code) {
                                    loadTemplates();
                                  }
                                },
                              },
                            );
                          }}
                        >
                          <IoDuplicateOutline className={'text-medium'} />
                        </Button>
                        <Button
                          size={'sm'}
                          color={'danger'}
                          isIconOnly
                          variant={'light'}
                          onPress={() => {
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t('Deleting template {{name}}', { name: tpl.name }),
                              children: t('This action cannot be undone. Are you sure you want to delete this template?'),
                              onOk: async () => {
                                const r = await BApi.mediaLibraryTemplate.deleteMediaLibraryTemplate(tpl.id);
                                if (!r.code) {
                                  loadTemplates();
                                }
                              },
                            });
                          }}
                        >
                          <MdDeleteOutline className={'text-large'} />
                        </Button>
                      </div>
                    </div>
                  )}
                >
                  {propertyMap && enhancers && templates && (
                    <Template
                      template={tpl}
                      enhancersCache={enhancers}
                      propertyMapCache={propertyMap}
                      templatesCache={templates}
                      onChange={async () => {
                        await loadTemplate(tpl.id);
                      }}
                    />
                  )}

                </AccordionItem>
              );
            })}
          </Accordion>
          // <div className={'select-text'}>{JSON.stringify(templates)}</div>
        )}
      </div>
    </div>
  );
};
