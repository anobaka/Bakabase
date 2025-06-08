import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { QuestionCircleOutlined } from '@ant-design/icons';
import { AiOutlineCheck, AiOutlineClose, AiOutlineEdit } from 'react-icons/ai';
import type { InputProps } from '@heroui/react';
import Component from './components/Component';
import store from '@/store';
import {
  Button,
  Input,
  Popover,
  Snippet,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import EditableInput from '@/components/EditableValue/components/EditableSnippetWithInput';
import { EditableValue } from '@/components/EditableValue';

export default () => {
  const { t } = useTranslation();
  const componentContexts = store.useModelState('dependentComponentContexts');
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');
  const aiOptions = store.useModelState('aiOptions');

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
            <>
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
            </>
            <TableRow key={componentContexts.length} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
              <TableCell>
                {t('curl')}
              </TableCell>
              <TableCell>
                <EditableValue<InputProps, InputProps, string>
                  Viewer={({
                             value,
                             ...props
                           }) => (<Snippet symbol={<>&nbsp;</>} {...props}>{value}</Snippet>)}
                  Editor={Input}
                  onSubmit={async v => await BApi.options.putThirdPartyOptions({
                    ...thirdPartyOptions,
                    curlExecutable: v,
                  })}
                  value={thirdPartyOptions.curlExecutable}
                />
              </TableCell>
            </TableRow>
            <TableRow key={componentContexts.length + 1} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
              <TableCell>
                {t('Ollama endpoint')}
              </TableCell>
              <TableCell>
                <EditableInput
                  onSubmit={async v => await BApi.options.putAiOptions({
                    ...aiOptions,
                    ollamaEndpoint: v,
                  })}
                  value={aiOptions.ollamaEndpoint}
                />
              </TableCell>
            </TableRow>
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
