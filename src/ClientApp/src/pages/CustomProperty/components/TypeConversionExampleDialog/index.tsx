import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { Modal, Tab, Table, TableBody, TableCell, TableColumn, TableHeader, TableRow, Tabs } from '@/components/bakaui';
import type { StandardValueType } from '@/sdk/constants';
import { PropertyType } from '@/sdk/constants';
import { propertyTypes } from '@/sdk/constants';
import StandardValueRenderer from '@/components/StandardValue/ValueRenderer';
import { deserializeStandardValue } from '@/components/StandardValue/helpers';
import BApi from '@/sdk/BApi';

type Result = {
  type: PropertyType;
  serializedBizValue?: string;
  bizValueType: StandardValueType;
  outputs?: {
    type: PropertyType;
    serializedBizValue?: string;
    bizValueType: StandardValueType;
  }[];
};

export default () => {
  const { t } = useTranslation();

  const [results, setResults] = useState<Result[]>([]);

  const columns = [
    // <TableColumn>{t('Type to be converted')}</TableColumn>,
    <TableColumn>{t('Value to be converted')}</TableColumn>,
    ...propertyTypes.map(cpt => {
      return (
        <TableColumn>{t(PropertyType[cpt.value])}</TableColumn>
      );
    }),
  ];

  useEffect(() => {
    BApi.customProperty.testCustomPropertyTypeConversion().then(r => {
      // @ts-ignore
      setResults(r.data?.results ?? []);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      title={t('Type conversion examples')}
      size={'full'}
      footer={{
        actions: ['cancel'],
        cancelProps: {
          children: t('Close'),
        },
      }}
    >
      <div>
        <Tabs isVertical disabledKeys={['title']}>
          <Tab key={'title'} title={t('Source type')} />
          {propertyTypes.map(cpt => {
            const filteredResults = results?.filter(x => x.type == cpt.value) || [];
            return (
              <Tab key={cpt.value} title={t(PropertyType[cpt.value])} className={'w-full'}>
                <Table isCompact isStriped removeWrapper isHeaderSticky>
                  <TableHeader>
                    {columns}
                  </TableHeader>
                  <TableBody>
                    {filteredResults.map((td, i) => {
                      const cells = [
                        // <TableCell>{t(PropertyType[td.type])}</TableCell>,
                        <TableCell>
                          <StandardValueRenderer
                            type={td.bizValueType}
                            value={deserializeStandardValue(td.serializedBizValue ?? null, td.bizValueType)}
                            variant={'default'}
                            propertyType={td.type}
                          />
                        </TableCell>,
                      ];
                      propertyTypes.forEach(type => {
                        const o = filteredResults?.[i]?.outputs?.find(o => o.type == type.value);
                        if (o) {
                          const deserializedValue = deserializeStandardValue(o.serializedBizValue ?? null, o.bizValueType);
                          console.log(o, deserializedValue);
                          cells.push(
                            <TableCell>
                              <StandardValueRenderer
                                type={o.bizValueType}
                                value={deserializedValue}
                                variant={'default'}
                                propertyType={type.value}
                              />
                            </TableCell>,
                          );
                        } else {
                          cells.push(
                            <TableCell>/</TableCell>,
                          );
                        }
                      });

                      // console.log(cells);

                      return (
                        <TableRow key={i}>
                          {cells}
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </Tab>
            );
          })}
        </Tabs>
      </div>
    </Modal>
  );
};
