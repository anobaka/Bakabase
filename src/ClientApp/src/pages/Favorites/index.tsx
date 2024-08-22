import React, { useEffect, useState } from 'react';
import { Button, Dialog, Input, Message, Table } from '@alifd/next';
import i18n from 'i18next';
import { AddFavorites, DeleteFavorites, GetAllFavorites, PutFavorites } from '@/sdk/apis';
import './index.scss';
import ConfirmationButton from '@/components/ConfirmationButton';
import FeatureStatusTip from '@/components/FeatureStatusTip';
import { useTranslation } from 'react-i18next';

export default () => {
  const { t } = useTranslation();

  const [favorites, setFavorites] = useState([]);
  const [editingFavorites, setEditingFavorites] = useState();

  useEffect(() => {
    // loadAllFavorites();
  }, []);

  const loadAllFavorites = () => {
    GetAllFavorites().invoke((t) => setFavorites(t.data));
  };

  const closeDetailDialog = () => {
    setEditingFavorites(undefined);
  };

  return (
    <div className={'favorites-page'}>
      <FeatureStatusTip
        status={'deprecated'}
        name={t('Favorites')}
      />
      {/* <Dialog */}
      {/*   closeable */}
      {/*   visible={!!editingFavorites} */}
      {/*   title={i18n.t('Favorites')} */}
      {/*   onClose={closeDetailDialog} */}
      {/*   onCancel={closeDetailDialog} */}
      {/*   onOk={() => { */}
      {/*     if (!(editingFavorites?.name?.length > 0)) { */}
      {/*       return Message.error(i18n.t('Name of favorites can not be empty')); */}
      {/*     } */}
      {/*     const invoker = editingFavorites?.id > 0 ? PutFavorites({ */}
      {/*       model: editingFavorites, */}
      {/*       id: editingFavorites?.id, */}
      {/*     }) : AddFavorites({ */}
      {/*       model: editingFavorites, */}
      {/*     }); */}
      {/*     invoker.invoke((t) => { */}
      {/*       if (!t.code) { */}
      {/*         loadAllFavorites(); */}
      {/*         closeDetailDialog(); */}
      {/*       } */}
      {/*     }); */}
      {/*   }} */}
      {/*   className={'favorites-page-detail-dialog'} */}
      {/* > */}
      {/*   <div className={'form'}> */}
      {/*     <div className="label">{i18n.t('Name')}</div> */}
      {/*     <div className="value"> */}
      {/*       <Input value={editingFavorites?.name} onChange={(v) => setEditingFavorites({ ...editingFavorites, name: v })} /> */}
      {/*     </div> */}
      {/*     <div className="label">{i18n.t('Description')}</div> */}
      {/*     <div className="value"> */}
      {/*       <Input.TextArea */}
      {/*         rows={10} */}
      {/*         value={editingFavorites?.description} */}
      {/*         onChange={(v) => setEditingFavorites({ ...editingFavorites, description: v })} */}
      {/*       /> */}
      {/*     </div> */}
      {/*   </div> */}
      {/* </Dialog> */}
      {/* <div className="header"> */}
      {/*   <Button */}
      {/*     type={'secondary'} */}
      {/*     onClick={() => { */}
      {/*       setEditingFavorites({}); */}
      {/*     }} */}
      {/*     size={'small'} */}
      {/*   > */}
      {/*     {i18n.t('Add')} */}
      {/*   </Button> */}
      {/* </div> */}
      {/* <Table dataSource={favorites}> */}
      {/*   <Table.Column title={i18n.t('Name')} dataIndex={'name'} /> */}
      {/*   <Table.Column title={i18n.t('Description')} dataIndex={'description'} /> */}
      {/*   <Table.Column */}
      {/*     title={i18n.t('Operations')} */}
      {/*     dataIndex={'id'} */}
      {/*     width={'15%'} */}
      {/*     cell={(id, index, fav) => { */}
      {/*       return ( */}

      {/*         <div className={'confirmation-button-group'}> */}
      {/*           <ConfirmationButton */}
      {/*             size={'small'} */}
      {/*             type={'secondary'} */}
      {/*             onClick={() => { */}
      {/*               setEditingFavorites({ ...fav }); */}
      {/*             }} */}
      {/*             icon={'edit-square'} */}
      {/*             label={'Edit'} */}
      {/*           /> */}
      {/*           <ConfirmationButton */}
      {/*             icon={'delete'} */}
      {/*             warning */}
      {/*             onClick={(e) => { */}
      {/*               DeleteFavorites({ */}
      {/*                 id, */}
      {/*               }).invoke((t) => { */}
      {/*                 if (!t.code) { */}
      {/*                   loadAllFavorites(); */}
      {/*                 } */}
      {/*               }); */}
      {/*             }} */}
      {/*             size={'small'} */}
      {/*             confirmation */}
      {/*             label={'Delete'} */}
      {/*           /> */}
      {/*         </div> */}
      {/*       ); */}
      {/*     }} */}
      {/*   /> */}
      {/* </Table> */}
    </div>
  );
};
