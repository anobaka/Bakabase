import IceLabel from '@icedesign/label';
import React, { useCallback, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Dialog } from '@alifd/next';
import { ResourceProperty } from '@/sdk/constants';
import { createPortalOfComponent } from '@/components/utils';

import './index.scss';
import CustomIcon from '@/components/CustomIcon';

interface IProps {
  // todo: typo
  testResult: {
    entries: ResultEntry[];
    rootPath: string;
  };
  afterClose?: any;
}

interface ResultEntry {
  isDirectory: boolean;
  relativePath: string;
  segmentAndMatchedValues: {
    value: string;
    properties: {
      property: ResourceProperty;
      keys: string[];
    }[];
  }[];
  globalMatchedValues: {
    property: ResourceProperty;
    key?: string;
    values: string[];
  }[];
}

const ValidationResult = (props: IProps) => {
  const { t } = useTranslation();

  const {
    testResult,
    afterClose,
  } = props;

  const [visible, setVisible] = useState(true);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  return (
    <Dialog
      afterClose={afterClose}
      v2
      title={t('Found top {{count}} resources. (shows up to 100 results)', { count: (testResult.entries || []).length })}
      width={'auto'}
      visible={visible}
      onClose={close}
      onCancel={close}
      closeMode={['close', 'esc', 'mask']}
      footerActions={['cancel']}
      cancelProps={{
        children: t('Close'),
      }}
    >
      <section className="test-result">
        {testResult.entries?.length > 0 && (
          <div className="entries">
            {
              testResult.entries.map((e, i) => {
                const {
                  globalMatchedValues,
                  isDirectory,
                  segmentAndMatchedValues,
                } = e;

                console.log(globalMatchedValues);

                const segments: any[] = [];
                for (let j = 0; j < segmentAndMatchedValues?.length; j++) {
                  const ps = segmentAndMatchedValues[j];
                  const matchLabels: string[] = [];
                  if (j == segmentAndMatchedValues.length - 1) {
                    matchLabels.push(t(ResourceProperty[ResourceProperty.Resource]));
                  }
                  if (ps.properties?.length > 0) {
                    for (const p of ps.properties) {
                      if (p.property == ResourceProperty.CustomProperty) {
                        matchLabels.push(`${t('Custom property')}:${p.keys.join(',')}`);
                      } else {
                        matchLabels.push(t(ResourceProperty[p.property]));
                      }
                    }
                  }
                  // console.log(types, ps);
                  segments.push(
                    <div className={'segment'}>
                      {matchLabels.length > 0 && (
                        <div className={`types ${matchLabels.length > 1 ? 'conflict' : ''}`}>
                          {matchLabels.join(', ')}
                        </div>
                      )}
                      <div className="value">{ps.value}</div>
                    </div>,
                  );
                  if (j != e.segmentAndMatchedValues.length - 1) {
                    segments.push(
                      <span className={'path-separator'}>/</span>,
                    );
                  }
                }

                const globalMatchesElements: any[] = [];
                if (globalMatchedValues.length > 0) {
                  for (const gmv of globalMatchedValues) {
                    const { property, key, values } = gmv;
                    const propertyLabel = property == ResourceProperty.CustomProperty ? `${t('Custom property')}:${key}` : t(ResourceProperty[property]);
                    if (values?.length > 0) {
                      globalMatchesElements.push(
                        <div className={'gm'}>
                          <div className="property">{propertyLabel}</div>
                          <div className="values">
                            {values.map(v => (
                              <IceLabel inverse={false} status={'default'}>{v}</IceLabel>
                            ))}
                          </div>
                        </div>,
                      );
                    }
                  }
                }

                // if (fixedTags) {
                //   if (fixedTags.length > 0) {
                //     globalMatchesElements.push(
                //       <div className={'gm'}>
                //         <div className="property">{t('Fixed tags')}</div>
                //         <div className="values">
                //           {fixedTags.map(t => (
                //             <IceLabel inverse={false} status={'default'}>{t.displayName}</IceLabel>
                //           ))}
                //         </div>
                //       </div>,
                //     );
                //   }
                // }

                return (
                  <div className={'entry'}>
                    <div className="no">
                      <IceLabel status={'default'} inverse={false} className="no">{i + 1}</IceLabel>
                    </div>
                    <div className="result">
                      <div className="segments">
                        <CustomIcon type={isDirectory ? 'folder' : 'file'} size={'small'} />
                        {/* <IceLabel */}
                        {/*   className="fs-type" */}
                        {/*   inverse={false} */}
                        {/*   status={isDirectory ? 'success' : 'info'} */}
                        {/* >{t(isDirectory ? 'Directory' : 'File')}</IceLabel> */}
                        {segments}
                      </div>
                      {globalMatchesElements.length > 0 && (
                        <div className="global-matches">
                          {globalMatchesElements}
                        </div>
                      )}
                    </div>
                  </div>
                );
              })
            }
          </div>
        )}
      </section>
    </Dialog>
  );
};

ValidationResult.show = (props: IProps) => createPortalOfComponent(ValidationResult, props);

export default ValidationResult;
