import React, { useEffect, useImperativeHandle, useState } from 'react';
import './index.scss';
import { Button, Dialog, Icon, Input } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { createPortalOfComponent, standardizePath } from '@/components/utils';
import BApi from '@/sdk/BApi';
import store from '@/store';

interface Props {
  onSelect: (path: string) => (Promise<any> | any);
  afterClose?: () => any;
}

const MediaLibraryPathSelector = React.forwardRef((props: Props, ref) => {
  const { t } = useTranslation();
  const {
    onSelect,
    afterClose = () => {
    },
  } = props;

  const [categories, setCategories] = useState<any[]>([]);
  const [visible, setVisible] = useState(true);
  const [keyword, setKeyword] = useState<string>();
  const fsOptions = store.useModelState('fileSystemOptions');

  const close = () => {
    setVisible(false);
  };

  useImperativeHandle(ref, () => ({
    close,
  }));

  useEffect(() => {
    BApi.mediaLibrary.getAllMediaLibraries()
      .then((a) => {
        const temp = {};
        const categoryNames = {};
        for (const ml of (a.data || [])) {
          if (!(ml.categoryId in temp)) {
            temp[ml.categoryId] = [];
            categoryNames[ml.categoryId] = ml.categoryName;
          }
          if (ml.pathConfigurations != undefined && ml.pathConfigurations?.length > 0) {
            temp[ml.categoryId].push(ml);
          }
        }
        const state = Object.keys(categoryNames)
          .map((cid) => {
            return {
              name: categoryNames[cid],
              libraries: temp[cid],
            };
          })
          .filter((b) => b.libraries?.length > 0);
        // console.log(state);
        setCategories(state);
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
          const filteredPathConfigurations = (b.pathConfigurations || []).filter((c) => {
            return c.path.toLowerCase()
              .indexOf(lowerCasedKeyword) > -1;
          });
          return {
            ...b,
            pathConfigurations: filteredPathConfigurations,
          };
        }
      })
        .filter((b) => b.pathConfigurations?.length > 0);

      return {
        ...a,
        libraries: filteredLibraries,
      };
    }
  })
    .filter((c) => c.libraries?.length > 0) : categories;

  return (
    <Dialog
      className={'mlps'}
      visible={visible}
      title={t('Select a path from media library')}
      footerActions={['cancel']}
      closeable
      onClose={close}
      onCancel={close}
      afterClose={afterClose}
    >
      <div className="filters">
        <Input
          className={'search'}
          innerBefore={(
            <Icon type={'search'} style={{ margin: '0 4px' }} />
          )}
          placeholder={t('Quick filter')}
          value={keyword}
          onChange={(v) => setKeyword(v)}
        />
      </div>
      <div className="categories">
        {filteredCategories.map((c) => {
          return (
            <div className={'category'}>
              <div className="name">{c.name}</div>
              <div className="libraries">
                {c.libraries
                  .map((l) => {
                    return (
                      <div className={'library'}>
                        <div className="name">
                          {l.name}
                        </div>
                        <div className="paths">
                          {(l.pathConfigurations || []).map((pc) => {
                            const selectedRecently = recentlySelectedPaths.indexOf(standardizePath(pc.path)) > -1;
                            return (
                              <div
                                className={'path'}
                                onClick={() => {
                                  const promise = onSelect(pc.path);
                                  if (promise) {
                                    promise.then(() => {
                                      close();
                                    });
                                  } else {
                                    close();
                                  }
                                }}
                              >
                                <Button
                                  type={'primary'}
                                  text
                                >
                                  {pc.path}
                                </Button>
                                {selectedRecently && (
                                  <span className={'selected-recently'}>{t('Selected recently')}</span>
                                )}
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
              </div>
            </div>
          );
        })}
      </div>
    </Dialog>
  );
});

const Wrapped = Object.assign({}, MediaLibraryPathSelector, {
  show: (props: Props) => createPortalOfComponent(MediaLibraryPathSelector, props),
});

export default Wrapped;
