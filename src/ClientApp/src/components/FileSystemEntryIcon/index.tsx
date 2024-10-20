import React, { useEffect, useState } from 'react';
import i18n from 'i18next';
import store from '@/store';
import { GetIconData } from '@/sdk/apis';
import CustomIcon from '@/components/CustomIcon';
import './index.scss';

export default ({
                  path,
                  showAsDirectory = false,
                  size = 14,
                }: { path?: string; showAsDirectory?: boolean; size?: number | string }) => {
  const parts = path?.split('.') ?? [];
  const ext = parts.length <= 1 ? '' : `.${parts[parts.length - 1]}`;

  const [icons, iconsDispatchers] = store.useModel('icons');
  const [iconImgData, setIconImgData] = useState(icons[ext]);

  // console.log(icons);

  useEffect(() => {
    // console.log(ext, icons);
    if (!iconImgData && !showAsDirectory) {
      if (ext in icons) {
        setIconImgData(icons[ext]);
      } else {
        const isCompressedFileContent = path!.includes('!');
        if (!isCompressedFileContent) {
          GetIconData({
            path,
          })
            .invoke((t) => {
              if (!t.code) {
                iconsDispatchers.add({ [ext]: t.data });
                setIconImgData(t.data);
              }
            });
        }
      }
    }
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
      {showAsDirectory ? (
        <svg aria-hidden="true">
          <use xlinkHref="#icon-folder1" />
        </svg>
      ) : iconImgData ? (
        <img src={iconImgData} />
      ) : (
        <CustomIcon
          type={'question-circle'}
          style={{ color: '#ccc', fontSize: size }}
          title={i18n.t('Unknown file type')}
        />
      )}
    </div>
  );
};
