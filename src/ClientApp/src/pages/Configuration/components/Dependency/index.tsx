import { Balloon } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { QuestionCircleOutlined } from '@ant-design/icons';
import Component from './components/Component';
import CustomIcon from '@/components/CustomIcon';
import Title from '@/components/Title';
import BApi from '@/sdk/BApi';
import store from '@/store';
import {
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  Table,
  Popover,
  Snippet,
} from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  const componentContexts = store.useModelState('dependentComponentContexts');

  useEffect(() => {
  }, []);


  return (
    <div className="group">
      {/* <Title title={t('Dependent components')} /> */}
      <div className="settings">
        <Table
          removeWrapper
        >
          <TableHeader>
            <TableColumn width={200}>{t('Dependent components')}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {componentContexts.map((c, i) => {
              return (
                <TableRow key={i} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
                  <TableCell>
                    <div className={'flex gap-1 items-center'}>
                      {c.name}
                      <Popover
                        // color={'primary'}
                        showArrow
                        trigger={(
                          <QuestionCircleOutlined className={'text-base'} />
                        )}
                        placement={'right'}
                      >
                        <div style={{ userSelect: 'text' }} className={'px-2 py-4 flex flex-col gap-2'}>
                          {c.description && (
                            <pre>
                              {c.description}
                            </pre>
                          )}
                          <div className={'flex items-center gap-2'}>
                            {t('Default location')}
                            <Snippet
                              size={'sm'}
                              variant="bordered"
                              hideSymbol
                            >
                              {c.defaultLocation}
                            </Snippet>
                          </div>
                        </div>

                      </Popover>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Component id={c.id} />
                  </TableCell>
                </TableRow>
              );
            })}

          </TableBody>
        </Table>
        {/* <Table */}
        {/*   dataSource={componentContexts} */}
        {/*   size={'small'} */}
        {/*   hasHeader={false} */}
        {/*   cellProps={(r, c) => { */}
        {/*     return { */}
        {/*       className: c == 0 ? 'key' : c == 1 ? 'value' : '', */}
        {/*     }; */}
        {/*   }} */}
        {/* > */}
        {/*   <Table.Column */}
        {/*     width={300} */}
        {/*     dataIndex={'name'} */}
        {/*     cell={(name, i, c) => { */}
        {/*       return ( */}
        {/*         <> */}
        {/*           {name} */}
        {/*           <Balloon */}
        {/*             trigger={( */}
        {/*               <CustomIcon size={'small'} type={'question-circle'} /> */}
        {/*             )} */}
        {/*             triggerType={'click'} */}
        {/*             align={'r'} */}
        {/*           > */}
        {/*             <div style={{ userSelect: 'text' }}> */}
        {/*               {c.description && ( */}
        {/*                 <div> */}
        {/*                   {c.description} */}
        {/*                 </div> */}
        {/*               )} */}
        {/*               <div> */}
        {/*                 {t('Default location')}: {c.defaultLocation} */}
        {/*               </div> */}
        {/*             </div> */}

        {/*           </Balloon> */}
        {/*         </> */}
        {/*       ); */}
        {/*     }} */}
        {/*   /> */}
        {/*   <Table.Column */}
        {/*     dataIndex={'id'} */}
        {/*     cell={(id) => ( */}
        {/*       <Component id={id} /> */}
        {/*     )} */}
        {/*   /> */}
        {/* </Table> */}
      </div>
    </div>
  );
};
