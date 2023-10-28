import { Balloon, Table } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Component from './components/Component';
import CustomIcon from '@/components/CustomIcon';
import Title from '@/components/Title';
import BApi from '@/sdk/BApi';
import store from '@/store';

export default () => {
  const { t } = useTranslation();
  const componentContexts = store.useModelState('dependentComponentContexts');

  useEffect(() => {
  }, []);


  return (
    <div className="group">
      <Title title={t('Dependent components')} />
      <div className="settings">
        <Table
          dataSource={componentContexts}
          size={'small'}
          hasHeader={false}
          cellProps={(r, c) => {
            return {
              className: c == 0 ? 'key' : c == 1 ? 'value' : '',
            };
          }}
        >
          <Table.Column
            width={300}
            dataIndex={'name'}
            cell={(name, i, c) => {
              return (
                <>
                  {name}
                  <CustomIcon size={'small'} type={'question-circle'} title={`${t('Default location')}: ${c.defaultLocation}`} />
                </>
              );
            }}
          />
          <Table.Column
            dataIndex={'id'}
            cell={(id) => (
              <Component id={id} />
            )}
          />
        </Table>
      </div>
    </div>
  );
};
