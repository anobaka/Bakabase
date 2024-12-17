import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { HistoryOutlined, SearchOutlined } from '@ant-design/icons';
import { buildLogger, standardizePath } from '@/components/utils';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { MediaLibraryAdditionalItem } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Divider, Input, Modal } from '@/components/bakaui';

type Props = {
  onSelect: (id: number) => (Promise<any> | any);
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

const log = buildLogger('MediaLibraryPathSelectorV2');

export default (props: Props) => {
  const { t } = useTranslation();
  const { onSelect } = props;

  const [visible, setVisible] = useState(true);
  const [categories, setCategories] = useState<Category[]>([]);
  const [keyword, setKeyword] = useState<string>();
  const resourceOptions = store.useModelState('resourceOptions');

  // log(fsOptions);

  useEffect(() => {
    BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category })
      .then((a) => {
        const data: Record<number, Category> = {};
        for (const ml of a.data || []) {
          if (!(ml.categoryId in data)) {
            data[ml.categoryId] = {
              id: ml.categoryId,
              name: ml.category!.name,
              libraries: [],
            };
          }
          const { libraries } = data[ml.categoryId];
          libraries.push({
            id: ml.id,
            name: ml.name,
          });
        }
        setCategories(Object.values(data));
      });
    return () => {
    };
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
                              onSelect?.(l.id);
                              setVisible(false);
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
