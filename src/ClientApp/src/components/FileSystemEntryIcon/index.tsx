import React, { useEffect, useState } from 'react';
import i18n from 'i18next';
import store from '@/store';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';
import { IconType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { splitPathIntoSegments } from '@/components/utils';
import _ from 'lodash';

type Props = {
  type: IconType;
  path?: string;
  size: number | string;
  disableCache?: boolean;
};

const buildCacheKey = (type: IconType, path?: string): string => {
  let suffix = '';
  switch (type) {
    case IconType.UnknownFile:
    case IconType.Directory:
      break;
    case IconType.Dynamic: {
      if (!path) {
        throw new Error('Path is required for dynamic icon');
      }

      if (path.endsWith('.exe') || path.endsWith('.app')) {
        suffix = path;
        break;
      }

      const filename = _.last(splitPathIntoSegments(path))!;
      const filenameSegments = filename.split('.');
      const ext = filenameSegments[filenameSegments.length - 1];
      if (ext == undefined) {
        type = IconType.UnknownFile;
      } else {
        suffix = `.${ext}`;
      }
      break;
    }
  }
  return `${type}-${suffix}`;
};

export default ({
                  path,
                  type,
                  size = 14,
                  disableCache,
                }: Props) => {
  const cacheKey = buildCacheKey(type, path);

  const [icons, iconsDispatchers] = store.useModel('icons');
  const [iconImgData, setIconImgData] = useState(disableCache ? undefined : icons[cacheKey]);

  // console.log(path, type, size, iconImgData);

  useEffect(() => {
    if (!iconImgData) {
      BApi.file.getIconData({
        type,
        path,
      }).then(r => {
        iconsDispatchers.add({ [cacheKey]: r.data });
        setIconImgData(r.data);
      });
    }
    // console.log(ext, icons);
    // if (!iconImgData && !type) {
    //   if (ext in icons) {
    //     setIconImgData(icons[ext]);
    //   } else {
    //     const isCompressedFileContent = path!.includes('!');
    //     if (!isCompressedFileContent) {
    //       GetIconData({
    //         path,
    //       })
    //         .invoke((t) => {
    //           if (!t.code) {
    //             iconsDispatchers.add({ [ext]: t.data });
    //             setIconImgData(t.data);
    //           }
    //         });
    //     }
    //   }
    // }
  }, []);

  return (
    <div
      className={'file-system-entry-icon'}
      style={{
        width: size,
        height: size,
        minWidth: size,
        minHeight: size,
      }}
    >
      {/* {type == 'directory' ? ( */}
      {/*   <svg aria-hidden="true"> */}
      {/*     <use xlinkHref="#icon-folder1" /> */}
      {/*   </svg> */}
      {/* ) : */}
      {iconImgData ? (
        <img src={iconImgData} alt={''} />
      ) : (
        <CustomIcon
          type={'question-circle'}
          style={{
            color: '#ccc',
            fontSize: size,
          }}
          title={i18n.t('Unknown file type')}
        />
      )}
    </div>
  );
};
