import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { Divider, Listbox, ListboxItem, ListboxSection, Spinner } from '@/components/bakaui';
import { ResourceProperty } from '@/sdk/constants';
import type { MigrationTarget } from '@/pages/Migration/components/Target';
import Target from '@/pages/Migration/components/Target';
import BApi from '@/sdk/BApi';

export default () => {
  const { t } = useTranslation();

  const [allMigrationsAreDone, setAllMigrationsAreDone] = useState(false);

  const [targets, setTargets] = useState<MigrationTarget[]>([]);
  const [loadingTargets, setLoadingTargets] = useState(false);

  const loadTargets = async () => {
    setLoadingTargets(true);
    try {
      const rsp = await BApi.migration.getMigrationTargets();
      const ts = rsp.data || [];
      // @ts-ignore
      setTargets(ts);
      if (ts.length == 0) {
        setAllMigrationsAreDone(true);
      }
    } finally {
      setLoadingTargets(false);
    }
  };

  useEffect(() => {
    loadTargets();
  }, []);


  return (
    <div className={'h-full min-h-full max-h-full overflow-auto flex flex-col'}>
      {loadingTargets ? (
        <div className={'min-h-full flex items-center justify-center text-2xl'}>
          <Spinner />
        </div>
      ) : allMigrationsAreDone ? (
        <div className={'min-h-full flex items-center justify-center text-2xl'}>
          {t('Congratulations, you have migrated all data.')}
        </div>
      ) : (
        <>
          <div>
            {/* <div className={'flex gap-3 flex-1 min-h-0'}> */}
            {/* <div className="left rounded border-1 overflow-auto pt-1 pl-1 pb-1 pr-2 min-w-[120px]"> */}
            {/*   <Listbox */}
            {/*     variant="flat" */}
            {/*   > */}
            {/*     <ListboxSection title={t('Publishers')} showDivider> */}
            {/*       {[1, 2, 3, 4, 5].map(depth => { */}
            {/*         return ( */}
            {/*           <ListboxItem */}
            {/*             key={depth} */}
            {/*           > */}
            {/*             {t('With {{depth}} layers', { depth })} */}
            {/*           </ListboxItem> */}
            {/*         ); */}
            {/*       })} */}
            {/*     </ListboxSection> */}
            {/*     <ListboxItem key="original"> */}
            {/*       {t('Original')} */}
            {/*     </ListboxItem> */}
            {/*     <ListboxItem key="series"> */}
            {/*       {t('Series')} */}
            {/*     </ListboxItem> */}
            {/*     <ListboxSection title={t('Volume')} showDivider> */}
            {/*       <ListboxItem key="names"> */}
            {/*         {t('Name')} */}
            {/*       </ListboxItem> */}
            {/*       <ListboxItem key="titles"> */}
            {/*         {t('Title')} */}
            {/*       </ListboxItem> */}
            {/*       <ListboxItem key="indexes"> */}
            {/*         {t('Index')} */}
            {/*       </ListboxItem> */}
            {/*     </ListboxSection> */}
            {/*     <ListboxSection title={t('Custom properties')} showDivider> */}
            {/*       {['声优', '画师', '作者', '出版社', '标签', '分类', '状态', '进度', '评分', '收藏'].map((item) => ( */}
            {/*         <ListboxItem key={item}> */}
            {/*           {item} */}
            {/*         </ListboxItem> */}
            {/*       ))} */}
            {/*     </ListboxSection> */}
            {/*     <ListboxItem key="favorites"> */}
            {/*       {t('Favorites')} */}
            {/*     </ListboxItem> */}
            {/*   </Listbox> */}
            {/* </div> */}
            {/* <div> */}
            {targets.map(t => (
              <>
                <Target
                  target={t}
                  isLeaf={!t.subTargets}
                  onMigrated={loadTargets}
                />
                <Divider />
              </>
            ))}
            {/* </div> */}
          </div>
        </>
      )}
    </div>
  );
};
