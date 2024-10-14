import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AppstoreOutlined, FileImageOutlined, HistoryOutlined, SearchOutlined } from '@ant-design/icons';
import { buildLogger, standardizePath } from '@/components/utils';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { MediaLibraryAdditionalItem } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Button, Divider, Input, Modal } from '@/components/bakaui';

type Props = {
  onSelect: (id: number, path: string) => (Promise<any> | any);
} & DestroyableProps;

type Library = {
  id: number;
  name: string;
  paths: string[];
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
  const fsOptions = store.useModelState('fileSystemOptions');

  // log(fsOptions);

  useEffect(() => {
    BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category })
      .then((a) => {
        const data: Record<number, Category> = {};
        for (const ml of a.data || []) {
          const paths: string[] | undefined = ml.pathConfigurations?.map(pc => pc.path).filter((x): x is string => x != null);
          if (paths && paths.length > 0) {
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
              paths: paths,
            });
          }
        }
        setCategories(Object.values(data));
      });
    return () => {
    };
  }, []);


  const recentlySelectedPaths = (fsOptions?.recentMovingDestinations || []).map((a) => standardizePath(a));

  const lowerCasedKeyword = keyword?.toLowerCase() ?? '';
  const filteredCategories = keyword != undefined && keyword?.length > 0 ? categories.map((a) => {
    if (a.name.toLowerCase()
      .indexOf(lowerCasedKeyword) > -1) {
      return a;
    } else {
      const filteredLibraries = (a.libraries || []).map((b) => {
        if (b.name.toLowerCase()
          .indexOf(lowerCasedKeyword) > -1) {
          return b;
        } else {
          const filteredPaths = b.paths.filter((c) => {
            return c.toLowerCase()
              .indexOf(lowerCasedKeyword) > -1;
          });
          return {
            ...b,
            paths: filteredPaths,
          };
        }
      })
        .filter((b) => b.paths.length > 0);

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
      title={t('Select a path')}
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
                  className={'grid gap-1 items-center'}
                  style={{ gridTemplateColumns: '20% auto' }}
                >
                  <div>
                    {c.name}
                  </div>
                  <div>
                    {c.libraries
                      .map((l, il) => {
                        return (
                          <>
                            <div
                              className={'grid gap-1 items-center'}
                              style={{ gridTemplateColumns: '20% auto' }}
                            >
                              <div>
                                {l.name}
                              </div>
                              <div
                                className={'flex flex-col gap-1'}
                              >
                                {l.paths.map((path) => {
                                  const selectedRecently = recentlySelectedPaths.indexOf(standardizePath(path)) > -1;
                                  return (
                                    <Button
                                      size={'sm'}
                                      variant={'light'}
                                      color={selectedRecently ? 'success' : 'primary'}
                                      onClick={() => {
                                        onSelect?.(l.id, path);
                                        setVisible(false);
                                      }}
                                    >
                                      {path}
                                      {selectedRecently && (
                                        <HistoryOutlined className={'text-sm'} />
                                      )}
                                    </Button>
                                  );
                                })}
                              </div>
                            </div>
                            {il != c.libraries.length - 1 && (
                              <Divider orientation={'horizontal'} />
                            )}
                          </>
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
