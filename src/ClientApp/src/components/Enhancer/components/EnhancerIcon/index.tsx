import { FieldStringOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { EnhancerId } from '@/sdk/constants';
import DLsite from '@/assets/logo/dlsite.png';
import Bangumi from '@/assets/logo/bangumi.png';
import ExHentai from '@/assets/logo/exhentai.png';
import Bakabase from '@/assets/logo/bakabase.png';

type Props = {
  id: EnhancerId;
};

export default ({ id }: Props) => {
  switch (id) {
    case EnhancerId.Bakabase:
      return (
        <img src={Bakabase} className={'max-h-[16px]'} height={16} alt={''} />
      );
    case EnhancerId.ExHentai:
      return (
        <img src={ExHentai} className={'max-h-[16px]'} height={16} alt={''} />
      );
    case EnhancerId.Bangumi:
      return (
        <img src={Bangumi} className={'max-h-[16px]'} height={16} alt={''} />
      );
    case EnhancerId.DLsite:
      return (
        <img src={DLsite} className={'max-h-[16px]'} height={16} alt={''} />
      );
    case EnhancerId.Regex:
      return (
        <FieldStringOutlined className={'text-base'} />
      );
  }
  return (
    <ThunderboltOutlined className={'text-base'} />
  );
};
