import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { QuestionCircleOutlined } from '@ant-design/icons';
import TypeConversionExampleDialog from '../TypeConversionExampleDialog';
import {
  Button,
  Chip,
  Modal,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tabs,
  Tooltip,
} from '@/components/bakaui';
import type { StandardValueConversionRule } from '@/sdk/constants';
import { PropertyType, propertyTypes } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [rules, setRules] = useState<Record<number, Record<number, {
    rule: StandardValueConversionRule;
    name: string;
    description: string | null;
  }[]>>>();

  const columns = [
    // <TableColumn>{t('Source type')}</TableColumn>,
    <TableColumn>{t('Target type')}</TableColumn>,
    <TableColumn>{t('Rules')}</TableColumn>,
  ];

  useEffect(() => {
    BApi.customProperty.getCustomPropertyConversionRules().then(r => {
      // @ts-ignore
      setRules(r.data);
    });
  }, []);

  const renderRows = (fromType: PropertyType): any[] => {
    if (!rules) {
      return [];
    }

    const targetMap = rules[fromType];
    if (!targetMap) {
      return [];
    }

    const rows: any[] = [];

    Object.keys(targetMap).forEach(toTypeStr => {
      const toType = parseInt(toTypeStr, 10) as PropertyType;
      const rules = targetMap[toType];
      rows.push(
        <TableRow key={fromType}>
          {/* <TableCell>{t(PropertyType[fromType])}</TableCell> */}
          <TableCell>{t(PropertyType[toType])}</TableCell>
          <TableCell>
            <div className={'flex flex-wrap gap-1'}>
              {rules.map(r => {
                if (r.description == null) {
                  return (
                    <Chip size={'sm'}>{r.name}</Chip>
                  );
                }
                return (
                  <Tooltip
                    content={(<pre>{r.description}</pre>)}
                  >
                    <Chip size={'sm'}>
                      <div className={'flex items-center gap-1'}>
                        {r.name}
                        <QuestionCircleOutlined className={'text-sm'} />
                      </div>
                    </Chip>
                  </Tooltip>
                );
              })}
            </div>
          </TableCell>
        </TableRow>,
      );
    });
    return rows;
  };

  return (
    <Modal
      defaultVisible
      title={(
        <div className={'flex items-center gap-2'}>
          {t('Type conversion rules')}
          <Button
            size={'sm'}
            variant={'light'}
            color={'secondary'}
            onClick={() => {
              createPortal(TypeConversionExampleDialog, {});
            }}
          >
            {t('Check examples')}
          </Button>
        </div>
      )}
      size={'xl'}
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
            return (
              <Tab key={cpt.value} title={t(PropertyType[cpt.value])} className={'w-full'} >
                <Table isCompact isStriped removeWrapper isHeaderSticky>
                  <TableHeader>
                    {columns}
                  </TableHeader>
                  <TableBody>
                    {renderRows(cpt.value)}
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
