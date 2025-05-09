import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { DeleteOutlined, ImportOutlined } from '@ant-design/icons';
import { BsFileEarmark, BsFolder, BsLayers } from 'react-icons/bs';
import type { PathFilter, PathLocator } from './models';
import { PathFilterFsType } from './models';
import { PathFilterDemonstrator } from './components/PathFilter';
import { Accordion, AccordionItem, Button, Input, Textarea } from '@/components/bakaui';
import type { PropertyPool } from '@/sdk/constants';
import { PathPositioner } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import Demonstrator from '@/pages/MediaLibraryTemplate/components/PathFilter/Demonstrator';


enum FileExtensionGroup {
  Image = 1,
  Audio,
  Video,
  Document,
  Application,
  Archive,
}

type MediaLibraryTemplate = {
  id: number;
  name: string;
  resourceFilters?: PathFilter[];
  properties?: MediaLibraryTemplateProperty[];
  playableFileLocator?: MediaLibraryTemplatePlayableFileLocator;
  enhancers?: MediaLibraryTemplateEnhancerOptions[];
  displayNameTemplate?: string;
  samplePaths?: string[];
};


type MediaLibraryTemplateProperty = {
  pool: PropertyPool;
  id: number;
  property?: IProperty;
  valueLocators?: PathLocator[];
};

type MediaLibraryTemplatePlayableFileLocator = {
  extensionGroups?: Set<FileExtensionGroup>;
  extensions?: Set<string>;
};

type MediaLibraryTemplateEnhancerOptions = {
  enhancerId: number;
  options?: EnhancerFullOptions;
};

type EnhancerFullOptions = any;

const testTemplates: MediaLibraryTemplate[] = [
  {
    id: 1,
    name: 'Movie -1 folder',
    resourceFilters: [
      {
        positioner: PathPositioner.Layer,
        layer: 1,
        // fsType: PathFilterFsType.Directory,
        extensions: new Set<string>(['.mp4', '.mp3']),
      },
      {
        positioner: PathPositioner.Regex,
        fsType: PathFilterFsType.Directory,
      },
      { positioner: PathPositioner.Regex },
      {
        positioner: PathPositioner.Regex,
        extensions: new Set<string>(['.mp4', '.mp3']),
      },
    ],
    playableFileLocator: {
      extensionGroups: new Set<FileExtensionGroup>([FileExtensionGroup.Video]),
    },
    properties: [],
    enhancers: [],
    displayNameTemplate: '',
    samplePaths: [],
  },
];


export default () => {
  const { t } = useTranslation();

  const [templates, setTemplates] = useState<MediaLibraryTemplate[]>(testTemplates);

  return (
    <div>
      <div className={'flex items-center gap-2 justify-between'}>
        <Button
          size={'sm'}
          color={'primary'}
        >{t('Create a template')}</Button>
        <Button
          size={'sm'}
          color={'default'}
        >
          <ImportOutlined />
          {t('Import a template')}
        </Button>
      </div>
      <div className={'mt-2'}>
        <Accordion
          variant="splitted"
          defaultSelectedKeys={templates.map(t => t.id.toString())}
        >
          {templates.map((tpl, i) => {
            return (
              <AccordionItem
                key={tpl.id}
                title={(
                  <div>{tpl.name}</div>
                )}
              >
                <div className={'flex flex-col gap-2'}>
                  <div>
                    <div>{t('Resource filter')}</div>
                    <div className={'grid grid-cols-5 gap-1'}>
                      {tpl.resourceFilters?.map(f => {
                        return (
                          <>
                            <PathFilterDemonstrator filter={f} />
                            <Button
                              color={'danger'}
                              variant={'light'}
                              size={'sm'}
                              isIconOnly
                            >
                              <DeleteOutlined />
                            </Button>
                          </>
                        );
                      })}
                    </div>
                    <div>
                      <Button
                        size={'sm'}
                        color={'primary'}
                      >
                        {t('Add a filter')}
                      </Button>
                    </div>
                  </div>
                  <div>
                    <div>{t('Playable files')}</div>
                    <div>
                      {tpl.playableFileLocator && (
                        <></>
                      )}
                    </div>
                    <div>
                      <Button
                        size={'sm'}
                        color={'primary'}
                      >{t('Add extension groups')}</Button>
                      <Button
                        size={'sm'}
                        color={'secondary'}
                      >{t('Add extensions')}</Button>
                    </div>
                  </div>
                  <div>
                    <div>{t('Properties')}</div>
                    <div>
                      fffff
                    </div>
                    <div>
                      <Button
                        size={'sm'}
                        color={'primary'}
                      >{t('Bind properties')}</Button>
                    </div>
                  </div>
                  <div>
                    <div>{t('Enhancers')}</div>
                    <div>
                      fffff
                    </div>
                    <div>
                      <Button
                        size={'sm'}
                        color={'primary'}
                      >{t('Bind enhancers')}</Button>
                    </div>
                  </div>
                  <div>
                    <div>{t('Display name template')}</div>
                    <div>
                      <Input size={'sm'} />
                    </div>
                  </div>
                  <div>
                    <div>{t('Sample paths')}</div>
                    <div>
                      <Textarea isMultiline />
                    </div>
                    <div>
                      <Button
                        size={'sm'}
                        color={'primary'}
                      >{t('Preview')}</Button>
                    </div>
                  </div>
                </div>
              </AccordionItem>
            );
          })}
        </Accordion>
      </div>
    </div>
  );
};
