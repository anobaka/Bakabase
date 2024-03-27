import { Dialog, Dropdown, Menu, Overlay } from '@alifd/next';
import { Trans, useTranslation } from 'react-i18next';
import React from 'react';
import { Link } from '@ice/runtime/router';
import styles from './index.module.scss';
import ClickableIcon from '@/components/ClickableIcon';
import BApi from '@/sdk/BApi';
import ResourceEnhancementsDialog from '@/components/Resource/components/ResourceEnhancementsDialog';
import ShowResourceMediaPlayer from '@/components/Resource/components/ShowResourceMediaPlayer';
import type { CoverSaveLocation } from '@/sdk/constants';
import { PlaylistItemType } from '@/sdk/constants';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';
import { PlaylistCollection } from '@/components/Playlist';
import TagSelector from '@/components/TagSelector';
import store from '@/store';
import type { IResourceCoverRef } from '@/components/Resource/components/ResourceCover';

const { Popup } = Overlay;

interface IProps {
  resource: any;
  coverRef?: IResourceCoverRef;
  reload?: (ct?: AbortSignal) => Promise<any>;
}

export default ({
                  resource,
                  coverRef,
                  reload,
                }: IProps) => {
  const { t } = useTranslation();

  const searchEngines = store.useModelState('thirdPartyOptions').simpleSearchEngines || [];

  return (
    <Popup
      trigger={(
        <ClickableIcon
          className={styles.portal}
          type={'ellipsis'}
          size={'small'}
          colorType={'normal'}
          onMouseEnter={e => {
            e.preventDefault();
            e.stopPropagation();
          }}
        />
      )}
      // triggerType={'click'}
    >
      <div className={styles.operations}>
        <div className={styles.opt} title={t('Preview')}>
          <ClickableIcon
            colorType={'normal'}
            type={'eye'}
            onClick={() => {
              ShowResourceMediaPlayer(resource.id, resource.rawFullname, (base64String: string, saveTarget?: CoverSaveLocation) => {
                coverRef?.save(base64String, saveTarget);
                // @ts-ignore
              }, t, resource.isSingleFile, resourceOptions.coverOptions?.target);
            }}
          />
        </div>
        <div className={styles.opt} title={t('Open folder')}>
          <ClickableIcon
            colorType={'normal'}
            type={'folder-open'}
            onClick={() => open()}
          />
        </div>
        <div
          className={styles.opt}
          title={t('Move')}
          onClick={() => {
            MediaLibraryPathSelector.show({
              onSelect: path => BApi.resource.moveResources({
                ids: [resource.id],
                path,
              }),
            });
          }}
        >
          <ClickableIcon
            colorType={'normal'}
            type={'move'}
            size={'small'}
          />
        </div>
        <div
          className={styles.opt}
          title={t('Add to favorites')}
          onClick={() => {
            FavoritesSelector.show({
              resourceIds: [resource.id],
            });
          }}
        >
          <ClickableIcon
            colorType={'normal'}
            type={'star'}
            size={'small'}
          />
        </div>
        <div
          className={styles.opt}
          title={t('Add to playlist')}
          onClick={() => {
            Dialog.show({
              title: t('Add to playlist'),
              content: (
                <PlaylistCollection defaultNewItem={{
                  resourceId: resource.id,
                  type: PlaylistItemType.Resource,
                }}
                />
              ),
              style: { minWidth: 600 },
              v2: true,
              closeMode: ['close', 'mask', 'esc'],
            });
          }}
        >
          <ClickableIcon
            colorType={'normal'}
            type={'playlistadd'}
            size={'small'}
          />
        </div>
        <div
          className={styles.opt}
          title={t('Set tags')}
          onClick={() => {
            let tagIds = (resource.tags || []).map(t => t.id);
            Dialog.show({
              title: t('Setting tags'),
              width: 'auto',
              content: (
                <TagSelector defaultValue={{ tagIds }} onChange={value => tagIds = value.tagIds} />
              ),
              v2: true,
              closeMode: ['close', 'mask', 'esc'],
              onOk: () => BApi.resource.updateResourceTags({
                resourceTagIds: {
                  [resource.id]: tagIds,
                },
              })
                .then(t => {
                  if (!t.code) {
                    reload?.();
                  }
                }),
            });
          }}
        >
          <ClickableIcon
            colorType={'normal'}
            type={'tags'}
            size={'small'}
          />
        </div>
        <div className={styles.opt} title={t('Search')}>
          <Dropdown
            autoFocus={false}
            trigger={
              <ClickableIcon
                colorType={'normal'}
                type={'search'}
              />
            }
            triggerType={'click'}
          >
            {(searchEngines && searchEngines.length > 0) ? (
              <Menu className={'resource-component-search-dropdown-menu'}>
                {searchEngines?.filter(e => e.urlTemplate)
                  .map((e, i) => {
                    return (
                      <Menu.Item
                        key={i}
                        onClick={() => {
                          BApi.gui.openUrlInDefaultBrowser({
                            url: e.urlTemplate!.replace('{keyword}', encodeURIComponent(resource.rawName)),
                          });
                        }}
                      >
                        <Trans i18nKey={'resource.search-engine.tip'}>
                          Use <span>{{ name: e.name } as any}</span> to
                          search <span>{{ keyword: resource.rawName } as any}</span>
                        </Trans>
                      </Menu.Item>
                    );
                  })}
              </Menu>
            ) : (
              <Link to={'/configuration'}>
                {t('Set external search engines')}
              </Link>
            )}
          </Dropdown>
        </div>
        <div className={styles.opt} title={t('Enhancements')}>
          <ClickableIcon
            colorType={'normal'}
            type={'flashlight'}
            onClick={() => {
              BApi.resource.getResourceEnhancementRecords(resource.id)
                .then((t) => {
                  ResourceEnhancementsDialog.show({
                    resourceId: resource.id,
                    enhancements: t.data || [],
                  });
                });
            }}
          />
        </div>
        <div className={styles.opt} title={t('Remove')}>
          <ClickableIcon
            type={'delete'}
            colorType={'danger'}
          />
        </div>
      </div>
    </Popup>
  );
};
