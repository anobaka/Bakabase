import { useTranslation } from 'react-i18next';
import { Balloon, Button, Checkbox } from '@alifd/next';
import React, { useEffect, useRef } from 'react';
import type { ButtonProps } from '@alifd/next/types/button';
import store from '@/store';
import BApi from '@/sdk/BApi';
import CustomIcon from '@/components/CustomIcon';
import { CoverSaveLocation } from '@/sdk/constants';

interface IProps {
  disabledReason?: string;
  isSingleFileResource: boolean;
  getDataURL: () => string;
  onSaveAsNewCover: (base64String: string, saveTarget?: CoverSaveLocation) => any;
}

export default (props: IProps) => {
  const { t } = useTranslation();
  const resourceOptions = store.useModelState('resourceOptions');

  // @ts-ignore
  const preferredSaveTarget: CoverSaveLocation = resourceOptions.coverOptions?.saveLocation;
  const {
    disabledReason,
    isSingleFileResource,
    getDataURL,
    onSaveAsNewCover,
  } = props;
  const disabled = !!disabledReason;

  const rememberSaveLocationRef = useRef(resourceOptions.coverOptions?.saveLocation != undefined);

  useEffect(() => {
    rememberSaveLocationRef.current = resourceOptions.coverOptions?.saveLocation != undefined;
  }, [resourceOptions.coverOptions?.saveLocation]);

  const buildButton = (listenOnClick: boolean, saveLocation?: CoverSaveLocation, text: string = 'Save as a new cover', otherProps?: ButtonProps) => {
    const onClick = listenOnClick ? async () => {
      if (rememberSaveLocationRef.current) {
        await BApi.options.patchResourceOptions({
          coverOptions: {
            ...(resourceOptions.coverOptions || {}),
            // @ts-ignore
            saveLocation,
          },
        });
      }
      const data = getDataURL();
      onSaveAsNewCover(data, saveLocation);
    } : undefined;

    return (
      <Button
        type={'normal'}
        disabled={disabled}
        {...(otherProps || {})}
        onClick={onClick}
      >
        <CustomIcon type={'image-redo'} />
        {t(text)}
      </Button>
    );
  };

  if (disabled) {
    return buildButton(false);
  } else {
    if (isSingleFileResource) {
      return (
        <Balloon.Tooltip
          align={'t'}
          trigger={(
            buildButton(true, CoverSaveLocation.TempDirectory)
          )}
        >
          {t('Cover of single-file-resource will be saved to temp folder')}
        </Balloon.Tooltip>
      );
    } else {
      const tmpDirBtn = (
        <Balloon.Tooltip
          trigger={(
            buildButton(true, CoverSaveLocation.TempDirectory, 'Save to temp folder', { type: 'secondary' })
          )}
          v2
          triggerType={'hover'}
          align={'t'}
        >
          {t('Cover file may be lost if you change the resource name in file system')}
        </Balloon.Tooltip>
      );
      const resourceDirBtn = (
        <Balloon.Tooltip
          trigger={(
            buildButton(true, CoverSaveLocation.ResourceDirectory, 'Save to resource folder', { type: 'normal' })
          )}
          v2
          triggerType={'hover'}
          align={'t'}
        >
          {t('Your original resource files may be tainted because the cover file will be added to the resource folder')}
        </Balloon.Tooltip>
      );
      if (preferredSaveTarget == undefined) {
        return (
          <Balloon
            autoFocus={false}
            v2
            align={'t'}
            closable={false}
            trigger={(
              buildButton(false, undefined)
            )}
          >
            <div style={{ marginBottom: 10 }}>
              <Button.Group>
                {tmpDirBtn}
                {resourceDirBtn}
              </Button.Group>
            </div>
            <Balloon.Tooltip
              trigger={(
                <Checkbox label={t('Remember my choice')} onChange={c => rememberSaveLocationRef.current = c} />
              )}
              triggerType={'hover'}
              v2
              align={'t'}
            >
              {t('You can change this behavior in configuration page')}
            </Balloon.Tooltip>
          </Balloon>
        );
      } else {
        switch (preferredSaveTarget) {
          case CoverSaveLocation.TempDirectory: {
            return tmpDirBtn;
          }
          case CoverSaveLocation.ResourceDirectory: {
            return resourceDirBtn;
          }
        }
      }
    }
  }
};
