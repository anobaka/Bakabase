import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DoubleRightOutlined, HistoryOutlined, SearchOutlined } from '@ant-design/icons';
import { buildLogger, standardizePath } from '@/components/utils';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { MediaLibraryAdditionalItem } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Chip, Divider, Input, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Props = {
  onSelect: (id: number, isLegacyMediaLibrary: boolean) => (Promise<any> | any);
  confirmation?: boolean;
} & DestroyableProps;

type Library = {
  id: number;
  name: string;
};

type Category = {
  id: number;
  name: string;
  libraries: Library[];
};

const log = buildLogger('MediaLibrarySelectorV2');

export default (props: Props) => {
  const { t } = useTranslation();
  const { onSelect, confirmation } = props;
  const { createPortal } = useBakabaseContext();

  const [visible, setVisible] = useState(true);
  const [categories, setCategories] = useState<Category[]>([]);
  const [keyword, setKeyword] = useState<string>();
  const resourceOptions = store.useModelState('resourceOptions');

  const init = async () => {
    const mls = (await BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category })).data ?? [];
    const data: Record<number, Category> = {};
    for (const ml of mls) {
      const paths: string[] | undefined = ml.pathConfigurations?.map(pc => pc.path).filter((x): x is string => x != null);
      if (paths && paths.length > 0) {
        if (!(ml.categoryId in data)) {
          data[ml.categoryId] = {
            id: ml.categoryId,
            name: ml.category!.name,
            libraries: [],
          };
        }
        const { libraries } = data[ml.categoryId]!;
        libraries.push({
          id: ml.id,
          name: ml.name,
          paths: paths,
        });
      }
    }
    const mlv2s = (await BApi.mediaLibraryV2.getAllMediaLibraryV2()).data ?? [];
    if (mlv2s.length > 0) {
      data[0] = { id: 0, name: t('/'), libraries: mlv2s.map(ml => ({ id: ml.id, name: ml.name, paths: [ml.path] })) };
    }
    setCategories(Object.values(data));
  };

  useEffect(() => {
    init();
  }, []);


  const recentlySelectedIds = resourceOptions?.idsOfMediaLibraryRecentlyMovedTo || [];

  const lowerCasedKeyword = keyword?.toLowerCase() ?? '';
  const filteredCategories = keyword != undefined && keyword?.length > 0 ? categories.map((a) => {
    if (a.name.toLowerCase()
      .indexOf(lowerCasedKeyword) > -1) {
      return a;
    } else {
      const filteredLibraries = (a.libraries || []).filter((b) => {
        return b.name.toLowerCase().includes(lowerCasedKeyword);
      });
      return {
        ...a,
        libraries: filteredLibraries,
      };
    }
  })
    .filter((c) => c.libraries.length > 0) : categories;

  return (
    <Modal
      visible={visible}
      footer={false}
      size={'lg'}
      title={t('Select a media library')}
      onDestroyed={props.onDestroyed}
      onClose={() => {
        setVisible(false);
      }}
    >
      <div>
        <div>
          <Input
            startContent={(
              <SearchOutlined className={'text-base'} />
            )}
            placeholder={t('Quick filter')}
            size={'sm'}
            value={keyword}
            onValueChange={(v) => setKeyword(v)}
          />
        </div>
        <div className={'py-2'}>
          {filteredCategories.map((c, ic) => {
            return (
              <>
                <div
                  className={'grid gap-12 items-center'}
                  style={{ gridTemplateColumns: '30% auto' }}
                >
                  <div>
                    {c.name}
                    {c.id != 0 && (
                      <Chip
                        size={'sm'}
                        variant={'flat'}
                        radius={'sm'}
                      >
                        {t('Deprecated')}
                      </Chip>
                    )}
                  </div>
                  <div>
                    {c.libraries
                      .map((l, il) => {
                        const selectedRecently = recentlySelectedIds.includes(l.id);
                        return (
                          <Button
                            size={'sm'}
                            variant={'light'}
                            color={selectedRecently ? 'success' : 'primary'}
                            onClick={() => {
                              if (confirmation) {
                                createPortal(Modal, {
                                  defaultVisible: true,
                                  title: t('Are you sure to select this media library?'),
                                  children: (
                                    <div className={'flex items-center gap-2'}>
                                      <Chip
                                        radius={'sm'}
                                        color={'primary'}
                                      >
                                        {c.name}
                                      </Chip>
                                      <DoubleRightOutlined className={'text-base'} />
                                      <Chip radius={'sm'}>
                                        {l.name}
                                      </Chip>
                                    </div>
                                  ),
                                  onOk: async () => {
                                    onSelect?.(l.id, c.id != 0);
                                    setVisible(false);
                                  },
                                });
                              } else {
                                onSelect?.(l.id, c.id != 0);
                                setVisible(false);
                              }
                            }}
                          >
                            {l.name}
                            {selectedRecently && (
                              <HistoryOutlined className={'text-sm'} />
                            )}
                          </Button>
                        );
                      })}
                  </div>
                </div>
                {ic != filteredCategories.length - 1 && (
                  <Divider orientation={'horizontal'} />
                )}
              </>
            );
          })}
        </div>
      </div>
    </Modal>
  );
};
