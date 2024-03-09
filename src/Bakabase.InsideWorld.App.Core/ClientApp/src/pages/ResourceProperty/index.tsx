import { useEffect, useState } from 'react';
import { Tab } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import './index.scss';
import SimpleLabel from '@/components/SimpleLabel';

export default () => {
  const { t } = useTranslation();

  const [publishers, setPublishers] = useState<{ name: string }[]>([]);
  const [originals, setOriginals] = useState<{ name: string }[]>([]);
  const [series, setSeries] = useState<{ name: string }[]>([]);
  const [customProperties, setCustomProperties] = useState<{ name: string }[]>([]);

  useEffect(() => {
    loadAllData();
  }, []);

  const loadAllData = async () => {
    const pr = await BApi.publisher.getAllPublishers();
    setPublishers(pr.data || []);

    const o = await BApi.resource.getAllOriginals();
    setOriginals(o.data || []);

    const s = await BApi.resource.getAllSeries();
    setSeries(s.data || []);

    const cp = await BApi.resource.getAllCustomProperties();
    const cpData = cp.data || {};
    setCustomProperties(Object.keys(cpData).map((key) => ({
      name: `${key}:${cpData[key]!.sort((a, b) => (a.index ?? 0) - (b.index ?? 0)).map(x => x.value).join(',')}`,
    })));
  };

  const allData = [
    {
      title: t('Publishers'),
      data: publishers,
    },
    {
      title: t('Originals'),
      data: originals,
    },
    {
      title: t('Series'),
      data: series,
    },
    {
      title: t('Custom properties'),
      data: customProperties,
    },
  ];

  return (
    <div className={'resource-property-page'}>
      <div className="top-tip">
        {t('This page is a temporarily page, it may be removed in the future')}
      </div>
      <Tab
        shape="wrapped"
        tabPosition="left"
        size="small"
      >
        {allData.map((list, index) => {
          let { title } = list;
          if (list.data.length > 0) {
            title += `(${list.data.length})`;
          }
          return (
            <Tab.Item key={index} title={title}>
              {list.data.map((item, index2) => (
                <SimpleLabel key={index2}>
                  {item.name}
                </SimpleLabel>
              ))}
            </Tab.Item>
          );
        })}
      </Tab>
    </div>
  );
};
