import React, { useCallback, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import PathSegmentsConfiguration, { PathSegmentConfigurationPropsMatcherOptions } from '@/components/PathSegmentsConfiguration';
import SegmentMatcherConfiguration, {
  SegmentMatcherConfigurationModesData,
} from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration';
import { Button, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const [samplePath, setSamplePath] = useState('D:\\test\\new-media-library-path-configuration\\a\\bc\\New Text Document.txt');
  const [value, setValue] = useState<IPscValue>({});
  const segmentsRef = useRef(samplePath.split('\\'));
  const { createPortal } = useBakabaseContext();

  const simpleMatchers = {
    [ResourceProperty.Resource]: false,
    [ResourceProperty.RootPath]: false,
    [ResourceProperty.ParentResource]: false,
    // [ResourceProperty.ReleaseDt]: false,
    // [ResourceProperty.Publisher]: false,
    // [ResourceProperty.Name]: false,
    // [ResourceProperty.Language]: false,
    // [ResourceProperty.Volume]: false,
    // [ResourceProperty.Original]: false,
    // [ResourceProperty.Series]: false,
    // [ResourceProperty.Tag]: false,
    // [MatcherType.Introduction]: false,
    // [ResourceProperty.Rate]: false,
    [ResourceProperty.CustomProperty]: false,
  };
  const matchers = Object.keys(simpleMatchers)
    .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
      ts.push(new PathSegmentConfigurationPropsMatcherOptions({
        property: parseInt(t, 10),
        readonly: simpleMatchers[t],
      }));
      return ts;
    }, []);

  console.log(matchers);

  const matchersRef = useRef(matchers);
  const onChangeCallback = useCallback(v => {
    setValue(v);
  }, []);
  const valueRef = useRef<IPscValue>({
    path: 'D:/test',
    rpmValues: [
      { layer: 3, property: ResourceProperty.Resource, valueType: ResourceMatcherValueType.Layer },
      { regex: '^[^\\/]+\\/[^\\/]+\\/[^\\/]+\\/[^\\/]+$', property: ResourceProperty.ParentResource, valueType: ResourceMatcherValueType.Regex },
    ],
  });

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
