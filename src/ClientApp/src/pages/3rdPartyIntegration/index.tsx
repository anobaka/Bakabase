import { useTranslation } from 'react-i18next';
import { GrInstallOption } from 'react-icons/gr';
import { Accordion, AccordionItem, Button } from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  return (
    <Accordion variant="splitted" defaultSelectedKeys={'all'}>
      <AccordionItem key="SoulPlus" title="SoulPlus">
        <div>
          <div className={'flex items-center gap-2'}>
            <div>{t('Tampermonkey script')}</div>
            <Button variant={'light'} color={'primary'}>
              <GrInstallOption className={'text-medium'} />
              {t('One-click installation')}
            </Button>
          </div>
        </div>
      </AccordionItem>
      <AccordionItem key="ExHentai" title="ExHentai">
        <div>
          <div className={'flex items-center gap-2'}>
            <div>{t('Tampermonkey script')}</div>
            <Button variant={'light'} color={'primary'}>
              <GrInstallOption className={'text-medium'} />
              {t('One-click installation')}
            </Button>
          </div>
        </div>
      </AccordionItem>
    </Accordion>
  );
};
