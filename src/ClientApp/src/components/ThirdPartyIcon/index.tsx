import { useTranslation } from 'react-i18next';
import { AiOutlineQuestionCircle } from 'react-icons/ai';
import NameIcon from '@/pages/Downloader/components/NameIcon';
import { ThirdPartyId } from '@/sdk/constants';

type Props = {
  thirdPartyId: ThirdPartyId;
};

export default ({ thirdPartyId }: Props) => {
  const { t } = useTranslation();

  const img = NameIcon[thirdPartyId];
  if (!img) {
    return (
      <AiOutlineQuestionCircle className={'text-medium'} />
    );
  }
  return (
    <img
      className={'max-w-[32px] max-h-[32px]'}
      src={img}
      alt={t(`ThirdPartyId.${ThirdPartyId[thirdPartyId]}`)}
    />
  );
};
