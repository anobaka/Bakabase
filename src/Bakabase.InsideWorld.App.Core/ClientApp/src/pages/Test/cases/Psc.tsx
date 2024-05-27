import React, { useCallback, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import PathSegmentsConfiguration, { PathSegmentConfigurationPropsMatcherOptions } from '@/components/PathSegmentsConfiguration';
import SegmentMatcherConfiguration, {
  SegmentMatcherConfigurationModesData,
} from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration';
import { Button, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IPscPropertyMatcherValue } from '@/components/PathSegmentsConfiguration/models/PscPropertyMatcherValue';
import { convertToPscValueFromPathConfigurationDto } from '@/components/PathSegmentsConfiguration/helpers';
import { PscPropertyType } from '@/components/PathSegmentsConfiguration/models/PscPropertyType';

const testData = {
  path: 'D:/test',
  rpmValues: [
    { layer: 3, propertyId: ResourceProperty.Resource, isReservedProperty: true, valueType: ResourceMatcherValueType.Layer },
    { regex: '^[^\\/]+\\/[^\\/]+\\/[^\\/]+\\/[^\\/]+$', propertyId: ResourceProperty.ParentResource, isReservedProperty: true, valueType: ResourceMatcherValueType.Regex },
  ],
};

export default () => {
  const { t } = useTranslation();
  const [samplePath, setSamplePath] = useState('D:\\test\\new-media-library-path-configuration\\a\\bc\\New Text Document.txt');
  const [value, setValue] = useState<IPscPropertyMatcherValue[]>(convertToPscValueFromPathConfigurationDto(testData));
  const segmentsRef = useRef(samplePath.split('\\'));
  const { createPortal } = useBakabaseContext();

  const simpleMatchers = {
    [PscPropertyType.Resource]: false,
    [PscPropertyType.RootPath]: false,
    [PscPropertyType.ParentResource]: false,
    [PscPropertyType.CustomProperty]: false,
  };
  const matchers = Object.keys(simpleMatchers)
    .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
      ts.push(new PathSegmentConfigurationPropsMatcherOptions({
        propertyType: parseInt(t, 10),
        readonly: simpleMatchers[t],
      }));
      return ts;
    }, []);

  console.log(matchers, value);

  const matchersRef = useRef(matchers);
  const onChangeCallback = useCallback(v => {
    setValue(v);
  }, []);
  const valueRef = useRef(value);

  return (
    <div className={'test-page'}>
      <div className="psc">
        <PathSegmentsConfiguration
          isDirectory={false}
          segments={segmentsRef.current}
          matchers={matchersRef.current}
          defaultValue={valueRef.current}
          onChange={onChangeCallback}
        />
        <div className="values">
          <div className="matcher">
            <div className="label">Raw</div>
            <div className="value">
              {JSON.stringify(value)}
            </div>
          </div>
          {Object.keys(value).map(m => {
            const t = parseInt(m);
            let v;
            if (t == ResourceProperty.RootPath || t == ResourceProperty.Resource) {
              v = value[t][0];
            } else {
              v = JSON.stringify(value[m]);
            }
            return (
              <div className={'matcher'}>
                <div className="label">{ResourceProperty[m]}</div>
                <div className="value">{v}</div>
              </div>
            );
          })}
        </div>
      </div>
      <Button
        size={'sm'}
        onClick={() => {
          createPortal(SegmentMatcherConfiguration, {
            segments: samplePath.split('\\'),
            segmentIndex: 2,
            property: {
              id: 1,
              isReserved: true,
              name: t('Resource'),
            },
            modesData: new SegmentMatcherConfigurationModesData(
              {
                layers: [
                  1,
                ],
                regex: {
                  text: 'new-media-library-path-configuration/a/bc/New Text Document.txt',
                },
              },
            ),
            onSubmit: value => {
              // selectMatcher(visibleMatchers.find(t => t.property == m.property)!, value);
            },
          });
      }}
      >
        SMC
      </Button>
    </div>
  );
};
